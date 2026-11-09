using System;
using System.Collections.Generic;
using System.IO;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// A session end to end: four squares of static on the surface, creatures carried only by disparity,
    /// a staircase per rung tightening the camouflage, every trial logged to JSONL, and an honest Depth
    /// Score card at the end. Rungs (SkillLadder): Spot (pinch the square a creature hides in), then Sort
    /// (two creatures: pinch the nearer) and Catch (touch one with a real fingertip at the height it floats).
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
            "If a condition affects your vision,\n" +
            "ask an eye-care professional first.\n" +
            "A short break comes after 10 minutes.";

        /// <summary>Set once the first-launch tutorial creature has been found.</summary>
        public const string TutorialKey = "cyc_tutorial_v1";
        /// <summary>Depth Scores across days (ProgressHistory), and the cue of the last measured session.</summary>
        public const string ProgressKey = "cyc_progress_v1", CueKey = "cyc_cue_v1";
        /// <summary>Shown under every menu-like screen: both ways of choosing work there.</summary>
        public const string MenuHint = "Pinch a square, or look at one and hold";

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
        [Tooltip("The daily home card: streak, best, next goal; the squares become Play / mode / Field guide / Options.")]
        public HomeCard Home;
        [Tooltip("All game sounds (Assets/Cyclopea/Audio). Empty slots fall back to plain tones.")]
        public SoundBank Sounds;
        [Tooltip("Looping, spatialised: the static's soft hiss, from the squares, while there is static to search.")]
        public AudioSource Bed;
        public float BedVolume = 0.5f;

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
        [Tooltip("The wink, once the tutorial creature has hidden: see it float, close one eye (it goes flat), open both " +
                 "(it floats again), then pinch it. Seconds for each of the first three beats.")]
        public float WinkSeeSeconds = 3.5f, WinkClosedSeconds = 3.5f, WinkOpenSeconds = 2.5f;
        [Tooltip("Title card that introduces Sort or Catch.")]
        public float RungIntroSeconds = 2.5f;
        [Tooltip("Catch: time to watch the caught creature climb on before the next one hides.")]
        public float CatchFeedbackSeconds = 1.4f;

        [Header("Ladder")]
        [Tooltip("After Spot: Sort (pinch the nearer of two creatures) and Catch (touch one with a fingertip). Stereo only.")]
        public bool LadderRungs = true;
        [Tooltip("Sort runs only when Spot reached this fine a level, so both creatures are easy to see.")]
        public double SortGateArcsec = 480;
        [Tooltip("Sort: the farther creature floats at a random disparity in this range (arcseconds).")]
        public double SortBaseMin = 480, SortBaseMax = 720;
        [Tooltip("Catch is skipped when no tracked fingertip shows up for this long (hands-free players).")]
        public float CatchHandWaitSeconds = 8f;
        [Tooltip("Catch: the fingertip must move this far after a catch before it can catch again (metres).")]
        public float CatchRearmM = 0.04f;

        [Header("Decoys")]
        [Tooltip("Chance that a Spot trial carries a fake: a flat outline either eye can see, in another square. " +
                 "Decoy trials are checks, not staircase steps: the real creature floats clearly (DecoyTargetArcsec).")]
        public float DecoyChance = 0.18f;
        [Tooltip("Decoys start once Spot's staircase has turned this many times (the player has learned what floating looks like).")]
        public int DecoyAfterReversals = 2;
        public double DecoyTargetArcsec = 960;

        [Header("Inclusion")]
        [Tooltip("No choice for this long in a trial: look and hold switches on as well, and a toast says so.")]
        public float IdleAssistSeconds = 25f;

        static readonly Color FrameIdle = new Color(0.78f, 0.88f, 1f, 0.22f);
        static readonly Color FrameIdleHighContrast = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color FrameHover = new Color(1f, 0.97f, 0.9f, 0.85f);
        // Colour-blind safe (blue / orange, Okabe-Ito), and never colour alone: the right square's frame is also thicker.
        public static readonly Color FrameRight = new Color(0.30f, 0.72f, 1f, 1f);
        public static readonly Color FrameWrong = new Color(1f, 0.55f, 0.08f, 1f);
        public const float RightFrameWidth = 2.4f;

        enum State { Intro, Tutorial, Trial, Feedback, Done, Break, RungIntro, Home }

        /// <summary>What a finished session produced; other systems (score anchor, camouflage, platform) listen for it.</summary>
        public struct Result
        {
            public int Round, Trials;
            public bool Measured, AtFloor;
            public double ThresholdArcsec, ViewingDistanceM;
            public string ScoreLine;
            public SessionLog.Cue Cue;
            /// <summary>Sort and Catch results, one line ("" when only Spot ran).</summary>
            public string ProfileLine;
            /// <summary>Finer than every earlier score with this cue (as shown, in whole arcseconds).</summary>
            public bool PersonalBest;
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
            onlyRung = null;
            Restart();
        }

        /// <summary>Back to the whole ladder after a one-rung practice session.</summary>
        public void StartAllRungs()
        {
            onlyRung = null;
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

        // Ladder state.
        SkillLadder.Rung rung;
        SkillLadder.Rung? onlyRung;
        int sortOther = -1;
        double sortBase, catchHeight;
        readonly StillDetector still = new StillDetector();
        int catchOver = -1, catchTrials, catchHits;
        float rungStart;
        bool tipSeen, catchArmed, lastCorrect;
        Vector3 lastCatchAt;
        readonly List<double> catchErrors = new List<double>();
        OVRCameraRig rig;

        // Decoys, the home card, inclusion.
        int decoy = -1, decoyTrials, decoysPicked, sinceDecoy;
        float tutorialHiddenAt, frameWidth;
        HeadDwellSelector dwellSel;
        public ProgressHistory Progress { get; private set; } = new ProgressHistory();
        /// <summary>Look and hold switched itself on because nothing was chosen for a while (this run only).</summary>
        public bool DwellAssist { get; private set; }

        // Pause reasons; play resumes only when none is left.
        public const int PauseFocus = 1, PauseUnmounted = 2, PauseApp = 4, PauseManual = 8;

        void Awake()
        {
            Options.Load();
            Progress = ProgressHistory.Parse(PlayerPrefs.GetString(ProgressKey, ""));
            clipRight = Or(Sounds != null ? Sounds.PickRight : null, () => Tones.Chirp(880, 1320, 0.16f));
            clipWrong = Or(Sounds != null ? Sounds.PickWrong : null, () => Tones.Chirp(330, 220, 0.18f));
            clipTick = Or(Sounds != null ? Sounds.UiHover : null, () => Tones.Chirp(1500, 1500, 0.03f));
        }

        static AudioClip Or(AudioClip clip, Func<AudioClip> fallback) => clip != null ? clip : fallback();

        /// <summary>A bank sound, flat (not spatialised); nothing when that slot is empty.</summary>
        void PlaySfx(Func<SoundBank, AudioClip> pick)
        {
            if (Sounds != null) Play(pick(Sounds));
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
            if (!isNew) return;
            Toast("New in your field guide: " + Core.Creatures.Name(s, r), 1.8f);
            PlaySfx(b => b.NewKind);
        }

        // ---------- First-launch tutorial ----------
        // Show, don't tell: a creature appears beside the squares, climbs onto your finger if you hold
        // one out, then hops into a square and sinks into the static. You pinch it back out.

        public bool ShowingTutorial => state == State.Tutorial;
        public bool TutorialCreatureHidden => state == State.Tutorial && tutorialHidden;

        /// <summary>Replays the tutorial (dev remote; also what a first launch does after the notice).</summary>
        public void StartTutorial()
        {
            if (Home != null) Home.Hide();
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
            tutorialHiddenAt = Time.time;
            winkBeat = -1;
            UpdateWink();
        }

        // The wink: the whole idea in ten seconds, without a rule to read. Two eyes see it float, one eye
        // sees flat static, two eyes again and it's back. Picking works at every beat.
        int winkBeat = -1;

        /// <summary>0 see it float, 1 close one eye, 2 open both, 3 pinch it; -1 outside the hidden tutorial.</summary>
        public int TutorialWinkBeat => state == State.Tutorial && tutorialHidden && !tutorialFound ? winkBeat : -1;

        void UpdateWink()
        {
            float t = Time.time - tutorialHiddenAt;
            int beat = t < WinkSeeSeconds ? 0
                     : t < WinkSeeSeconds + WinkClosedSeconds ? 1
                     : t < WinkSeeSeconds + WinkClosedSeconds + WinkOpenSeconds ? 2 : 3;
            if (beat == winkBeat) return;
            winkBeat = beat;
            if (Title != null) Title.text = WinkLine(beat);
            if (beat == 1) PlaySfx(b => b.EyeClose);
            else if (beat == 2) PlaySfx(b => b.EyeOpen);
        }

        string WinkLine(int beat)
        {
            bool motion = Cue == SessionLog.Cue.Motion;
            switch (beat)
            {
                case 0: return motion ? "It hid in the static.\nSway your head gently. See it slide?"
                                      : "It hid in the static.\nSee one square float?";
                case 1: return motion ? "Now hold your head still." : "Now close one eye.";
                case 2: return motion ? "Gone. It only shows while you move.\nSway again."
                                      : "Flat! One eye can't find it.\nOpen both.";
                default: return (motion ? "Moving shows it.\n" : "Only two eyes can see them.\n") +
                                How("Point at it and pinch.", "Look at it and hold.");
            }
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

        /// <summary>The pinch wording, or the look-and-hold wording when that is how this player chooses.</summary>
        string How(string pinch, string look) => Options.LookAndHold || DwellAssist ? look : pinch;

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
                rungStart += d;
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
            Toast(MenuHint + ", to start", 9999f);
        }

        // ---------- Home card ----------
        // From the second launch on (and after each score card): streak, best, next goal; the squares are buttons.

        public bool ShowingHome => state == State.Home;

        public void ShowHome()
        {
            if (Home == null) { StartAllRungs(); return; }
            state = State.Home;
            ClearPatches();
            Home.Show(HomeCard.Page.Home);
            if (dwellSel != null) dwellSel.ResetDwell();
            Toast(MenuHint, 9999f);
        }

        /// <summary>Screens that wait for one choice: the notice, the home card, the score card, a finished break.</summary>
        public bool InMenu => state == State.Intro || state == State.Home || state == State.Done ||
                              (state == State.Break && Time.time >= breakUntil);

        /// <summary>Look and hold is live: chosen in Options, switched on by the idle assist, or a menu is showing.</summary>
        public bool WantsLookAndHold => Options.LookAndHold || DwellAssist || InMenu;

        /// <summary>Stereo, or motion once the last measured session was in motion mode.</summary>
        public SessionLog.Cue PreferredCue
        {
            get => PlayerPrefs.GetInt(CueKey, 0) == 1 ? SessionLog.Cue.Motion : SessionLog.Cue.Stereo;
            private set { PlayerPrefs.SetInt(CueKey, value == SessionLog.Cue.Motion ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Title text at its normal size, a little smaller from five lines so it stays inside the comfortable view.</summary>
        public void SetTitle(string text)
        {
            if (Title == null) return;
            Title.text = text;
            if (titleCharSize > 0) Title.characterSize = titleCharSize * (text.Split('\n').Length >= 5 ? 0.85f : 1f);
        }

        // ---------- Rest break ----------

        void EnterBreak()
        {
            state = State.Break;
            PlaySfx(b => b.BreakChime);
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
                Eyes = DevControls.Eyes, Cue = Cue, Rung = rung.ToString()
            });
            Flush();
            lastMeasured = true;
            EnterBreak();
        }

        void ClearPatches()
        {
            foreach (var p in Patches) if (p != null) { p.Arcsec = 0; p.MotionArcsec = 0; p.HoldHeightM = 0; p.Decoy = false; }
            decoy = -1;
        }

        // Test hooks.
        public bool OnBreak => state == State.Break;
        public bool ShowingNotice => state == State.Intro;
        public int TrialsDone => trialIndex;

        [Tooltip("Display refresh rate to ask for when the headset offers it. The game uses ~6 ms of a 11.1 ms budget at 90 Hz.")]
        public float PreferredRefreshHz = 90f;

        /// <summary>
        /// 90 Hz instead of the default 72: smoother creatures and a steadier, clearer field of dots. Asked for only
        /// when the headset lists it (Meta: request a rate from displayFrequenciesAvailable); dynamic throttling may
        /// still drop it to 72 when the headset is hot.
        /// </summary>
        void RequestRefreshRate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var freqs = OVRManager.display?.displayFrequenciesAvailable;
                if (PreferredRefreshHz > 0 && freqs != null && System.Array.IndexOf(freqs, PreferredRefreshHz) >= 0)
                {
                    OVRPlugin.systemDisplayFrequency = PreferredRefreshHz;
                    Debug.Log("[Cyclopea] display " + PreferredRefreshHz + " Hz requested (available: " + string.Join(", ", freqs) + ")");
                }
            }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] refresh rate: " + e.Message); }
