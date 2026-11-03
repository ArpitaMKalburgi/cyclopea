using System;
using System.Collections.Generic;
using System.IO;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The Spot rung end to end: four squares of static on the surface, one hides a creature carried
    /// only by disparity, the player picks it, the staircase tightens the camouflage, every trial is
    /// logged to JSONL, and the session ends with an honest Depth Score card.
    /// </summary>
    public sealed class SpotSession : MonoBehaviour
    {
        public const string SubjectKey = "cyc_subject";
        /// <summary>Set once the first-launch notice has been accepted.</summary>
        public const string NoticeKey = "cyc_notice_v1";

        /// <summary>First-launch notice. Plain language, and no medical wording anywhere in the app.</summary>
        public const string NoticeText =
            "Cyclopea is a game about depth perception.\n" +
            "It is not a medical device and gives no medical advice.\n" +
            "If you have a condition that affects your vision,\n" +
            "ask an eye-care professional before playing.\n" +
            "Play pauses for a short break after 10 minutes.";

        /// <summary>Set once the first-launch tutorial creature has been found.</summary>
        public const string TutorialKey = "cyc_tutorial_v1";

        [Header("Scene")]
        public Transform Head;
        public SurfaceFinder Surface;
        public StereoPatch[] Patches = new StereoPatch[4];
        public LineRenderer[] Frames = new LineRenderer[4];
        public PatchSelector[] Selectors;
        public TextMesh Title, Status;
        public AudioSource Audio;
        [Tooltip("Spatialised source (Meta XR Audio) moved to the creature for feedback, so you hear where it was. Falls back to Audio.")]
        public AudioSource CreatureVoice;
        [Tooltip("Found creatures come out of the static, ride your fingertip and gather in a nest; runs the tutorial creature.")]
        public CreatureDirector Creatures;

        [Header("Layout")]
        public float PatchSize = 0.12f;
        public float Gap = 0.035f;

        [Header("Timing")]
        public float FeedbackSeconds = 0.75f;
        [Tooltip("Active play (trials + feedback) before a rest break. Paused time does not count.")]
        public float CapSeconds = 600f;
        [Tooltip("Length of the rest break. It keeps counting while the headset is off.")]
        public float BreakSeconds = 120f;
        [Tooltip("Tutorial: how long the found creature is celebrated before round 1 begins.")]
        public float TutorialRewardSeconds = 2.6f;

        static readonly Color FrameIdle = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color FrameHover = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color FrameRight = new Color(0.35f, 1f, 0.55f, 1f);
        static readonly Color FrameWrong = new Color(1f, 0.35f, 0.35f, 1f);

        enum State { Intro, Tutorial, Trial, Feedback, Done, Break }

        /// <summary>What a finished session produced; other systems (score anchor, camouflage, platform) listen for it.</summary>
        public struct Result
        {
            public int Round, Trials;
            public bool Measured, AtFloor;
            public double ThresholdArcsec, ViewingDistanceM;
            public string ScoreLine;
            public SessionLog.Cue Cue;
        }

        /// <summary>
        /// What carries the creature. Stereo: disparity only (one eye sees nothing). Motion: depth shown
        /// only through head-motion parallax, the same in both eyes, so players without stereo vision
        /// (and a headset whose per-eye rendering failed) still get the game and a score.
        /// </summary>
        public SessionLog.Cue Cue { get; private set; } = SessionLog.Cue.Stereo;
        bool lastMeasured = true;

        /// <summary>Switch cue and start a new session with it.</summary>
        public void StartCue(SessionLog.Cue cue)
        {
            Cue = cue;
            Restart();
        }

        public event Action<Result> Finished;
        public event Action<int> RoundStarting;
        public int Round { get; private set; }

        State state;
        SkillLadder ladder;
        SessionLog log;
        int logged;
        string sessionId, subjectId, device, filePath;
        double floorArcsec;
        int target, trialIndex;
        float trialStart, stateUntil, toastUntil;
        DateTime utcStart;
        readonly System.Random rng = new System.Random();
        readonly List<double> distances = new List<double>();
        AudioClip clipRight, clipWrong, clipTick;
        int chosen = -1;
        float playSeconds, breakUntil, pausedAt, titleCharSize;
        int pauseFlags;
        bool tutorialHidden, tutorialFound;

        // Pause reasons; play resumes only when none is left.
        public const int PauseFocus = 1, PauseUnmounted = 2, PauseApp = 4, PauseManual = 8;

        void Awake()
        {
            clipRight = Tones.Chirp(880, 1320, 0.16f);
            clipWrong = Tones.Chirp(330, 220, 0.18f);
            clipTick = Tones.Chirp(1500, 1500, 0.03f);
        }

        void OnEnable()
        {
            foreach (var s in Selectors) if (s != null) s.Selected += OnSelected;
            if (Surface != null) Surface.SurfaceChanged += Place;
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
            OVRManager.HMDUnmounted += OnUnmounted;
            OVRManager.HMDMounted += OnMounted;
            if (Creatures != null) Creatures.Collected += OnCollected;
        }

        void OnDisable()
        {
            foreach (var s in Selectors) if (s != null) s.Selected -= OnSelected;
            if (Surface != null) Surface.SurfaceChanged -= Place;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            OVRManager.HMDUnmounted -= OnUnmounted;
            OVRManager.HMDMounted -= OnMounted;
            if (Creatures != null) Creatures.Collected -= OnCollected;
        }

        void OnCollected(Core.Creatures.Species s, Core.Creatures.Rarity r, bool isNew)
        {
            if (isNew) Toast("New in your field guide: " + Core.Creatures.Name(s, r), 1.8f);
        }

        // ---------- First-launch tutorial ----------
        // Show, don't tell: a creature appears beside the squares, climbs onto your finger if you hold
        // one out, then hops into a square and sinks into the static. You pinch it back out.

        public bool ShowingTutorial => state == State.Tutorial;
        public bool TutorialCreatureHidden => state == State.Tutorial && tutorialHidden;

        /// <summary>Replays the tutorial (dev remote; also what a first launch does after the notice).</summary>
        public void StartTutorial()
        {
            state = State.Tutorial;
            tutorialHidden = tutorialFound = false;
            chosen = -1;
            ClearPatches();
            foreach (var p in Patches) p.Reshuffle(rng);
            target = rng.Next(0, Patches.Length);
            if (Title != null)
            {
                if (titleCharSize > 0) Title.characterSize = titleCharSize;
                Title.text = "Something lives in this static.\nHold out a finger.";
            }
            Toast("", 0f);
            if (Creatures != null) Creatures.IntroBegin(Patches[target], OnTutorialDived);
            else OnTutorialDived();
        }

        void OnTutorialDived()
        {
            if (state != State.Tutorial) return;
            if (Cue == SessionLog.Cue.Stereo) Patches[target].Arcsec = 1920;
            else Patches[target].MotionArcsec = 1920;
            tutorialHidden = true;
            if (Title != null)
                Title.text = Cue == SessionLog.Cue.Motion
                    ? "It hid in the static. Sway your head to see it.\nPoint at it and pinch."
                    : "It hid in the static. See it float?\nPoint at it and pinch.";
        }

        void OnTutorialPick(StereoPatch p)
        {
            if (tutorialFound) return;
            if (!tutorialHidden) { if (Creatures != null) Creatures.IntroSkip(); return; }
            if (p.Index == target)
            {
                tutorialFound = true;
                chosen = p.Index;
                if (Creatures != null) Creatures.Reveal(p, 1920, 1920, true);
                PlayAtCreature(clipRight, p, 1920);
                if (Title != null) Title.text = "You found it!\nMore are hiding, and they get harder to see.";
                stateUntil = Time.time + TutorialRewardSeconds;
            }
            else
            {
                if (Creatures != null) Creatures.Reveal(Patches[target], 1920, 1920, false);
                PlayAtCreature(clipWrong, Patches[target], 1920);
                Toast("Not that one. Look for the square that floats.", 2.5f);
            }
        }

        // ---------- Pause / resume ----------
        // System menu, headset off, app backgrounded: the trial freezes (reaction time and the 10-minute
        // budget stop counting) and picks up exactly where it was.

        void OnFocusLost() => SetPaused(PauseFocus, true);
        void OnFocusAcquired() => SetPaused(PauseFocus, false);
        void OnUnmounted() => SetPaused(PauseUnmounted, true);
        void OnMounted() => SetPaused(PauseUnmounted, false);
        void OnApplicationPause(bool p) { if (!Application.isEditor) SetPaused(PauseApp, p); }

        public bool Paused => pauseFlags != 0;

        public void SetPaused(int reason, bool on)
        {
            bool was = Paused;
            pauseFlags = on ? pauseFlags | reason : pauseFlags & ~reason;
            if (was == Paused) return;
            if (Paused)
            {
                pausedAt = Time.time;
                if (Status != null) Status.text = "Paused";
            }
            else
            {
                float d = Time.time - pausedAt;
                trialStart += d;
                stateUntil += d;
                if (state == State.Trial || state == State.Feedback) Toast("Welcome back", 1.5f);
            }
        }

        // ---------- First-launch notice ----------

        void ShowNotice()
        {
            state = State.Intro;
            ClearPatches();
            if (Title != null)
            {
                Title.characterSize = titleCharSize * 0.7f;
                Title.text = NoticeText;
            }
            Toast("Pinch any square to start", 9999f);
        }

        // ---------- Rest break ----------

        void EnterBreak()
        {
            state = State.Break;
            breakUntil = Time.time + BreakSeconds;
            ClearPatches();
            if (Title != null)
                Title.text = "That's " + Mathf.RoundToInt(CapSeconds / 60f) + " minutes of play.\nRest your eyes for a moment.";
        }

        /// <summary>The 10-minute budget ran out mid-session: no score from a cut-short staircase.</summary>
        void StopForBreak()
        {
            log.Add(new SessionLog.Summary
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcStart = utcStart,
                Trials = trialIndex, ThresholdArcsec = double.NaN, AtFloor = false, FloorArcsec = floorArcsec,
                Eyes = DevControls.Eyes, Cue = Cue
            });
            Flush();
            lastMeasured = true;
            EnterBreak();
        }

        void ClearPatches()
        {
            foreach (var p in Patches) if (p != null) { p.Arcsec = 0; p.MotionArcsec = 0; }
        }

        // Test hooks.
        public bool OnBreak => state == State.Break;
        public bool ShowingNotice => state == State.Intro;
        public int TrialsDone => trialIndex;

        void Start()
        {
            for (int i = 0; i < Patches.Length; i++) { Patches[i].Index = i; Patches[i].Head = Head; }
            device = DeviceName(out floorArcsec);
            if (Title != null) titleCharSize = Title.characterSize;
            Place(Surface != null ? Surface.Current : default);
            if (PlayerPrefs.GetInt(NoticeKey, 0) != 1) ShowNotice();
            else if (PlayerPrefs.GetInt(TutorialKey, 0) != 1) StartTutorial();
            else Restart();
        }

        // Automation hooks (play-mode tests, XR Operator scripts): read the hidden answer, pick a square.
        public int CurrentTarget => target;
        public bool IsDone => state == State.Done;
        public bool AwaitingChoice => state == State.Trial;
        public double CurrentArcsec => ladder.CurrentArcsec;
        public string LogPath => filePath;
        public void Choose(int index, string method = "script")
        {
            if (index >= 0 && index < Patches.Length) OnSelected(Patches[index], method);
        }

        public void Restart()
        {
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + rng.Next(1000, 9999);
            subjectId = "P" + PlayerPrefs.GetInt(SubjectKey, 1).ToString("00");
            utcStart = DateTime.UtcNow;
            ladder = new SkillLadder(floorArcsec, new[]
            {
                new SkillLadder.RungConfig
                {
                    Rung = SkillLadder.Rung.Spot, Alternatives = 4,
                    Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = floorArcsec }
                }
            });
            log = new SessionLog();
            logged = 0;
            trialIndex = 0;
            distances.Clear();
            string dir = Path.Combine(Application.persistentDataPath, "sessions");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, sessionId + ".jsonl");
            Round++;
            if (Creatures != null) Creatures.BeginSession();
            RoundStarting?.Invoke(Round);
            if (Title != null && titleCharSize > 0) Title.characterSize = titleCharSize;
            if (Title != null)
                Title.text = Cue == SessionLog.Cue.Motion
                    ? "Sway your head gently from side to side.\nOne square's static slides differently. Pinch it."
                    : Round == 1
                        ? "One square hides a creature.\nOnly two eyes can see it. Pinch it."
                        : "Round " + Round + ". Same creatures, harder to find.\nPinch the one you see.";
            NextTrial();
        }

        void Place(SurfaceFinder.Surface s)
        {
            if (Surface == null) return;
            if (s.Normal == Vector3.zero) s = Surface.Current;
            if (Surface.FacingObstacle) Toast("Something is very close in front of you.\nTurn toward open space.", 5f);
            var rot = SurfaceFinder.QuadRotation(s);
            float step = (PatchSize + Gap) * 0.5f;
            Vector2[] grid = { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) };
            for (int i = 0; i < Patches.Length; i++)
            {
                var t = Patches[i].transform;
                t.SetPositionAndRotation(s.Center + s.Right * grid[i].x * step + s.Away * grid[i].y * step, rot);
                t.localScale = new Vector3(PatchSize, PatchSize, 1f);
            }
            Vector3 above = s.Center + s.Away * (step * 2f + 0.05f) + s.Normal * 0.01f;
            Quaternion textRot = Quaternion.LookRotation(above - Head.position, Vector3.up);
            if (Title != null) Title.transform.SetPositionAndRotation(above + s.Normal * 0.02f, textRot);
            if (Status != null)
                Status.transform.SetPositionAndRotation(s.Center - s.Away * (step * 2f + 0.04f) + s.Normal * 0.01f,
                    Quaternion.LookRotation(s.Center - Head.position, Vector3.up));
        }

        void NextTrial()
        {
            if (ladder.IsFinished) { Finish(); return; }
            if (playSeconds >= CapSeconds) { StopForBreak(); return; }
            target = rng.Next(0, Patches.Length);
            for (int i = 0; i < Patches.Length; i++)
            {
                Patches[i].Reshuffle(rng);
                double lv = i == target ? ladder.CurrentArcsec : 0.0;
                Patches[i].Arcsec = Cue == SessionLog.Cue.Stereo ? lv : 0.0;
                Patches[i].MotionArcsec = Cue == SessionLog.Cue.Motion ? lv : 0.0;
                Patches[i].Decoy = false;
            }
            chosen = -1;
            trialStart = Time.time;
            state = State.Trial;
        }

        void OnSelected(StereoPatch p, string method)
        {
            if (Paused) return;
            if (state == State.Intro)
            {
                PlayerPrefs.SetInt(NoticeKey, 1);
                PlayerPrefs.Save();
                if (PlayerPrefs.GetInt(TutorialKey, 0) != 1) StartTutorial();
                else Restart();
                return;
            }
            if (state == State.Tutorial) { if (p != null) OnTutorialPick(p); return; }
            if (state == State.Break)
            {
                if (Time.time < breakUntil) return;
                playSeconds = 0;
                Restart();
                return;
            }
            if (state == State.Done && playSeconds >= CapSeconds) { EnterBreak(); return; }
            if (state == State.Done)
            {
                // After the honest "no stereo" card, the next pinch starts the motion version.
                if (Cue == SessionLog.Cue.Stereo && !lastMeasured) StartCue(SessionLog.Cue.Motion);
                else Restart();
                return;
            }
            if (state != State.Trial || p == null) return;

            bool correct = p.Index == target;
            double level = ladder.CurrentArcsec;
            double z = Mean(q => q.LastViewingDistance);
            distances.Add(z);
            string phase = ladder.CurrentStaircase.CurrentPhase.ToString();

            log.Add(new SessionLog.Trial
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcTime = DateTime.UtcNow,
                Index = trialIndex++, LevelArcsec = level, ViewingDistanceM = z,
                Mode = SessionLog.CatchMode.FourAFC, Eyes = DevControls.Eyes, Correct = correct,
                ReactionMs = (int)((Time.time - trialStart) * 1000), Decoy = false, Phase = phase, Cue = Cue
            });
            Flush();
            ladder.Respond(correct);

            // Feedback: show where it was, clearly, so the player learns what to look for.
            chosen = p.Index;
            if (Cue == SessionLog.Cue.Stereo) Patches[target].Arcsec = Math.Max(level, 1920);
            else Patches[target].MotionArcsec = Math.Max(level, 1920);
            PlayAtCreature(correct ? clipRight : clipWrong, Patches[target], Math.Max(level, 1920));
            // Found: it comes out where it floated (rarity = how fine the depth was) and climbs onto a finger.
            // Missed: it peeks out of the right square and ducks back.
            if (Creatures != null) Creatures.Reveal(Patches[target], level, Math.Max(level, 1920), correct);
            state = State.Feedback;
            stateUntil = Time.time + FeedbackSeconds;
        }

        void Finish()
        {
            state = State.Done;
            foreach (var p in Patches) { p.Arcsec = 0; p.MotionArcsec = 0; }
            var profile = ladder.BuildProfile();
            double z = distances.Count > 0 ? Average(distances) : 0.6;
            bool measured = profile.StereoMeasured && profile.ThresholdArcsec.ContainsKey(SkillLadder.Rung.Spot);
            double thr = measured ? profile.ThresholdArcsec[SkillLadder.Rung.Spot] : double.NaN;
            bool atFloor = measured && profile.AtFloor[SkillLadder.Rung.Spot];

            log.Add(new SessionLog.Summary
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcStart = utcStart,
                Trials = trialIndex, ThresholdArcsec = thr, AtFloor = atFloor, FloorArcsec = floorArcsec,
                Eyes = DevControls.Eyes, Cue = Cue
            });
            Flush();
            lastMeasured = measured;

            bool motion = Cue == SessionLog.Cue.Motion;
            string line = !measured ? "" : motion ? MotionScore.Describe(thr, atFloor, z)
                                                  : SkillLadder.Describe(SkillLadder.Rung.Spot, thr, atFloor, z);
            if (Title != null)
            {
                Title.text = measured
                    ? (motion ? "Motion Depth Score\n" : "Depth Score\n") + line
                    : motion
                        ? "Not measured this time.\nMove your head a little more and try again."
                        : "Your eyes didn't use depth from two eyes today.\nThat's common, and the game still plays.";
                if (Creatures != null) Creatures.Persist();
                string found = Creatures != null ? Creatures.SessionLine() : "";
                if (found.Length > 0) Title.text += "\n" + found;
            }
            Toast(!measured && !motion ? "Pinch any square to play with motion instead" : "Pinch any square to play again", 9999f);
            Finished?.Invoke(new Result
            {
                Round = Round, Trials = trialIndex, Measured = measured, AtFloor = atFloor,
                ThresholdArcsec = thr, ViewingDistanceM = z, ScoreLine = line, Cue = Cue
            });
        }

        void Update()
        {
            if (Paused) return;
            if (state == State.Trial || state == State.Feedback) playSeconds += Time.deltaTime;
            if (state == State.Break && Status != null)
            {
                float left = breakUntil - Time.time;
                Status.text = left > 0 ? "Back in " + Mathf.FloorToInt(left / 60f) + ":" + (Mathf.CeilToInt(left) % 60).ToString("00")
                                       : "Pinch any square to play again";
            }
            if (state == State.Feedback && Time.time >= stateUntil) NextTrial();
            if (state == State.Tutorial && tutorialFound && Time.time >= stateUntil)
            {
                PlayerPrefs.SetInt(TutorialKey, 1);
                PlayerPrefs.Save();
                Restart();
            }

            StereoPatch hovered = null;
            foreach (var s in Selectors)
                if (s != null && s.isActiveAndEnabled && s.Hovered != null) { hovered = s.Hovered; break; }

            for (int i = 0; i < Frames.Length; i++)
            {
                if (Frames[i] == null) continue;
                Color c = FrameIdle;
                if (state == State.Feedback)
                {
                    if (i == target) c = FrameRight;
                    else if (i == chosen) c = FrameWrong;
                }
                else if (state == State.Tutorial && i == target && tutorialHidden)
                {
                    // Where it hid: green once found, otherwise a slow pulse so a first-timer knows where to pinch.
                    c = tutorialFound ? FrameRight
                                      : Color.Lerp(FrameIdle, FrameHover, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
                    if (!tutorialFound && hovered != null && hovered.Index == i) c = FrameHover;
                }
                else if (hovered != null && hovered.Index == i) c = FrameHover;
                Frames[i].startColor = Frames[i].endColor = c;
            }

            if (Status != null && Time.time > toastUntil && (state == State.Trial || state == State.Feedback))
                Status.text = state == State.Trial || state == State.Feedback
                    ? $"{subjectId}   trial {trialIndex + 1}   {ladder.CurrentArcsec:0}″   {DevControls.Eyes}   {Cue}"
                    : "";
        }

        public void Toast(string msg, float seconds = 2.5f)
        {
            if (Status == null) return;
            Status.text = msg;
            toastUntil = Time.time + seconds;
        }

        void Flush()
        {
            try
            {
                var lines = log.Lines;
                using (var w = new StreamWriter(filePath, true))
                    for (; logged < lines.Count; logged++) w.WriteLine(lines[logged]);
            }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] log write failed: " + e.Message); }
        }

        void Play(AudioClip c) { if (Audio != null && c != null) Audio.PlayOneShot(c); }

        /// <summary>Feedback sound from the point where the creature floats (patch centre, lifted by its pop-out depth).</summary>
        void PlayAtCreature(AudioClip c, StereoPatch p, double levelArcsec)
        {
            if (CreatureVoice == null || p == null || c == null) { Play(c); return; }
            Vector3 toViewer = -p.transform.forward;
            float lift = (float)DisparityMath.PopOutMeters(levelArcsec, p.LastViewingDistance);
            CreatureVoice.transform.position = p.transform.position + toViewer * Mathf.Min(lift, 0.15f);
            CreatureVoice.PlayOneShot(c);
        }

        double Mean(Func<StereoPatch, double> f)
        {
            double s = 0; foreach (var p in Patches) s += f(p); return s / Patches.Length;
        }

        static double Average(List<double> v) { double s = 0; foreach (var x in v) s += x; return s / v.Count; }

        /// <summary>Device name for the log and the display floor that goes with it.</summary>
        static string DeviceName(out double floor)
        {
            string model = SystemInfo.deviceModel ?? "";
            string name = "Editor";
            floor = 15;
#if UNITY_ANDROID && !UNITY_EDITOR
            var type = OVRPlugin.GetSystemHeadsetType();
            name = type.ToString();
            // Quest 3S: lower pixel density than Quest 3, so its floor sits higher.
            if (name.Contains("3S")) floor = 20;
#endif
            return name + (model.Length > 0 ? " / " + model : "");
        }
    }

    /// <summary>Tiny procedural sounds so the build needs no audio assets yet.</summary>
    public static class Tones
    {
        public static AudioClip Chirp(float f0, float f1, float seconds, int rate = 44100)
        {
            int n = Mathf.Max(1, (int)(seconds * rate));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2 * Math.PI * f / rate;
                float env = Mathf.Sin(Mathf.PI * t);
                data[i] = (float)Math.Sin(phase) * env * 0.35f;
            }
            var clip = AudioClip.Create("tone", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
