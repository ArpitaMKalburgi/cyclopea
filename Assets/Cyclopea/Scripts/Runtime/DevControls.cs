using System;
using System.IO;
using Cyclopea.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
    /// <summary>
    /// Switches for the Nov 4 device checks, reachable without controllers or menus:
    ///   left middle-finger pinch  : eye tint on/off (left eye red, right eye blue)
    ///   left ring-finger pinch    : cycle eyes Both -> Left only -> Right only (the one-eye control)
    ///   left pinky pinch          : flip disparity sign (if creatures sink instead of float)
    ///   right middle-finger pinch : cycle input: ISDK (point or look + pinch) -> raw OVR ray (backup)
    ///                               -> head-gaze dwell (eyes-only)
    ///   right ring-finger pinch   : cue: stereo <-> motion parallax (the fallback if per-eye rendering fails)
    /// Keyboard in the editor: T, E, O, H (input), M (cue), plus N = next tester id, R = restart.
    /// Finger switches exist only in development builds; the judge build has none of them.
    /// The left index pinch is never used here; it stays the game's select.
    /// </summary>
    public sealed class DevControls : MonoBehaviour
    {
        public static SessionLog.EyeCondition Eyes = SessionLog.EyeCondition.Both;

        public OVRHand LeftHand, RightHand;
        public SpotSession Session;
        public HeadDwellSelector Dwell;
        public HandRaySelector HandRay;
        public IsdkSelector Isdk;

        public enum InputMode { Isdk, RawRay, Dwell }
        public static InputMode Mode = InputMode.Isdk;
        static bool? fingerSwitches;
        // Read lazily: Debug.isDebugBuild may not be touched from a MonoBehaviour field initializer.
        public static bool FingerSwitches { get => fingerSwitches ??= Debug.isDebugBuild; set => fingerSwitches = value; }

        bool lm, lr, lp, rm, rr;

        void Start()
        {
            Apply();
            if (Options.LookAndHold) Mode = InputMode.Dwell;
            SetMode(Mode, false);
        }

        void OnEnable() => Options.Changed += OnOptions;
        void OnDisable() => Options.Changed -= OnOptions;

        /// <summary>"Choose by" on the Options page: look and hold replaces the pinch path, and back.</summary>
        void OnOptions()
        {
            var want = Options.LookAndHold ? InputMode.Dwell : (Mode == InputMode.Dwell ? InputMode.Isdk : Mode);
            if (want != Mode) SetMode(want, false);
        }

        void Update()
        {
            // The one place look-and-hold is switched: the chosen input, or any menu-like screen (so a player
            // who cannot pinch can always get through the notice, the home card and Options), or the idle assist.
            if (Dwell != null)
            {
                bool on = Mode == InputMode.Dwell || (Session != null && Session.WantsLookAndHold);
                if (Dwell.enabled != on) Dwell.enabled = on;
                Dwell.DwellSeconds = Mode == InputMode.Dwell ? 1.4f : 2.0f;   // a little longer where it is not the chosen input
            }

            if (FingerSwitches)
            {
                if (Edge(LeftHand, OVRHand.HandFinger.Middle, ref lm)) ToggleTint();
                if (Edge(LeftHand, OVRHand.HandFinger.Ring, ref lr)) CycleEyes();
                if (Edge(LeftHand, OVRHand.HandFinger.Pinky, ref lp)) FlipSign();
                if (Edge(RightHand, OVRHand.HandFinger.Middle, ref rm)) ToggleDwell();
                if (Edge(RightHand, OVRHand.HandFinger.Ring, ref rr)) ToggleCue();
            }

            if (FingerSwitches && Time.unscaledTime >= nextRemote) { nextRemote = Time.unscaledTime + 0.5f; PollRemote(); }
            if (autoObserver != null) StepAutoplay();

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.tKey.wasPressedThisFrame) ToggleTint();
            if (kb.eKey.wasPressedThisFrame) CycleEyes();
            if (kb.oKey.wasPressedThisFrame) FlipSign();
            if (kb.hKey.wasPressedThisFrame) ToggleDwell();
            if (kb.mKey.wasPressedThisFrame) ToggleCue();
            if (kb.nKey.wasPressedThisFrame) NextTester();
            if (kb.rKey.wasPressedThisFrame && Session != null) Session.Restart();
        }

        // ---------- Remote control for agents (development builds only) ----------
        // Tools/quest.sh push-remote writes <persistentDataPath>/remote.txt over adb; each line is key=value:
        //   tint=1|0  eyes=Both|LeftOnly|RightOnly  sign=1|-1  mode=Isdk|RawRay|Dwell  restart=1  choose=0..3
        //   tutorial=1  cue=Stereo|Motion  sway=1|0  rung=Sort|Catch|all  rungs=1|0
        //   home=1  option=contrast|look|less (toggles, as the Options page does)  decoys=0..1 (chance per Spot trial)
        //   autoplay=<arcsec> (a simulated player plays notice -> tutorial -> round -> score card -> home)  autoplay=0
        // The file is deleted once applied. Lets an agent (Meta VR CLI / XR Operator over MCP) drive the
        // device checks without injecting finger pinches.
        float nextRemote;

        void PollRemote()
        {
            string path = Path.Combine(Application.persistentDataPath, "remote.txt");
            if (!File.Exists(path)) return;
            string[] lines;
            try { lines = File.ReadAllLines(path); File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] remote read failed: " + e.Message); return; }
            foreach (var raw in lines)
            {
                var kv = raw.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim();
                switch (k)
                {
                    case "tint": StereoPatch.DebugEyeTint = v == "1"; Say("Remote: tint " + v); break;
                    case "eyes":
                        if (Enum.TryParse(v, true, out SessionLog.EyeCondition e)) { Eyes = e; Apply(); Say("Remote: eyes " + e); if (Session != null) Session.Restart(); }
                        break;
                    case "sign": StereoPatch.OffsetSign = v.StartsWith("-") ? -1f : 1f; Say("Remote: sign " + StereoPatch.OffsetSign); break;
                    case "mode": if (Enum.TryParse(v, true, out InputMode m)) SetMode(m, true); break;
                    case "restart": if (Session != null) Session.Restart(); break;
                    case "tutorial": if (Session != null) Session.StartTutorial(); break;
                    case "cue":
                        if (Session != null && Enum.TryParse(v, true, out SessionLog.Cue cue)) { Session.StartCue(cue); Say("Remote: cue " + cue); }
                        break;
                    case "sway": StereoPatch.AutoSway = v == "1"; Say("Remote: auto-sway " + v); break;
                    case "choose": if (Session != null && int.TryParse(v, out int i)) Session.Choose(i, "remote"); break;
                    case "rung":   // rung=sort / rung=catch: that rung alone, as practice; rung=all: the whole ladder
                        if (Session == null) break;
                        if (Enum.TryParse(v, true, out SkillLadder.Rung r)) { Session.StartAtRung(r); Say("Remote: rung " + r); }
                        else { Session.StartAllRungs(); Say("Remote: all rungs"); }
                        break;
                    case "home": if (Session != null) Session.ShowHome(); break;
                    case "option":
                        if (v == "contrast") Options.HighContrast = !Options.HighContrast;
                        else if (v == "look") Options.LookAndHold = !Options.LookAndHold;
                        else if (v == "less") Options.LessMovement = !Options.LessMovement;
                        else break;
                        Options.Save();
                        Say("Remote: contrast " + (Options.HighContrast ? "high" : "standard") + ", look and hold " +
                            (Options.LookAndHold ? "on" : "off") + ", less movement " + (Options.LessMovement ? "on" : "off"));
                        break;
                    case "decoys":
                        if (Session != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float dc))
                        { Session.DecoyChance = Mathf.Clamp01(dc); Say("Remote: decoy chance " + Session.DecoyChance); }
                        break;
                    case "rungs": if (Session != null) { Session.LadderRungs = v != "0"; Say("Remote: Sort and Catch " + (Session.LadderRungs ? "on" : "off")); } break;
                    case "autoplay":
                        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double ap) && ap > 0) StartAutoplay(ap);
                        else StopAutoplay("remote");
                        break;
                }
            }
        }

        // ---------- Autoplay (development builds only): an agent plays the whole game on the headset ----------
        // autoplay=<arcsec> starts a simulated player who sees depth down to that many arcseconds (the same
        // observer the tests use). It accepts the notice, finds the tutorial creature, plays round 1 with Sort and
        // Catch, reads the score card, opens the home card and stops. Every step is logged as
        // "[Cyclopea autoplay] ..." for Tools/device_e2e.py. autoplay=0 stops it.
        SimulatedObserver autoObserver;
        System.Random autoRng;
        float autoNext, autoDoneAt;
        string autoLast = "";
        int autoSteps, autoDones;
        readonly System.Collections.Generic.List<int> autoUp = new System.Collections.Generic.List<int>();

        public bool Autoplaying => autoObserver != null;
        /// <summary>Why the last autoplay stopped ("complete: ..." when it reached the home card after a score).</summary>
        public string LastAutoplayStop { get; private set; } = "";
        [Tooltip("Seconds between autoplay actions (the device run uses 0.7 so screenshots catch each screen).")]
        public float AutoplayStepSeconds = 0.7f;

        public void StartAutoplay(double arcsec)
        {
            autoObserver = new SimulatedObserver(arcsec, seed: 11);
            autoRng = new System.Random(5);
            autoNext = Time.time + 1f;
            autoDoneAt = -1f;
            autoLast = "";
            autoSteps = 0;
            autoDones = 0;
            Say("Remote: autoplay " + arcsec + "″");
            AutoLog("start, a player who sees " + arcsec + "″");
        }

        void StopAutoplay(string why)
        {
            if (autoObserver == null) return;
            autoObserver = null;
            LastAutoplayStop = why;
            AutoLog("stop: " + why);
        }

        static void AutoLog(string m) => Debug.Log("[Cyclopea autoplay] " + m);

        string AutoState(SpotSession s) =>
            s.ShowingNotice ? "notice"
            : s.ShowingTutorial ? (!s.TutorialCreatureHidden ? "tutorial"
                                   // The wink plays out (each beat is its own state, so the device run screenshots it).
                                   : s.TutorialWinkBeat >= 0 && s.TutorialWinkBeat < 3 ? "tutorial-wink-" + s.TutorialWinkBeat
                                   : "tutorial-hidden")
            : s.OnBreak ? "break"
            : s.InArcade ? "arcade"
            : s.IntroducingRung ? "rung-intro"
            : s.ShowingHome ? (s.Home != null && s.Home.Current == HomeCard.Page.Check ? "check" : "home")
            : s.IsDone ? "done"
            : s.AwaitingChoice ? "trial " + s.CurrentRung
            : "busy";

        void StepAutoplay()
        {
            var s = Session;
            if (s == null) { StopAutoplay("no session"); return; }
            string st = AutoState(s);
            if (st != autoLast)
            {
                AutoLog("state " + st + " | " + (s.Title != null ? s.Title.text.Replace('\n', ' ') : ""));
                autoLast = st;
            }
            if (Time.time < autoNext || s.Paused) return;
            autoNext = Time.time + AutoplayStepSeconds;
            // Only actions count toward the limit; waiting through feedback and rung intros does not.
            if (autoSteps > 800) { StopAutoplay("action limit reached in state " + st); return; }
            switch (st)
            {
                case "notice": s.Choose(0, "autoplay"); autoSteps++; break;
                case "tutorial": s.Choose(0, "autoplay"); autoSteps++; break;     // a pinch sends it into hiding
                case "tutorial-hidden": s.Choose(s.CurrentTarget, "autoplay"); autoSteps++; break;
                case "break": s.Choose(0, "autoplay"); autoSteps++; break;
                case "done":
                    if (autoDoneAt < 0) { autoDoneAt = Time.time; AutoLog("score card | " + (s.Title != null ? s.Title.text.Replace("\n", " / ") : "")); }
                    else if (Time.time - autoDoneAt > Mathf.Max(0.5f, 6f * AutoplayStepSeconds))
                    {
                        s.Choose(1, "autoplay"); autoSteps++;
                        autoDones++; autoDoneAt = -1f;
                    }
                    break;
                case "home":
                    // First an arcade round (Play), then a two-eye Depth Check, then stop.
                    if (autoDones >= 2) StopAutoplay("complete: home card after an arcade round and a Depth Check");
                    else { s.Choose(autoDones == 0 ? 0 : 1, "autoplay"); autoSteps++; }
                    break;
                case "check": s.Choose(0, "autoplay"); autoSteps++; break;   // two eyes
                case "arcade":
                    if (s.Arcade == null || !s.Arcade.Playing) break;
                    s.Arcade.RealUp(autoUp);
                    if (autoUp.Count == 0) break;
                    autoSteps++;
                    double lvl = s.Arcade.LevelArcsec;
                    if (autoObserver.Respond(lvl))
                    {
                        AutoLog("whack tile " + autoUp[0] + " at " + lvl.ToString("0") + "″, score " + s.Arcade.Score);
                        s.Arcade.Whack(s.Arcade.Tiles[autoUp[0]], "autoplay");
                    }
                    break;
                default:
                    if (!s.AwaitingChoice) break;
                    autoSteps++;
                    double level = s.CurrentArcsec;
                    bool sees = autoObserver.Respond(level);
                    int pick;
                    if (sees) pick = s.CurrentTarget;
                    else if (s.CurrentRung == SkillLadder.Rung.Sort) pick = s.SortOther;
                    else { int w = autoRng.Next(0, 3); pick = w >= s.CurrentTarget ? w + 1 : w; }
                    AutoLog("trial " + (s.TrialsDone + 1) + " " + s.CurrentRung + " " + level.ToString("0") + "″ target " + s.CurrentTarget +
                            (s.CurrentDecoy >= 0 ? " decoy " + s.CurrentDecoy : "") + " -> " + pick);
                    if (s.CurrentRung == SkillLadder.Rung.Catch) s.CatchAt(pick, s.CatchHeightM, "autoplay");
                    else s.Choose(pick, "autoplay");
                    break;
            }
        }

        static bool Edge(OVRHand hand, OVRHand.HandFinger f, ref bool down)
        {
            if (hand == null || !hand.IsTracked) { down = false; return false; }
            float s = hand.GetFingerPinchStrength(f);
            if (!down && s > 0.92f) { down = true; return true; }
            if (down && s < 0.4f) down = false;
            return false;
        }

        void ToggleTint()
        {
            StereoPatch.DebugEyeTint = !StereoPatch.DebugEyeTint;
            Say(StereoPatch.DebugEyeTint ? "Eye tint ON  (left red, right blue)" : "Eye tint off");
        }

        void CycleEyes()
        {
            Eyes = (SessionLog.EyeCondition)(((int)Eyes + 1) % 3);
            Apply();
            Say("Eyes: " + Eyes + (Eyes == SessionLog.EyeCondition.Both ? "" : "  (one-eye control: expect ~25 %)"));
            if (Session != null) Session.Restart();
        }

        void FlipSign()
        {
            StereoPatch.OffsetSign = -StereoPatch.OffsetSign;
            Say(StereoPatch.OffsetSign > 0 ? "Disparity: crossed (+1)" : "Disparity: uncrossed (-1)");
        }

        void ToggleCue()
        {
            if (Session == null) return;
            var next = Session.Cue == SessionLog.Cue.Stereo ? SessionLog.Cue.Motion : SessionLog.Cue.Stereo;
            Session.StartCue(next);
            Say(next == SessionLog.Cue.Motion ? "Motion mode: sway your head to see depth (one eye is enough)"
                                              : "Stereo mode: two eyes only");
        }

        void ToggleDwell() => SetMode((InputMode)(((int)Mode + 1) % 3), true);

        /// <summary>Exactly one pinch path is live at a time, so one pinch can never answer twice.</summary>
        public void SetMode(InputMode m, bool announce)
        {
            Mode = m;
            if (Isdk != null) Isdk.enabled = m == InputMode.Isdk;
            if (HandRay != null) HandRay.enabled = m == InputMode.RawRay || (m == InputMode.Isdk && Isdk == null);
            if (Dwell != null) Dwell.enabled = m == InputMode.Dwell;
            if (!announce) return;
            Say(m == InputMode.Isdk ? "Input: point or look, then pinch (Interaction SDK)"
              : m == InputMode.RawRay ? "Input: raw hand ray + pinch (backup path)"
              : "Eyes-only mode: look and hold");
        }

        void NextTester()
        {
            int id = PlayerPrefs.GetInt(SpotSession.SubjectKey, 1) + 1;
            PlayerPrefs.SetInt(SpotSession.SubjectKey, id);
            PlayerPrefs.Save();
            Say("Tester P" + id.ToString("00"));
            if (Session != null) Session.Restart();
        }

        static void Apply()
        {
            // One-eye control: the covered eye sees flat grey, the other sees the full static.
            StereoPatch.ContrastL = Eyes == SessionLog.EyeCondition.RightOnly ? 0f : 1f;
            StereoPatch.ContrastR = Eyes == SessionLog.EyeCondition.LeftOnly ? 0f : 1f;
        }

        void Say(string msg)
        {
            Debug.Log("[Cyclopea] " + msg);
            if (Session != null) Session.Toast(msg);
        }
    }
}
