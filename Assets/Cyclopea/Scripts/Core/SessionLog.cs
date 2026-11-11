using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cyclopea.Core
{
    /// <summary>
    /// Append-only trial log, one JSON object per line. This is the raw material for every
    /// published number: test-retest reliability, the one-eye chance control, the population
    /// comparison. Written without any JSON library so it stays engine-independent.
    /// </summary>
    public sealed class SessionLog
    {
        public enum EyeCondition { Both, LeftOnly, RightOnly }
        public enum CatchMode { ReachToDepth, FourAFC, TwoAFC }
        /// <summary>What carries the creature: binocular disparity, or motion parallax from head movement (one eye is enough).</summary>
        public enum Cue { Stereo, Motion }

        public sealed class Trial
        {
            public string SessionId;
            public string SubjectId;          // anonymous, device-generated
            public string Device;             // e.g. "Quest 3"
            public DateTime UtcTime;
            public int Index;
            public double LevelArcsec;
            public double ViewingDistanceM;
            public CatchMode Mode;
            public EyeCondition Eyes;
            public bool Correct;
            public int ReactionMs;
            public bool Decoy;                // a flat fake outline was in one of the other squares (not scored)
            public bool DecoyPicked;          // ...and the player picked the fake
            public string Phase;              // Descent / Tracking
            public Cue Cue;
            public string Rung = "Spot";      // Spot / Sort / Catch (SkillLadder.Rung)
            public double HeightM = double.NaN;      // Catch: how high the creature was held above the surface
            public double DepthErrorM = double.NaN;  // Catch: fingertip height minus that (NaN when the square was wrong)
            public bool Clean = true;         // false: a health fault touched this trial, so it was not counted
            public string Fault = "";         // ...and what it was
        }

        public sealed class Summary
        {
            public string SessionId;
            public string SubjectId;
            public string Device;
            public DateTime UtcStart;
            public int Trials;
            public double ThresholdArcsec;    // NaN if none
            public bool AtFloor;
            public double FloorArcsec;
            public EyeCondition Eyes;
            public Cue Cue;
            public string Rung = "Spot";
            public string Outcome = "";       // Verdict: Measured / AtFloor / NoDepth / Uncertain
            public double PlusMinusPercent = double.NaN;
            public double FittedLapse = double.NaN;
            public int DirtyTrials;           // trials not counted because a health fault touched them
        }

        /// <summary>One Critter Whack round: how it went, how it was played, and how the headset ran during it.</summary>
        public sealed class Round
        {
            public string SessionId;          // the app launch (one play log per launch)
            public string SubjectId;
            public string Device;
            public DateTime UtcStart;
            public int Index;                 // rounds this launch, from 1
            public string Ended = "clock";    // clock / left (menu, break, headset off then Home)
            public double Seconds;            // in play (pauses not counted)
            public int Score, Caught, Escaped, Fooled, Whiffs, BestCombo, TotalCaught, FrenzyCaught, Golden;
            public bool PersonalBest;
            public double FinestArcsec = double.NaN;
            public double FinestMm = double.NaN;
            public double LevelArcsec;
            public double ViewingDistanceM;
            public double Beginner;           // the new-player ease at the start (1 new, 0 from 40 caught)
            public Cue Cue;
            public string Inputs = "";        // "slap:21,pinch:2"
            public string Surface = "";       // desk / table / easel ... (where the grid lay)
            public bool FastHands;
            public FrameStats Frames;
            public bool Clean = true;         // false: a critical health fault happened during the round
            public int Errors;                // errors and exceptions logged by the app during the round
        }

        readonly List<string> lines = new List<string>();
        public IReadOnlyList<string> Lines => lines;

        public void Add(Round r)
        {
            var f = r.Frames;
            lines.Add(
                "{" +
                S("type", "round") + "," +
                S("session", r.SessionId) + "," +
                S("subject", r.SubjectId) + "," +
                S("device", r.Device) + "," +
                S("utc", r.UtcStart.ToString("o", CultureInfo.InvariantCulture)) + "," +
                N("i", r.Index) + "," +
                S("ended", r.Ended) + "," +
                N("seconds", Math.Round(r.Seconds, 2)) + "," +
                N("score", r.Score) + "," +
                N("caught", r.Caught) + "," +
                N("escaped", r.Escaped) + "," +
                N("fooled", r.Fooled) + "," +
                N("whiffs", r.Whiffs) + "," +
                N("best_combo", r.BestCombo) + "," +
                N("frenzy_caught", r.FrenzyCaught) + "," +
                N("golden", r.Golden) + "," +
                N("total_caught", r.TotalCaught) + "," +
                B("personal_best", r.PersonalBest) + "," +
                (double.IsNaN(r.FinestArcsec) ? "\"finest_arcsec\":null" : N("finest_arcsec", r.FinestArcsec)) + "," +
                (double.IsNaN(r.FinestMm) ? "\"finest_mm\":null" : N("finest_mm", Math.Round(r.FinestMm, 2))) + "," +
                N("level_arcsec", Math.Round(r.LevelArcsec, 1)) + "," +
                N("z_m", Math.Round(r.ViewingDistanceM, 3)) + "," +
                N("beginner", Math.Round(r.Beginner, 2)) + "," +
                S("cue", r.Cue.ToString()) + "," +
                S("inputs", r.Inputs) + "," +
                S("surface", r.Surface) + "," +
                B("fast_hands", r.FastHands) + "," +
                B("clean", r.Clean) + "," +
                N("errors", r.Errors) +
                (f != null && f.Count > 0
                    ? "," + N("frames", f.Count) + "," + N("hz", Math.Round(f.RefreshHz, 1)) + "," +
                      N("fps_mean", Math.Round(f.MeanFps, 1)) + "," + N("ms_p50", f.Percentile(0.5)) + "," +
                      N("ms_p95", f.Percentile(0.95)) + "," + N("ms_p99", f.Percentile(0.99)) + "," +
                      N("ms_max", Math.Round(f.MaxMs, 2)) + "," + N("late_pct", Math.Round(f.LatePercent, 2))
                    : "") +
                "}");
        }

        /// <summary>The app launch: what it ran on, once at the top of a play log.</summary>
        public void AddApp(string sessionId, DateTime utc, string device, string version, string build, double refreshHz) => lines.Add(
            "{" +
            S("type", "app") + "," +
            S("session", sessionId) + "," +
            S("utc", utc.ToString("o", CultureInfo.InvariantCulture)) + "," +
            S("device", device) + "," +
            S("version", version) + "," +
            S("build", build) + "," +
            N("hz", Math.Round(refreshHz, 1)) +
            "}");

        /// <summary>An error or exception the app logged (the first of its kind; repeats are counted in Count).</summary>
        public void AddError(string sessionId, DateTime utc, string level, string message, string where, int count) => lines.Add(
            "{" +
            S("type", "error") + "," +
            S("session", sessionId) + "," +
            S("utc", utc.ToString("o", CultureInfo.InvariantCulture)) + "," +
            S("level", level) + "," +
            S("message", message) + "," +
            S("where", where) + "," +
            N("count", count) +
            "}");

        public void Add(Trial t) => lines.Add(
            "{" +
            S("type", "trial") + "," +
            S("session", t.SessionId) + "," +
            S("subject", t.SubjectId) + "," +
            S("device", t.Device) + "," +
            S("utc", t.UtcTime.ToString("o", CultureInfo.InvariantCulture)) + "," +
            N("i", t.Index) + "," +
            N("arcsec", t.LevelArcsec) + "," +
            N("z_m", t.ViewingDistanceM) + "," +
            S("mode", t.Mode.ToString()) + "," +
            S("eyes", t.Eyes.ToString()) + "," +
            B("correct", t.Correct) + "," +
            N("rt_ms", t.ReactionMs) + "," +
            B("decoy", t.Decoy) + "," +
            (t.Decoy ? B("decoy_picked", t.DecoyPicked) + "," : "") +
            S("phase", t.Phase) + "," +
            S("cue", t.Cue.ToString()) + "," +
            S("rung", t.Rung) +
            (t.Clean ? "" : "," + B("clean", false) + "," + S("fault", t.Fault)) +
            (double.IsNaN(t.HeightM) ? "" : "," + N("height_m", t.HeightM)) +
            (double.IsNaN(t.DepthErrorM) ? "" : "," + N("depth_err_m", t.DepthErrorM)) +
            "}");

        public void Add(Summary s) => lines.Add(
            "{" +
            S("type", "summary") + "," +
            S("session", s.SessionId) + "," +
            S("subject", s.SubjectId) + "," +
            S("device", s.Device) + "," +
            S("utc", s.UtcStart.ToString("o", CultureInfo.InvariantCulture)) + "," +
            N("trials", s.Trials) + "," +
            (double.IsNaN(s.ThresholdArcsec) ? "\"threshold_arcsec\":null" : N("threshold_arcsec", s.ThresholdArcsec)) + "," +
            B("at_floor", s.AtFloor) + "," +
            N("floor_arcsec", s.FloorArcsec) + "," +
            S("eyes", s.Eyes.ToString()) + "," +
            S("cue", s.Cue.ToString()) + "," +
            S("rung", s.Rung) +
            (s.Outcome.Length > 0 ? "," + S("outcome", s.Outcome) : "") +
            (double.IsNaN(s.PlusMinusPercent) ? "" : "," + N("plus_minus_pct", s.PlusMinusPercent)) +
            (double.IsNaN(s.FittedLapse) ? "" : "," + N("fitted_lapse", s.FittedLapse)) +
            (s.DirtyTrials > 0 ? "," + N("dirty_trials", s.DirtyTrials) : "") +
            "}");

        /// <summary>A health change during the session (part, level, detail), so a log shows what the system was doing.</summary>
        public void AddHealth(string sessionId, DateTime utc, string part, string level, string detail) => lines.Add(
            "{" +
            S("type", "health") + "," +
            S("session", sessionId) + "," +
            S("utc", utc.ToString("o", CultureInfo.InvariantCulture)) + "," +
            S("part", part) + "," +
            S("level", level) + "," +
            S("detail", detail) +
            "}");

        public string ToJsonLines()
        {
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        static string S(string k, string v) => "\"" + k + "\":\"" + Escape(v ?? "") + "\"";
        static string N(string k, double v) => "\"" + k + "\":" + v.ToString("R", CultureInfo.InvariantCulture);
        static string B(string k, bool v) => "\"" + k + "\":" + (v ? "true" : "false");

        static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
