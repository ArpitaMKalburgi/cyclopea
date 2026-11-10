using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;
using UnityEngine.XR;

namespace Cyclopea
{
    /// <summary>
    /// Watches, every frame, the things the Depth Score silently depends on and reports them into
    /// <see cref="Health"/>, so a failure is shown instead of hidden behind a game that looks fine:
    ///   Two-eye view — the headset really renders two views from two different eye positions.
    ///                  If both eyes got one picture the creatures could not float, and every player
    ///                  would look as if they had no depth vision. That is the failure this exists for.
    ///   Head tracking — lost tracking makes the static swim.
    ///   Frame rate — sustained stutter (over a quarter of frames late in a second) blurs fine depth.
    /// A fault in any of these makes the rounds it touched not count (see <see cref="TrialGate"/>).
    /// It also maps the other parts' own status (room scan, passthrough, sounds, Meta account, desk card)
    /// to Ok / Fallback, with a line for the player when the fallback changes what they see.
    /// A fault is only raised on positive evidence; anything it cannot read is a fallback, never a fault,
    /// so the check itself can never block a measurement by mistake.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SystemCheck : MonoBehaviour
    {
        public SpotSession Session;
        public SurfaceFinder Surface;
        public SoundBank Sounds;
        public PlatformServices Platform;
        public ScoreAnchor Scores;

        [Tooltip("Eye separation outside this range (m) means the two views are not two eyes.")]
        public float MinEyeSeparation = 0.040f, MaxEyeSeparation = 0.085f;
        [Tooltip("Seconds a bad reading must last before it is a fault (one odd frame is not).")]
        public float FaultAfterSeconds = 0.5f;
        [Tooltip("Late frames: more than 1.5 frame budgets. Fault when over this fraction of a 1 s window.")]
        public float SlowFrameFraction = 0.25f;
        [Tooltip("Run the view, head and frame checks in the editor too (tests use this to inject faults).")]
        public bool CheckInEditor;

        /// <summary>Test hook: pretend both eyes get one picture.</summary>
        public static bool SimulateSameImageBothEyes;
        /// <summary>Test hook: pretend head tracking is lost.</summary>
        public static bool SimulateTrackingLost;

        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        float viewBadSince = -1, headBadSince = -1;
        float windowStart;
        int windowFrames, windowSlow;
        float lastSlowFraction;
        float nextSlowCheck;
        Camera eyeCam;
        OVRCameraRig rig;

        public float EyeSeparation { get; private set; } = float.NaN;
        public int Views { get; private set; }

        void OnEnable() => Health.Changed += OnHealthChanged;
        void OnDisable() => Health.Changed -= OnHealthChanged;

        static void OnHealthChanged(Health.Entry e) =>
            Debug.Log("[Cyclopea] health " + e.Part + ": " + e.Level + (e.Detail.Length > 0 ? " (" + e.Detail + ")" : ""));

        void Start()
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            windowStart = Time.unscaledTime;
            nextSlowCheck = Time.time + 8f;   // loading hitches in the first seconds are not a stutter
            Health.Ok(Health.Save, "", Time.time);
            ReportSounds();
        }

        bool Live => !Application.isEditor || CheckInEditor;

        void LateUpdate()
        {
            float now = Time.time;
            CheckView(now);
            CheckHead(now);
            CheckFrames(now);
            if (Time.frameCount % 30 == 0) ReportOthers(now);
        }

        // ---------- Two-eye view ----------

        void CheckView(float now)
        {
            if (!Live) { Health.Set(Health.View, Health.Level.Fallback, "editor: no headset", now); return; }
            bool known = ReadViews(out int views, out float sep, out string mode);
            Views = views; EyeSeparation = sep;
            if (SimulateSameImageBothEyes) { known = true; sep = 0; views = 2; mode = "simulated"; }
            if (!known)
            {
                viewBadSince = -1;
                Health.Set(Health.View, Health.Level.Fallback, "eye views not readable", now);
                return;
            }
            bool bad = views < 2 || sep < MinEyeSeparation || sep > MaxEyeSeparation;
            if (!bad)
            {
                viewBadSince = -1;
                Health.Ok(Health.View, mode + ", " + (sep * 1000).ToString("0") + " mm apart", now);
                return;
            }
            if (viewBadSince < 0) viewBadSince = now;
            if (now - viewBadSince < FaultAfterSeconds) return;
            string why = views < 2 ? "only one view is drawn" : sep < MinEyeSeparation ? "both eyes get the same picture" : "the eye views are too far apart";
            Health.Set(Health.View, Health.Level.Fault, why + " (" + views + " views, " + (sep * 1000).ToString("0") + " mm)", now,
                "Your two eyes are getting the same picture, so nothing can float. Restart Cyclopea.");
        }

