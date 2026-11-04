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
        }

        readonly List<string> lines = new List<string>();
        public IReadOnlyList<string> Lines => lines;

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
