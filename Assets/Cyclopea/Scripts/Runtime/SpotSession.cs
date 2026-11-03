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

        [Header("Scene")]
        public Transform Head;
        public SurfaceFinder Surface;
        public StereoPatch[] Patches = new StereoPatch[4];
        public LineRenderer[] Frames = new LineRenderer[4];
        public PatchSelector[] Selectors;
        public TextMesh Title, Status;
        public AudioSource Audio;

        [Header("Layout")]
        public float PatchSize = 0.12f;
        public float Gap = 0.035f;

        [Header("Timing")]
        public float FeedbackSeconds = 0.75f;

        static readonly Color FrameIdle = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color FrameHover = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color FrameRight = new Color(0.35f, 1f, 0.55f, 1f);
        static readonly Color FrameWrong = new Color(1f, 0.35f, 0.35f, 1f);

        enum State { Trial, Feedback, Done }

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
        }

        void OnDisable()
        {
            foreach (var s in Selectors) if (s != null) s.Selected -= OnSelected;
            if (Surface != null) Surface.SurfaceChanged -= Place;
        }

        void Start()
        {
            for (int i = 0; i < Patches.Length; i++) { Patches[i].Index = i; Patches[i].Head = Head; }
            device = DeviceName(out floorArcsec);
            Place(Surface != null ? Surface.Current : default);
            Restart();
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
            RoundStarting?.Invoke(Round);
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
            Play(correct ? clipRight : clipWrong);
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
            if (state == State.Feedback && Time.time >= stateUntil) NextTrial();

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
                else if (hovered != null && hovered.Index == i) c = FrameHover;
                Frames[i].startColor = Frames[i].endColor = c;
            }

            if (Status != null && Time.time > toastUntil && state != State.Done)
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