        /// <summary>
        /// Eye positions from what the XR display will really render this frame (one pass with two views
        /// for multiview, or two passes). Falls back to the rig's eye anchors. False if neither is readable.
        /// </summary>
        bool ReadViews(out int views, out float sep, out string mode)
        {
            views = 0; sep = float.NaN; mode = "";
            if (eyeCam == null) eyeCam = Camera.main;
            SubsystemManager.GetSubsystems(displays);
            XRDisplaySubsystem d = null;
            foreach (var x in displays) if (x.running) { d = x; break; }
            if (d != null && eyeCam != null)
            {
                try
                {
                    var eyes = new List<Vector3>(2);
                    int passes = d.GetRenderPassCount();
                    for (int p = 0; p < passes; p++)
                    {
                        d.GetRenderPass(p, out var pass);
                        int n = pass.GetRenderParameterCount();
                        for (int i = 0; i < n; i++)
                        {
                            pass.GetRenderParameter(eyeCam, i, out var par);
                            eyes.Add(par.view.inverse.GetColumn(3));
                        }
                    }
                    views = eyes.Count;
                    mode = passes == 1 && views == 2 ? "multiview" : passes + " passes";
                    if (views >= 2) { sep = Vector3.Distance(eyes[0], eyes[1]); return true; }
                    if (views == 1) return true;
                }
                catch (System.Exception) { /* not readable this frame: try the rig */ }
            }
            if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.leftEyeAnchor != null && rig.rightEyeAnchor != null && XRSettings.enabled)
            {
                views = 2;
                mode = "eye anchors";
                sep = Vector3.Distance(rig.leftEyeAnchor.position, rig.rightEyeAnchor.position);
                return true;
            }
            return false;
        }

        // ---------- Head tracking ----------

        void CheckHead(float now)
        {
            if (!Live) { Health.Set(Health.Head, Health.Level.Fallback, "editor: no headset", now); return; }
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = false;
            bool known = head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
            if (SimulateTrackingLost) { known = true; tracked = false; }
            if (!known) { headBadSince = -1; Health.Set(Health.Head, Health.Level.Fallback, "not readable", now); return; }
            if (tracked) { headBadSince = -1; Health.Ok(Health.Head, "", now); return; }
            if (Session != null && Session.Paused) { headBadSince = -1; return; }   // headset off: paused anyway
            if (headBadSince < 0) headBadSince = now;
            if (now - headBadSince < FaultAfterSeconds) return;
            Health.Set(Health.Head, Health.Level.Fault, "head not tracked", now,
                "Lost track of your head. Look around your room for a moment.");
        }

        // ---------- Frame rate ----------

        void CheckFrames(float now)
        {
            if (!Live) { Health.Set(Health.Frames, Health.Level.Fallback, "editor", now); return; }
            float hz = XRDevice.refreshRate > 1 ? XRDevice.refreshRate : 72f;
            float dt = Time.unscaledDeltaTime;
            bool paused = Session != null && Session.Paused;
            if (!paused)
            {
                windowFrames++;
                if (dt > 1.5f / hz) windowSlow++;
            }
            if (Time.unscaledTime - windowStart < 1f) return;
            lastSlowFraction = windowFrames > 0 ? (float)windowSlow / windowFrames : 0;
            windowStart = Time.unscaledTime; windowFrames = 0; windowSlow = 0;
            if (paused || now < nextSlowCheck) return;
            if (lastSlowFraction > SlowFrameFraction)
                Health.Set(Health.Frames, Health.Level.Fault, (lastSlowFraction * 100).ToString("0") + " % late frames at " + hz.ToString("0") + " Hz", now,
                    "The view stuttered, so that round didn't count.");
            else
                Health.Ok(Health.Frames, hz.ToString("0") + " Hz", now);
        }

        /// <summary>Fraction of late frames in the last full second (for tests and the device log).</summary>
        public float SlowFraction => lastSlowFraction;

        // ---------- Other parts' own status ----------

        void ReportSounds()
        {
            if (Sounds == null) { Health.Set(Health.Sound, Health.Level.Fallback, "no sound set: plain tones", Time.time); return; }
            int missing = Sounds.MissingCount();
            if (missing == 0) Health.Ok(Health.Sound, "", Time.time);
            else Health.Set(Health.Sound, Health.Level.Fallback, missing + " sounds missing: plain tones instead", Time.time);
        }

        void ReportOthers(float now)
        {
            if (Surface != null)
            {
                switch (Surface.Source)
                {
                    case "scene": Health.Ok(Health.Room, "room scan", now); break;
                    case "depth":
                        Health.Set(Health.Room, Health.Level.Fallback, "no room scan: depth raycast", now,
                            "No room scan: the squares sit on the nearest flat spot."); break;
                    default:
                        Health.Set(Health.Room, Health.Level.Fallback, "no room scan or depth: floating", now,
                            "No room scan: the squares float in front of you."); break;
                }
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (OVRManager.HasInsightPassthroughInitFailed())
                Health.Set(Health.Passthrough, Health.Level.Fallback, "passthrough failed to start", now,
                    "Can't show your room right now, so the game plays on dark.");
            else if (OVRManager.IsInsightPassthroughInitialized())
                Health.Ok(Health.Passthrough, "", now);
            else
                Health.Set(Health.Passthrough, Health.Level.Fallback, "passthrough starting", now);
#endif
            if (Platform != null && !string.IsNullOrEmpty(Platform.Status))
            {
                if (Platform.Status == "entitled") Health.Ok(Health.Platform, "entitled", now);
                else Health.Set(Health.Platform, Health.Level.Fallback, Platform.Status + ": achievements and leaderboard off", now);
            }
            if (Scores != null && !string.IsNullOrEmpty(Scores.Status))
            {
                string s = Scores.Status;
                if (s.Contains("fail") || s.Contains("threw"))
                    Health.Set(Health.Anchor, Health.Level.Fallback, s, now,
                        "Your desk card couldn't be saved here, so it won't be on the desk tomorrow.");
                else Health.Ok(Health.Anchor, s, now);
            }
        }
    }
}
