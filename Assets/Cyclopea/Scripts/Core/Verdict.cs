using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// What one run of the Spot rung is allowed to say, and how sure it is. There are four honest
    /// outcomes and the game shows exactly one:
    ///   Measured  — a number, with the range it is good to (± %), from steady answers.
    ///   AtFloor   — finer than the display can show: "≤ 15″", never a made-up smaller number.
    ///   NoDepth   — repeated misses at the largest depth, on trials the health check passed.
    ///   Uncertain — answers scattered too much, too few rounds, or the system was disturbed:
    ///               "not sure this time", which never counts as a score or as "no depth".
    /// Thresholds here are tuned against <see cref="MeasurementBenchmark"/>, which runs the same code
    /// on simulated players whose true answer is known.
    /// </summary>
    public struct Verdict
    {
        public enum Outcome { Measured, AtFloor, NoDepth, Uncertain }

        public Outcome Result;
        public double Arcsec;          // the estimate (NaN for NoDepth / Uncertain)
        public double SpreadLog;       // SD of the reversals averaged (log10), NaN if none
        public double PlusMinusPercent; // the ± range shown with a Measured score
        public string Reason;          // for Uncertain: a short player-facing reason
        public double FittedLapse;     // lapse rate from the attention check (NaN if not run)

        /// <summary>Counts as a Depth Score (personal best, streak best, leaderboard).</summary>
        public bool IsScore => Result == Outcome.Measured || Result == Outcome.AtFloor;

        /// <summary>
        /// Reversal spread above this (log10) is too scattered to report: ×1.9 between typical reversals.
        /// Chosen with the benchmark: it keeps steady players' scores and drops the wild ones.
        /// </summary>
        public const double MaxSpreadLog = 0.28;

        /// <summary>
        /// The ± range is k·SD/√n of the averaged reversals, widened because neighbouring reversals are
        /// correlated. k is set so the range holds the true answer for ~90 % of simulated players.
        /// </summary>
        public const double RangeK = 3.0;

        /// <summary>
        /// Attention check: when a likelihood fit of every counted Spot trial finds this many lapses and
        /// puts the player's attentive threshold well below the staircase's, the staircase was pushed
        /// coarse by missed easy ones. The game then says "not sure" instead of a score that undersells
        /// the player's eyes. (The fit is never reported as the score: on short runs it can flatter.)
        /// </summary>
        public const double LapseForUnsure = 0.12;
        public const double FitGapLogForUnsure = 0.15;

        /// <param name="levels">Disparity of every counted Spot trial, in order (optional: enables the attention check).</param>
        /// <param name="correct">Whether each of those trials was answered right.</param>
        public static Verdict Judge(SkillLadder ladder, TrialGate gate,
                                    IReadOnlyList<double> levels = null, IReadOnlyList<bool> correct = null)
        {
            var stair = ladder.StaircaseOf(SkillLadder.Rung.Spot);
            var v = Judge(stair, ladder.NoStereoDetected, gate);
            if (v.Result != Outcome.Measured || levels == null || correct == null) return v;
            var fit = ThresholdFit.Fit(levels, correct, 0.25, stair.FloorArcsec, stair.CeilingArcsec);
            v.FittedLapse = fit.Ok ? fit.Lapse : double.NaN;
            if (fit.Ok && fit.Lapse >= LapseForUnsure &&
                DisparityMath.Log10(v.Arcsec) - DisparityMath.Log10(fit.Arcsec) > FitGapLogForUnsure)
            {
                v.Result = Outcome.Uncertain;
                v.Arcsec = double.NaN;
                v.PlusMinusPercent = double.NaN;
                v.Reason = "Some easy ones slipped past you today.";
            }
            return v;
        }

        public static Verdict Judge(Staircase stair, bool noStereoDetected, TrialGate gate)
        {
            var v = new Verdict { Arcsec = double.NaN, SpreadLog = double.NaN, PlusMinusPercent = double.NaN, FittedLapse = double.NaN, Reason = "" };
            if (gate != null && gate.GaveUp)
            {
                v.Result = Outcome.Uncertain;
                v.Reason = "Couldn't measure: " + (string.IsNullOrEmpty(gate.LastReason) ? "the view wasn't steady" : gate.LastReason) + ".";
                return v;
            }
            if (noStereoDetected) { v.Result = Outcome.NoDepth; return v; }

            double thr = stair.ThresholdArcsec();
            if (double.IsNaN(thr) || stair.HitTrialCap)
            {
                v.Result = Outcome.Uncertain;
                v.Reason = "Not enough steady rounds to be sure.";
                return v;
            }
            v.Arcsec = thr;
            if (stair.AtFloor()) { v.Result = Outcome.AtFloor; return v; }

            double sd = stair.ThresholdSpreadLog();
            v.SpreadLog = sd;
            if (!double.IsNaN(sd) && sd > MaxSpreadLog)
            {
                v.Result = Outcome.Uncertain;
                v.Arcsec = double.NaN;
                v.Reason = "Your answers changed a lot today.";
                return v;
            }
            int n = Math.Max(1, stair.ThresholdReversals);
            double halfLog = RangeK * (double.IsNaN(sd) ? MaxSpreadLog : sd) / Math.Sqrt(n);
            v.PlusMinusPercent = (DisparityMath.Pow10(halfLog) - 1) * 100;
            v.Result = Outcome.Measured;
            return v;
        }

        /// <summary>" (±18 %)" for the score card; "" when there is no range to show.</summary>
        public string RangeText()
        {
            if (Result != Outcome.Measured || double.IsNaN(PlusMinusPercent)) return "";
            double r = Math.Max(5, Math.Round(PlusMinusPercent / 5) * 5);
            return " (±" + r.ToString("0") + " %)";
        }
    }
}
