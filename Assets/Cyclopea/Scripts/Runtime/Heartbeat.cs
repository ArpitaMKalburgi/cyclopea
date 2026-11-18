using System.Globalization;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// A heartbeat every 10 s: what the game is doing, frame rate, hands or controllers, where the squares lie,
    /// memory, garbage collections, heat, battery, worn or not, focus, errors so far. One line that says whether
    /// the app is healthy, in logcat and in the play log ("beat" lines, read by Tools/stats.py).
    /// Ported from the same author's Worldeck.
    /// </summary>
    public sealed class Heartbeat : MonoBehaviour
    {
        public SpotSession Session;
        public WhackRound Arcade;
        public FingerTips Tips;
        public SurfaceFinder Surface;
        public PlayLog Log;
        public SlapCoach Coach;
        public float Seconds = 10f;

        float next;
        readonly float[] recent = new float[90];
        int ri;

        /// <summary>Android's thermal status (0 none … 6 shutdown), or -1 where it can't be read.</summary>
        static int Thermal()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var power = activity.Call<AndroidJavaObject>("getSystemService", "power"))
                    return power.Call<int>("getCurrentThermalStatus");
            }
            catch { return -1; }
#else
            return -1;
#endif
        }

        string State()
        {
            if (Session == null) return "-";
            if (Session.Paused) return "paused";
            if (Session.ShowingNotice) return "notice";
            if (Session.ShowingTutorial) return "tutorial";
            if (Session.InArcade) return Arcade != null && Arcade.Counting ? "countdown" : "round";
            if (Session.ShowingHome) return "home";
            if (Session.AwaitingChoice) return "depth_check";
            return "card";
        }

        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        void Update()
        {
            recent[ri] = Time.unscaledDeltaTime; ri = (ri + 1) % recent.Length;
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + Seconds;
            float sum = 0f; foreach (var d in recent) sum += d;
            float fps = sum > 0f ? recent.Length / sum : 0f;
            bool l = Tips != null && Tips.Left != null && Tips.Left.IsTrackedDataValid;
            bool r = Tips != null && Tips.Right != null && Tips.Right.IsTrackedDataValid;
            string hands = (l ? "L" : "-") + (r ? "R" : "-");
            int battery = SystemInfo.batteryLevel >= 0f ? Mathf.RoundToInt(SystemInfo.batteryLevel * 100f) : -1;
            float hz = OVRManager.display != null ? OVRManager.display.displayFrequency : 0f;
            string fields =
                "\"t\":" + F(Time.realtimeSinceStartup) +
                ",\"state\":\"" + State() + "\"" +
                ",\"fps\":" + F(Mathf.Round(fps)) + ",\"hz\":" + F(hz) +
                ",\"hands\":\"" + hands + "\",\"pads\":" + (ControllerSelector.InUse ? "true" : "false") +
                ",\"surface\":\"" + (Surface != null ? Surface.Source : "-") + "\"" +
                ",\"coach\":\"" + (Coach != null ? Coach.Mode : "") + "\"" +
                ",\"mem_mb\":" + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024) +
                ",\"mono_mb\":" + UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024 * 1024) +
                ",\"gc\":" + System.GC.CollectionCount(0) +
                ",\"thermal\":" + Thermal() +
                ",\"battery\":" + battery + ",\"charging\":" + (SystemInfo.batteryStatus == BatteryStatus.Charging ? "true" : "false") +
                ",\"mounted\":" + (OVRManager.instance == null || OVRManager.isHmdPresent ? "true" : "false") +
                ",\"focus\":" + (Application.isFocused ? "true" : "false") +
                ",\"errors\":" + (Log != null ? Log.Errors.Total : 0);
            if (Log != null) Log.Beat(fields);
            Debug.Log("[Cyclopea] heartbeat " + fields);
        }
    }
}