#endif
        }

        void Start()
        {
            RequestRefreshRate();
            for (int i = 0; i < Patches.Length; i++) { Patches[i].Index = i; Patches[i].Head = Head; }
            device = DeviceName(out floorArcsec);
            rig = FindFirstObjectByType<OVRCameraRig>();
            UpdateIpd();
            if (Title != null) titleCharSize = Title.characterSize;
            if (Frames.Length > 0 && Frames[0] != null) frameWidth = Frames[0].widthMultiplier;
            foreach (var sel in Selectors) if (sel is HeadDwellSelector d) dwellSel = d;
            Place(Surface != null ? Surface.Current : default);
            if (PlayerPrefs.GetInt(NoticeKey, 0) != 1) ShowNotice();
            else if (PlayerPrefs.GetInt(TutorialKey, 0) != 1) StartTutorial();
            else if (Progress.Count > 0 && Home != null) ShowHome();   // a returning player starts at the home card
            else Restart();
        }

        // Automation hooks (play-mode tests, XR Operator scripts): read the hidden answer, pick a square.
        public int CurrentTarget => target;
        public bool IsDone => state == State.Done;
        public bool AwaitingChoice => state == State.Trial;
        public double CurrentArcsec => ladder.CurrentArcsec;
        public SkillLadder.Rung CurrentRung => rung;
        public bool IntroducingRung => state == State.RungIntro;
        /// <summary>Sort: the farther creature's square (CurrentTarget is the nearer one). -1 otherwise.</summary>
        public int SortOther => sortOther;
        /// <summary>Catch: how high the creature is held above the surface (metres).</summary>
        public double CatchHeightM => catchHeight;
        public float CatchHoldProgress => (float)still.Progress;
        /// <summary>The square holding a flat fake this trial, or -1.</summary>
        public int CurrentDecoy => decoy;

        /// <summary>
        /// Catch with the fingertip over square <paramref name="index"/> at <paramref name="heightM"/> above the
        /// surface (automation and tests; a real hand goes through the same judgement in Update).
        /// </summary>
        public void CatchAt(int index, double heightM, string method = "script")
        {
            if (Paused || state != State.Trial || rung != SkillLadder.Rung.Catch) return;
            JudgeCatch(index, heightM, method);
        }

        /// <summary>A session of just one rung (dev remote "rung=sort|catch", tests): practise or check it alone.</summary>
        public void StartAtRung(SkillLadder.Rung only)
        {
            Cue = SessionLog.Cue.Stereo;
            onlyRung = only;
            Restart();
        }
        public string LogPath => filePath;
        public void Choose(int index, string method = "script")
        {
            if (index >= 0 && index < Patches.Length) OnSelected(Patches[index], method);
        }

        public void Restart()
        {
            if (Home != null) Home.Hide();
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + rng.Next(1000, 9999);
            subjectId = "P" + PlayerPrefs.GetInt(SubjectKey, 1).ToString("00");
            utcStart = DateTime.UtcNow;
            var rungs = new List<SkillLadder.RungConfig>(Rungs());
            if (onlyRung.HasValue) rungs.RemoveAll(r => r.Rung != onlyRung.Value);
            ladder = new SkillLadder(floorArcsec, rungs);
            rung = SkillLadder.Rung.Spot;   // a different first rung (StartAtRung) gets its intro card
            sortOther = -1;
            catchTrials = catchHits = 0;
            catchErrors.Clear();
            catchHeight = 0;
            decoy = -1; decoyTrials = decoysPicked = sinceDecoy = 0;
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
                    ? (Options.LessMovement ? "The static sways for you.\nOne square's static slides differently. " + How("Pinch it.", "Look at it and hold.")
                                            : "Sway your head gently from side to side.\nOne square's static slides differently. " + How("Pinch it.", "Look at it and hold."))
                    : Round == 1
                        ? "One square hides a creature.\nOnly two eyes can see it. " + How("Pinch it.", "Look at it and hold.")
                        : "Round " + Round + ". Same creatures, harder to find.\n" + How("Pinch the one you see.", "Look at the one you see and hold.");
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
            Vector3 above = s.Center + s.Away * (step * 2f + 0.025f) + s.Normal * 0.01f;
            Quaternion textRot = Quaternion.LookRotation(above - Head.position, Vector3.up);
            if (Title != null) Title.transform.SetPositionAndRotation(above + s.Normal * 0.02f, textRot);
            if (Status != null)
                Status.transform.SetPositionAndRotation(s.Center - s.Away * (step * 2f + 0.04f) + s.Normal * 0.01f,
                    Quaternion.LookRotation(s.Center - Head.position, Vector3.up));
        }

        /// <summary>
        /// The ladder for a session. Spot measures; Sort and Catch follow in stereo mode. Sort is short
        /// (8 reversals) and Catch shorter (it stops at 480", about as fine as hand tracking resolves).
        /// </summary>
        IEnumerable<SkillLadder.RungConfig> Rungs()
        {
            yield return new SkillLadder.RungConfig
            {
                Rung = SkillLadder.Rung.Spot, Alternatives = 4,
                Stair = new Staircase.Config { StartArcsec = 1920, FloorArcsec = floorArcsec }
            };
            if (Cue != SessionLog.Cue.Stereo || (!LadderRungs && !onlyRung.HasValue)) yield break;
            yield return new SkillLadder.RungConfig
            {
                Rung = SkillLadder.Rung.Sort, Alternatives = 2,
                Stair = new Staircase.Config
                {
                    StartArcsec = 720, CeilingArcsec = 1440, FloorArcsec = floorArcsec * 2, DownRule = 3,
                    ReversalsToStop = 8, ReversalsForThreshold = 6, FloorTrialsToStop = 8, MaxTrials = 60
                }
            };
            yield return new SkillLadder.RungConfig
            {
                Rung = SkillLadder.Rung.Catch, Alternatives = 1,
                Stair = new Staircase.Config
                {
                    StartArcsec = 1920, CeilingArcsec = 1920, FloorArcsec = 480,
                    ReversalsToStop = 4, ReversalsForThreshold = 4, FloorTrialsToStop = 4, MaxTrials = 12
                }
            };
        }

        void NextTrial()
        {
            if (playSeconds >= CapSeconds && !ladder.IsFinished) { StopForBreak(); return; }
            // A new rung: check it can run here, then introduce it with a short title card.
            while (!ladder.IsFinished && ladder.CurrentRung != rung)
            {
                var next = ladder.CurrentRung;
                if (CanRun(next)) { IntroduceRung(next); return; }
                ladder.SetEnabled(next, false);
            }
            if (ladder.IsFinished) { Finish(); return; }
            StartTrial();
        }

        bool CanRun(SkillLadder.Rung r)
        {
            switch (r)
            {
                case SkillLadder.Rung.Sort:
                    // Both creatures must be easy to see: only after a fine enough Spot (or on its own, as practice).
                    if (onlyRung.HasValue) return true;
                    var prof = ladder.BuildProfile();
                    return prof.StereoMeasured && prof.ThresholdArcsec.TryGetValue(SkillLadder.Rung.Spot, out double thr) &&
                           thr <= SortGateArcsec;
                case SkillLadder.Rung.Catch:
                    return Tips != null && !Options.LessMovement;   // Less movement: no reaching
                default:
                    return true;
            }
        }

        FingerTips Tips => Creatures != null ? Creatures.Tips : null;

        void IntroduceRung(SkillLadder.Rung r)
        {
            rung = r;
            state = State.RungIntro;
            PlaySfx(b => b.LevelUp);
            ClearPatches();
            stateUntil = Time.time + RungIntroSeconds;
            if (Title != null)
            {
                if (titleCharSize > 0) Title.characterSize = titleCharSize;
                Title.text = r == SkillLadder.Rung.Sort ? "Two creatures now.\nPinch the one floating nearer to you."
                           : r == SkillLadder.Rung.Catch ? "Now catch one.\nTouch its back with a fingertip and hold still."
                           : "Pinch the square a creature hides in.";
            }
            rungStart = Time.time + RungIntroSeconds;
            tipSeen = false;
            catchArmed = true;
        }

        void StartTrial()
        {
            target = rng.Next(0, Patches.Length);
            sortOther = -1;
            decoy = -1;
            double lv = ladder.CurrentArcsec;
            foreach (var p in Patches)
            {
                p.Reshuffle(rng);
                p.Arcsec = 0; p.MotionArcsec = 0; p.HoldHeightM = 0; p.Decoy = false;
            }
            switch (rung)
            {
                case SkillLadder.Rung.Sort:
                    // Two creatures; the nearer one floats lv arcseconds in front of the farther one.
                    sortOther = (target + 1 + rng.Next(0, Patches.Length - 1)) % Patches.Length;
                    sortBase = SortBaseMin + rng.NextDouble() * (SortBaseMax - SortBaseMin);
                    Patches[target].Arcsec = sortBase + lv;
                    Patches[sortOther].Arcsec = sortBase;
                    break;
                case SkillLadder.Rung.Catch:
                {
                    // Held at a fixed height, like a real object, so leaning in to reach does not move it.
                    var p = Patches[target];
                    catchHeight = Math.Min(0.10, Math.Max(0.008, DisparityMath.PopOutMeters(lv, p.LastViewingDistance, StereoPatch.Ipd)));
                    p.Arcsec = lv;
                    p.HoldHeightM = catchHeight;
                    still.Reset();
                    catchOver = -1;
                    break;
                }
                default:
                    if (DecoyDue())
                    {
                        // A check, not a step: the real creature floats clearly, and a flat fake sits in another square.
                        decoy = (target + 1 + rng.Next(0, Patches.Length - 1)) % Patches.Length;
                        Patches[decoy].Decoy = true;
                        Patches[target].Arcsec = Math.Min(1920, Math.Max(DecoyTargetArcsec, 2 * lv));
                        sinceDecoy = 0;
                    }
                    else if (Cue == SessionLog.Cue.Stereo)
                    {
                        Patches[target].Arcsec = lv;
                        sinceDecoy++;
                    }
                    else Patches[target].MotionArcsec = lv;
                    break;
            }
            chosen = -1;
            trialStart = Time.time;
            state = State.Trial;
            if (dwellSel != null) dwellSel.ResetDwell();
        }

        /// <summary>Spot in stereo, past the first turns of the staircase, at least three trials since the last fake.</summary>
        bool DecoyDue() =>
            rung == SkillLadder.Rung.Spot && Cue == SessionLog.Cue.Stereo && !onlyRung.HasValue && DecoyChance > 0 &&
            ladder.CurrentStaircase.Reversals >= DecoyAfterReversals && sinceDecoy >= 3 && rng.NextDouble() < DecoyChance;

        void OnSelected(StereoPatch p, string method)
        {
            if (Paused) return;
            if (state == State.Intro)
            {
                // Accepted by looking and holding: this player chooses that way, so the game will too.
                if (method == "head-dwell" && !Options.LookAndHold) { Options.LookAndHold = true; Options.Save(); }
                PlayerPrefs.SetInt(NoticeKey, 1);
                PlayerPrefs.Save();
                if (PlayerPrefs.GetInt(TutorialKey, 0) != 1) StartTutorial();
                else Restart();
                return;
            }
            if (state == State.Tutorial) { if (p != null) OnTutorialPick(p); return; }
            if (state == State.Home)
            {
                if (p != null && Home != null) Home.Pick(p.Index, method);
                if (dwellSel != null) dwellSel.ResetDwell();
                return;
            }
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
                else ShowHome();   // the home card: streak, best, what to find next, Play
                return;
            }
            if (state != State.Trial || p == null) return;
            if (rung == SkillLadder.Rung.Catch) return;   // caught with a fingertip, not a pinch
            if (decoy >= 0) { JudgeDecoy(p, method); return; }
            bool sort = rung == SkillLadder.Rung.Sort;
            if (sort && p.Index != target && p.Index != sortOther)
            {
                Toast("Pick one of the two creatures.", 1.5f);
                return;
            }

            bool correct = p.Index == target;
            double level = ladder.CurrentArcsec;
            double z = Mean(q => q.LastViewingDistance);
            distances.Add(z);
            string phase = ladder.CurrentStaircase.CurrentPhase.ToString();

            log.Add(new SessionLog.Trial
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcTime = DateTime.UtcNow,
                Index = trialIndex++, LevelArcsec = level, ViewingDistanceM = z,
                Mode = sort ? SessionLog.CatchMode.TwoAFC : SessionLog.CatchMode.FourAFC, Eyes = DevControls.Eyes,
                Correct = correct, ReactionMs = (int)((Time.time - trialStart) * 1000), Decoy = false, Phase = phase,
                Cue = Cue, Rung = rung.ToString()
            });
            Flush();
            ladder.Respond(correct);
            lastCorrect = correct;
            chosen = p.Index;

            if (sort)
            {
                // The nearer one stands out clearly. Found, it comes out; the farther one peeks too.
                double shown = sortBase + Math.Max(level, 960);
                Patches[target].Arcsec = shown;
                PlayAtCreature(correct ? clipRight : clipWrong, Patches[target], shown);
                if (Creatures != null)
                {
                    Creatures.Reveal(Patches[target], level, shown, correct);
                    Creatures.Reveal(Patches[sortOther], level, sortBase, false, collect: false);
                }
                state = State.Feedback;
                stateUntil = Time.time + FeedbackSeconds;
                return;
            }

            // Feedback: show where it was, clearly, so the player learns what to look for.
            if (Cue == SessionLog.Cue.Stereo) Patches[target].Arcsec = Math.Max(level, 1920);
            else Patches[target].MotionArcsec = Math.Max(level, 1920);
            PlayAtCreature(correct ? clipRight : clipWrong, Patches[target], Math.Max(level, 1920));
            // Found: it comes out where it floated (rarity = how fine the depth was) and climbs onto a finger.
            // Missed: it peeks out of the right square and ducks back.
            if (Creatures != null) Creatures.Reveal(Patches[target], level, Math.Max(level, 1920), correct);
            state = State.Feedback;
            stateUntil = Time.time + FeedbackSeconds;
        }

        /// <summary>A trial with a fake in it: logged and counted, never fed to the staircase.</summary>
        void JudgeDecoy(StereoPatch p, string method)
        {
            bool correct = p.Index == target, fooled = p.Index == decoy;
            double shown = Patches[target].Arcsec;
            double z = Mean(q => q.LastViewingDistance);
            log.Add(new SessionLog.Trial
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcTime = DateTime.UtcNow,
                Index = trialIndex++, LevelArcsec = shown, ViewingDistanceM = z, Mode = SessionLog.CatchMode.FourAFC,
                Eyes = DevControls.Eyes, Correct = correct, ReactionMs = (int)((Time.time - trialStart) * 1000),
                Decoy = true, DecoyPicked = fooled, Phase = "Decoy", Cue = Cue, Rung = rung.ToString()
            });
            Flush();
            decoyTrials++;
            if (fooled) decoysPicked++;
            lastCorrect = correct;
            chosen = p.Index;
            PlayAtCreature(correct ? clipRight : clipWrong, Patches[target], shown);
            if (Creatures != null) Creatures.Reveal(Patches[target], shown, Math.Max(shown, 1920), correct);
            if (fooled) Toast("A fake! That one was flat. Real ones float.", 2.5f);
            else if (correct && decoyTrials - decoysPicked == 1) Toast("You ignored a fake. Only real ones float.", 2.2f);
            state = State.Feedback;
            stateUntil = Time.time + FeedbackSeconds + (fooled ? 0.6f : 0f);
        }

        // ---------- Catch ----------
        // One creature is held at a fixed height above a square. The player touches its back with a real
        // fingertip and holds still; the touch is judged by the fingertip's height against the creature's.

        void JudgeCatch(int over, double heightM, string method)
        {
            var outcome = Reach.Judge(over == target, heightM, catchHeight);
            bool correct = outcome == Reach.Outcome.Caught;
            double level = ladder.CurrentArcsec;
            double z = Mean(q => q.LastViewingDistance);
            distances.Add(z);
            log.Add(new SessionLog.Trial
            {
                SessionId = sessionId, SubjectId = subjectId, Device = device, UtcTime = DateTime.UtcNow,
                Index = trialIndex++, LevelArcsec = level, ViewingDistanceM = z,
                Mode = SessionLog.CatchMode.ReachToDepth, Eyes = DevControls.Eyes, Correct = correct,
                ReactionMs = (int)((Time.time - trialStart) * 1000), Decoy = false,
                Phase = ladder.CurrentStaircase.CurrentPhase.ToString(), Cue = Cue, Rung = rung.ToString(),
                HeightM = catchHeight, DepthErrorM = over == target ? heightM - catchHeight : double.NaN
            });
            Flush();
            ladder.Respond(correct);
            catchTrials++;
            if (correct) catchHits++;
            if (over == target) catchErrors.Add(Math.Abs(heightM - catchHeight));
            lastCorrect = correct;
            chosen = over;

            // Caught: it comes out right under the fingertip and climbs on. Missed: it peeks out at the
            // height it really floated, and the toast says which way.
            var p = Patches[target];
            PlayAtCreature(correct ? Or(Sounds != null ? Sounds.CatchSuccess : null, () => clipRight) : clipWrong, p, level);
            if (Creatures != null) Creatures.Reveal(p, level, level, correct, true, catchHeight);
            Toast(outcome == Reach.Outcome.Caught ? "Caught it!"
                : outcome == Reach.Outcome.TooHigh ? "It floated lower than that."
                : outcome == Reach.Outcome.TooLow ? "It floated higher than that."
                : "It was in another square.", 1.6f);
            state = State.Feedback;
            stateUntil = Time.time + Mathf.Max(FeedbackSeconds, CatchFeedbackSeconds);
        }

        /// <summary>The fingertip over a square, held still, is a catch at its height.</summary>
        void UpdateCatch()
        {
            var tips = Tips;
            Pose tip = default;
            bool have = tips != null && Head != null && tips.TryNearest(GridCenter(), 0.8f, out tip);
            if (have)
            {
                tipSeen = true;
                // After a catch the finger has to move on before it can catch again (a creature may be riding it).
                if (!catchArmed && Vector3.Distance(tip.position, lastCatchAt) >= CatchRearmM) catchArmed = true;
            }
            int over = -1;
            double h = 0;
            if (have && catchArmed) over = Over(tip.position, out h);
            if (over >= 0 && h > -0.01 && h < 0.3)
            {
                if (over != catchOver) { still.Reset(); catchOver = over; }
                if (still.Feed(Time.time, tip.position.x, tip.position.y, tip.position.z))
                {
                    var mean = new Vector3((float)still.MeanX, (float)still.MeanY, (float)still.MeanZ);
                    int at = Over(mean, out double hm);
                    catchArmed = false;
                    lastCatchAt = mean;
                    catchOver = -1;
                    JudgeCatch(at >= 0 ? at : over, at >= 0 ? hm : h, "finger");
                    return;
                }
            }
            else
            {
                still.Reset();
                catchOver = -1;
            }

            // Hands-free players: no fingertip ever shows up, so Catch is skipped rather than waited on.
            if (!tipSeen && catchTrials == 0 && Time.time - rungStart > CatchHandWaitSeconds)
            {
                Toast("Catch needs hand tracking, so it is skipped this time.", 3f);
                ladder.SetEnabled(SkillLadder.Rung.Catch, false);
                ClearPatches();
                NextTrial();
            }
        }

        Vector3 GridCenter()
        {
            Vector3 c = Vector3.zero;
            foreach (var p in Patches) c += p.transform.position;
            return c / Patches.Length;
        }

        /// <summary>
        /// The square a fingertip covers as seen from the head (a floating creature is seen on that sightline,
        /// not straight above its square), and the fingertip's height above the surface. -1 when none.
        /// </summary>
        int Over(Vector3 f, out double h)
        {
            h = 0;
            if (Head == null) return -1;
            foreach (var p in Patches)
            {
                var t = p.transform;
                Vector3 n = -t.forward;
                float hF = Vector3.Dot(f - t.position, n);
                float hH = Vector3.Dot(Head.position - t.position, n);
                if (hH <= hF + 0.02f) continue;
                Vector3 q = Head.position + (f - Head.position) * (hH / (hH - hF));
                Vector3 local = t.InverseTransformPoint(q);
                if (Mathf.Abs(local.x) <= 0.6f && Mathf.Abs(local.y) <= 0.6f) { h = hF; return p.Index; }
            }
            return -1;
        }

        /// <summary>Held heights need the real distance between the eyes; the rig's eye anchors carry it.</summary>
        void UpdateIpd()
        {
            if (rig == null || rig.leftEyeAnchor == null || rig.rightEyeAnchor == null) return;
            float e = Vector3.Distance(rig.leftEyeAnchor.position, rig.rightEyeAnchor.position);
            if (e > 0.045f && e < 0.085f) StereoPatch.Ipd = e;
        }

        /// <summary>"Sort: 95″  ·  Catch: 7 of 8, within 6 mm" for the rungs that ran ("" for none).</summary>
        string ProfileLine(SkillLadder.Profile prof)
        {
            var parts = new List<string>();
            if (prof.ThresholdArcsec.TryGetValue(SkillLadder.Rung.Sort, out double st) && !double.IsNaN(st))
                parts.Add("Sort: " + (prof.AtFloor[SkillLadder.Rung.Sort] ? "≤ " : "") + st.ToString("0") + "″");
            if (catchTrials > 0)
                parts.Add("Catch: " + catchHits + " of " + catchTrials +
                          (catchErrors.Count > 0 ? ", within " + (Median(catchErrors) * 1000).ToString("0") + " mm" : ""));
            if (decoyTrials > 0) parts.Add("Fakes ignored: " + (decoyTrials - decoysPicked) + " of " + decoyTrials);
            return string.Join("  ·  ", parts);
        }

        static double Median(List<double> v)
        {
            var a = new List<double>(v);
            a.Sort();
            int n = a.Count;
            return n % 2 == 1 ? a[n / 2] : 0.5 * (a[n / 2 - 1] + a[n / 2]);
        }

        void Finish()
        {
            state = State.Done;
            ClearPatches();
            var profile = ladder.BuildProfile();
            double z = distances.Count > 0 ? Average(distances) : 0.6;
            bool measured = profile.StereoMeasured && profile.ThresholdArcsec.ContainsKey(SkillLadder.Rung.Spot);
            double thr = measured ? profile.ThresholdArcsec[SkillLadder.Rung.Spot] : double.NaN;
            bool atFloor = measured && profile.AtFloor[SkillLadder.Rung.Spot];
            bool practice = onlyRung.HasValue && onlyRung.Value != SkillLadder.Rung.Spot;

            // One summary per rung that ran; Spot's is the Depth Score (stats.py reads only Spot).
            if (!practice)
                log.Add(new SessionLog.Summary
                {
                    SessionId = sessionId, SubjectId = subjectId, Device = device, UtcStart = utcStart,
                    Trials = profile.Trials.TryGetValue(SkillLadder.Rung.Spot, out int spotTrials) ? spotTrials : trialIndex,
                    ThresholdArcsec = thr, AtFloor = atFloor, FloorArcsec = floorArcsec,
                    Eyes = DevControls.Eyes, Cue = Cue, Rung = "Spot"
                });
            foreach (var r in new[] { SkillLadder.Rung.Sort, SkillLadder.Rung.Catch })
                if (profile.Trials.TryGetValue(r, out int n))
                    log.Add(new SessionLog.Summary
                    {
                        SessionId = sessionId, SubjectId = subjectId, Device = device, UtcStart = utcStart,
                        Trials = n, ThresholdArcsec = profile.ThresholdArcsec[r], AtFloor = profile.AtFloor[r],
                        FloorArcsec = r == SkillLadder.Rung.Catch ? 480 : floorArcsec * 2,
                        Eyes = DevControls.Eyes, Cue = Cue, Rung = r.ToString()
                    });
            Flush();
            lastMeasured = measured || practice;
            string profileLine = ProfileLine(profile);

            bool motion = Cue == SessionLog.Cue.Motion;
            string line = !measured ? "" : motion ? MotionScore.Describe(thr, atFloor, z)
                                                  : SkillLadder.Describe(SkillLadder.Rung.Spot, thr, atFloor, z);

            // Growth between sessions: the day's best is kept, and a finer score than ever before is said out loud.
            bool best = false;
            if (measured && !practice)
            {
                best = Progress.Record(DateTime.Now, Cue, thr);
                PlayerPrefs.SetString(ProgressKey, Progress.Serialize());
                PreferredCue = Cue;
                PlayerPrefs.Save();
                if (best) line += "\nNew personal best!";
            }
            PlaySfx(b => best ? b.PersonalBest : b.ScoreReveal);
            if (Title != null)
            {
                Title.text = practice
                    ? "Practice: " + onlyRung.Value + "\n" + (profileLine.Length > 0 ? profileLine : "nothing caught")
                    : measured
                    ? (motion ? "Motion Depth Score\n" : "Depth Score\n") + line
                    : motion
                        ? "Not measured this time.\nMove your head a little more and try again."
                        : "Your eyes didn't use depth from two eyes today.\nThat's common, and the game still plays.";
                if (!practice && measured && profileLine.Length > 0) Title.text += "\n" + profileLine;
                if (Creatures != null) Creatures.Persist();
                string found = Creatures != null ? Creatures.SessionLine() : "";
                if (found.Length > 0) Title.text += "\n" + found;
                // Five lines and more: a little smaller so the card stays inside the comfortable view.
                if (titleCharSize > 0 && Title.text.Split('\n').Length >= 5) Title.characterSize = titleCharSize * 0.85f;
            }
            Toast(!measured && !motion && !practice ? "Pinch any square to play with motion instead"
                  : Home != null ? MenuHint + ", to go on" : "Pinch any square to play again", 9999f);
            if (practice) return;   // practice of one rung is not a session result (no score anchor, no leaderboard)
            Finished?.Invoke(new Result
            {
                Round = Round, Trials = trialIndex, Measured = measured, AtFloor = atFloor,
                ThresholdArcsec = thr, ViewingDistanceM = z, ScoreLine = line, Cue = Cue, ProfileLine = profileLine,
                PersonalBest = best
            });
        }

        void Update()
        {
            if (Paused) return;
            UpdateIpd();
            if (state == State.Trial || state == State.Feedback) playSeconds += Time.deltaTime;
            if (state == State.RungIntro && Time.time >= stateUntil) StartTrial();
            if (state == State.Trial && rung == SkillLadder.Rung.Catch) UpdateCatch();
            if (state == State.Break && Status != null)
            {
                float left = breakUntil - Time.time;
                Status.text = left > 0 ? "Back in " + Mathf.FloorToInt(left / 60f) + ":" + (Mathf.CeilToInt(left) % 60).ToString("00")
                                       : "Pinch any square to play again";
            }
            if (state == State.Feedback && Time.time >= stateUntil) NextTrial();

            // Nothing chosen for a long while: maybe this player cannot pinch. Look and hold joins in.
            if (!DwellAssist && !Options.LookAndHold && IdleAssistSeconds > 0 &&
                ((state == State.Trial && rung != SkillLadder.Rung.Catch && Time.time - trialStart > IdleAssistSeconds) ||
                 (state == State.Tutorial && tutorialHidden && !tutorialFound && Time.time - tutorialHiddenAt > IdleAssistSeconds)))
            {
                DwellAssist = true;
                if (dwellSel != null) dwellSel.ResetDwell();
                Toast("Can't pinch? Look at a square and hold your gaze on it.", 6f);
            }
            if (Title != null)
            {
                var style = Options.HighContrast ? FontStyle.Bold : FontStyle.Normal;
                if (Title.fontStyle != style) { Title.fontStyle = style; if (Status != null) Status.fontStyle = style; }
            }
            if (state == State.Tutorial && tutorialHidden && !tutorialFound) UpdateWink();
            UpdateBed();
            if (state == State.Tutorial && tutorialFound && Time.time >= stateUntil)
            {
                PlayerPrefs.SetInt(TutorialKey, 1);
                PlayerPrefs.Save();
                Restart();
            }

            StereoPatch hovered = null;
            foreach (var s in Selectors)
                if (s != null && s.isActiveAndEnabled && s.Hovered != null) { hovered = s.Hovered; break; }
            // Look and hold fills the frame as the gaze holds.
            float dwell = dwellSel != null && dwellSel.isActiveAndEnabled && dwellSel.Hovered != null ? dwellSel.Progress : 0f;
            int dwellOn = dwell > 0 ? dwellSel.Hovered.Index : -1;
            bool hc = Options.HighContrast;
            Color idle = hc ? FrameIdleHighContrast : FrameIdle;
            float baseWidth = (frameWidth > 0 ? frameWidth : 0.0025f) * (hc ? 1.8f : 1f);

            for (int i = 0; i < Frames.Length; i++)
            {
                if (Frames[i] == null) continue;
                Color c = idle;
                float w = 1f;
                if (state == State.Feedback)
                {
                    // Catch at the wrong height: the right square, but in the "wrong" colour and thin.
                    if (i == target)
                    {
                        bool miss = rung == SkillLadder.Rung.Catch && !lastCorrect && chosen == target;
                        c = miss ? FrameWrong : FrameRight;
                        w = miss ? 1f : RightFrameWidth;
                    }
                    else if (i == chosen) c = FrameWrong;
                }
                else if (state == State.Trial && rung == SkillLadder.Rung.Catch)
                {
                    // Brightens as the fingertip holds still over a square.
                    if (i == catchOver) c = Color.Lerp(FrameIdle, FrameHover, 0.3f + 0.7f * (float)still.Progress);
                }
                else if (state == State.Tutorial && i == target && tutorialHidden)
                {
                    // Where it hid: green once found, otherwise a slow pulse so a first-timer knows where to pinch.
                    c = tutorialFound ? FrameRight
                                      : Color.Lerp(FrameIdle, FrameHover, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
                    if (!tutorialFound && hovered != null && hovered.Index == i) c = FrameHover;
                    if (tutorialFound) w = RightFrameWidth;
                }
                else if (hovered != null && hovered.Index == i) c = FrameHover;
                if (i == dwellOn && state != State.Feedback)
                {
                    c = Color.Lerp(idle, FrameHover, 0.35f + 0.65f * dwell);
                    w = 1f + 1.2f * dwell;
                }
                // Hover and pulses ease in and out; right / wrong lands at once (feedback must feel instant).
                bool snap = state == State.Feedback || (state == State.Tutorial && tutorialFound);
                Color shown = snap ? c : Color.Lerp(Frames[i].startColor, c, 1f - Mathf.Exp(-14f * Time.deltaTime));
                Frames[i].startColor = Frames[i].endColor = shown;
                Frames[i].widthMultiplier = baseWidth * w;
            }

            // Development builds: the tester line. The judge build shows only what a player needs.
            if (Status != null && Time.time > toastUntil && (state == State.Trial || state == State.Feedback))
                Status.text = DevControls.FingerSwitches
                    ? $"{subjectId}   {rung}   trial {trialIndex + 1}   {ladder.CurrentArcsec:0}″   {DevControls.Eyes}   {Cue}"
                    : Creatures != null && Creatures.FoundThisSession > 0 ? "Found " + Creatures.FoundThisSession : "";
        }

        /// <summary>The static hisses softly from the squares while there is something hidden in it; fades out otherwise.</summary>
        void UpdateBed()
        {
            if (Bed == null || Bed.clip == null) return;
            bool on = state == State.Trial || state == State.Feedback || (state == State.Tutorial && tutorialHidden);
            float target = on ? BedVolume : 0f;
            Bed.volume = Mathf.MoveTowards(Bed.volume, target, Time.deltaTime * BedVolume / 1.2f);
            if (Bed.volume > 0f && !Bed.isPlaying) { Bed.loop = true; Bed.Play(); }
            else if (Bed.volume <= 0f && Bed.isPlaying) Bed.Stop();
            if (Patches.Length > 0 && Patches[0] != null)
            {
                Vector3 c = Vector3.zero;
                foreach (var p in Patches) c += p.transform.position;
                Bed.transform.position = c / Patches.Length;
            }
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
