using System;
using System.Collections.Generic;
using System.IO;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The play log: one JSONL file per launch (sessions/&lt;launch&gt;-play.jsonl, beside the Depth Check's logs)
    /// with what the launch ran on, every Critter Whack round (score, catches, inputs used, frame-time
    /// percentiles, whether a health fault touched it), health changes, and the first of each kind of error or
    /// exception the app logged. It stays on the headset like every other log; testers' files are pulled over
    /// adb and read by Tools/stats.py and Tools/device_e2e.py. Bounded: errors are deduplicated and capped, the
    /// in-memory lines are dropped once written, and a round is one line.
    /// </summary>
    public sealed class PlayLog : MonoBehaviour
    {
        public SpotSession Session;
        public WhackRound Arcade;
        public SurfaceFinder Surface;
        [Tooltip("Write to the headset's storage (tests may switch it off).")]
        public bool WriteFiles = true;

        /// <summary>Errors and exceptions logged by the app since launch.</summary>
        public readonly ErrorTally Errors = new ErrorTally();

        SessionLog log;
        int written, rounds, errorsAtRoundStart;
        bool wasPlaying;
        string launchId, path;
        const int KeepLinesInMemory = 200;

        public string Path => path;
        public int RoundsLogged => rounds;
        /// <summary>Every line written this launch (tests; bounded by the number of rounds and error kinds).</summary>
        public readonly List<string> Written = new List<string>();

        [Tooltip("Storage stays bounded on a headset played for months: older logs go, and only this many files stay.")]
        public int KeepDays = 30, KeepFiles = 300;

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLog;
            if (Arcade != null) Arcade.Recorded += OnRound;
            Health.Changed += OnHealth;
            Milestones.Reset();
            Milestones.Marked += OnStep;
        }

        void Start() => Milestones.Mark("launch", PlayerPrefs.GetInt(SpotSession.NoticeKey, 0) == 1 ? "returning" : "first");

        /// <summary>A heartbeat line (Heartbeat.cs): how the app is doing right now.</summary>
        public void Beat(string fields)
        {
            Open();
            log.AddBeat(launchId, DateTime.UtcNow, fields);
            Flush();
        }

        void OnStep(string step, string detail)
        {
            Open();
            log.AddStep(launchId, DateTime.UtcNow, step, Time.realtimeSinceStartup, detail);
            Flush();
        }

        /// <summary>Deletes session logs older than <see cref="KeepDays"/> and all but the newest <see cref="KeepFiles"/>.</summary>
        public static int Prune(string dir, int keepDays, int keepFiles, DateTime nowUtc)
        {
            int removed = 0;
            try
            {
                var files = new List<FileInfo>(new DirectoryInfo(dir).GetFiles("*.jsonl"));
                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = 0; i < files.Count; i++)
                {
                    bool old = keepDays > 0 && (nowUtc - files[i].LastWriteTimeUtc).TotalDays > keepDays;
                    if (!old && (keepFiles <= 0 || i < keepFiles)) continue;
                    files[i].Delete();
                    removed++;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] play log prune: " + e.Message); }
            return removed;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            if (Arcade != null) Arcade.Recorded -= OnRound;
            Health.Changed -= OnHealth;
            Milestones.Marked -= OnStep;
            DrainErrors();
            Flush();
        }

        void OnApplicationPause(bool paused) { if (paused) { DrainErrors(); Flush(); } }

        // Any thread: count it now, write it on the main thread.
        void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Errors.Record(type.ToString(), condition, stack, DateTime.UtcNow);
        }

        void Update()
        {
            bool playing = Arcade != null && Arcade.Playing;
            if (playing && !wasPlaying) errorsAtRoundStart = Errors.Total;
            wasPlaying = playing;
            if (DrainErrors()) Flush();
        }

        bool DrainErrors()
        {
            var fresh = Errors.TakeNew();
            if (fresh.Count == 0) return false;
            Open();
            foreach (var e in fresh) log.AddError(launchId, e.FirstUtc, e.Level, e.Message, e.Where, e.Count);
            return true;
        }

        void OnRound(WhackRound.Result r)
        {
            DrainErrors();
            Open();
            rounds++;
            var s = new SessionLog.Round
            {
                SessionId = launchId,
                SubjectId = "P" + PlayerPrefs.GetInt(SpotSession.SubjectKey, 1).ToString("00"),
                Device = Session != null ? Session.DeviceLabel : SystemInfo.deviceModel,
                UtcStart = DateTime.UtcNow.AddSeconds(-r.Seconds),
                Index = rounds,
                Ended = r.Ended ?? "clock",
                Seconds = r.Seconds,
                Score = r.Score, Caught = r.Caught, Escaped = r.Escaped, Fooled = r.Fooled, Whiffs = r.Whiffs,
                BestCombo = r.BestCombo, TotalCaught = r.TotalCaught, FrenzyCaught = r.FrenzyCaught, Golden = r.Golden,
                PersonalBest = r.PersonalBest,
                FinestArcsec = r.FinestArcsec,
                FinestMm = double.IsNaN(r.FinestArcsec) ? double.NaN : DisparityMath.PopOutMeters(r.FinestArcsec, r.ViewingDistanceM) * 1000.0,
                LevelArcsec = r.LevelArcsec,
                ViewingDistanceM = r.ViewingDistanceM,
                Beginner = r.Beginner,
                Cue = Session != null ? Session.PreferredCue : SessionLog.Cue.Stereo,
                Inputs = r.Inputs ?? "",
                Surface = Surface != null ? Surface.Source : "",
                FastHands = r.FastHands,
                Frames = r.Frames,
                Clean = r.Clean,
                Errors = Math.Max(0, Errors.Total - errorsAtRoundStart)
            };
            errorsAtRoundStart = Errors.Total;
            log.Add(s);
            Flush();
            Debug.Log("[Cyclopea] play log: round " + rounds + " (" + s.Ended + ", " + s.Score + " points, " +
                      (r.Frames != null ? r.Frames.Line() : "no frames") + ", errors " + s.Errors + ")");
        }

        void OnHealth(Health.Entry e)
        {
            if (log == null) return;   // the log opens with every part's state; later changes follow it
            log.AddHealth(launchId, DateTime.UtcNow, e.Part, e.Level.ToString(), e.Detail);
        }

        void Open()
        {
            if (log != null) return;
            launchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999) + "-play";
            log = new SessionLog();
            written = 0;
            log.AddApp(launchId, DateTime.UtcNow, Session != null ? Session.DeviceLabel : SystemInfo.deviceModel,
                Application.version, Debug.isDebugBuild ? "dev" : "release", RefreshHz());
            foreach (var e in Health.All) log.AddHealth(launchId, DateTime.UtcNow, e.Part, e.Level.ToString(), e.Detail);
            if (!WriteFiles) return;
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "sessions");
                Directory.CreateDirectory(dir);
                int removed = Prune(dir, KeepDays, KeepFiles, DateTime.UtcNow);
                if (removed > 0) Debug.Log("[Cyclopea] play log: removed " + removed + " old session files");
                path = System.IO.Path.Combine(dir, launchId + ".jsonl");
            }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] play log: " + e.Message); path = null; }
        }

        void Flush()
        {
            if (log == null) return;
            var lines = log.Lines;
            try
            {
                if (WriteFiles && path != null && written < lines.Count)
                    using (var w = new StreamWriter(path, true))
                        for (int i = written; i < lines.Count; i++) w.WriteLine(lines[i]);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cyclopea] play log write failed: " + e.Message);
                Health.Set(Health.Save, Health.Level.Fallback, "play log write failed: " + e.Message, Time.time);
            }
            for (int i = written; i < lines.Count; i++) Written.Add(lines[i]);
            written = lines.Count;
            if (Written.Count > KeepLinesInMemory) Written.RemoveRange(0, Written.Count - KeepLinesInMemory);
            // Written lines are not kept twice: a fresh buffer, same file.
            if (lines.Count > KeepLinesInMemory) { log = new SessionLog(); written = 0; }
        }

        static double RefreshHz()
        {
            try { float hz = UnityEngine.XR.XRDevice.refreshRate; if (hz > 1) return hz; } catch (Exception) { }
            var r = Screen.currentResolution.refreshRateRatio.value;
            return r > 1 ? r : 72;
        }
    }
}
