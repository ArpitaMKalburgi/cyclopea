using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// Transformed up-down staircase (Levitt 1971) on log10 disparity.
    /// 3-down / 1-up converges on ~79.4% correct. Engine-independent so it can be
    /// unit-tested with a simulated observer and driven on device by XR Operator.
    /// </summary>
    [Serializable]
    public sealed class Staircase
    {
        public sealed class Config
        {
            public double StartArcsec = 1920;
            public double FloorArcsec = 15;      // display limit; set per device (Quest 3 ≈ 15", 3S higher)
            public double CeilingArcsec = 3840;
            public int DownRule = 3;             // correct responses needed to step down
            public double InitialStepLog = 0.2;
            public double FineStepLog = 0.1;
            public int ReversalsBeforeFineStep = 2;
            public int ReversalsToStop = 8;
            public int ReversalsForThreshold = 6;
            public int MaxTrials = 120;
        }

        public enum Direction { None, Down, Up }

        readonly Config cfg;
        double logLevel;
        int consecutiveCorrect;
        Direction lastMove = Direction.None;
        readonly List<double> reversalLogs = new List<double>();

        public int Trials { get; private set; }
        public int Reversals => reversalLogs.Count;
        public bool IsFinished => Reversals >= cfg.ReversalsToStop || Trials >= cfg.MaxTrials;
        public double CurrentArcsec => DisparityMath.Pow10(logLevel);
        public IReadOnlyList<double> ReversalLogs => reversalLogs;

        public Staircase(Config config = null)
        {
            cfg = config ?? new Config();
            logLevel = DisparityMath.Log10(cfg.StartArcsec);
        }

        double StepLog => Reversals >= cfg.ReversalsBeforeFineStep ? cfg.FineStepLog : cfg.InitialStepLog;

        /// <summary>Record the outcome of a trial at <see cref="CurrentArcsec"/>.</summary>
        public void Respond(bool correct)
        {
            if (IsFinished) return;
            Trials++;

            Direction move = Direction.None;
            if (correct)
            {
                consecutiveCorrect++;
                if (consecutiveCorrect >= cfg.DownRule)
                {
                    consecutiveCorrect = 0;
                    move = Direction.Down;
                }
            }
            else
            {
                consecutiveCorrect = 0;
                move = Direction.Up;
            }

            if (move == Direction.None) return;

            if (lastMove != Direction.None && move != lastMove)
                reversalLogs.Add(logLevel);
            lastMove = move;

            double step = StepLog * (move == Direction.Down ? -1 : 1);
            double floorLog = DisparityMath.Log10(cfg.FloorArcsec);
            double ceilLog = DisparityMath.Log10(cfg.CeilingArcsec);
            logLevel = Math.Clamp(logLevel + step, floorLog, ceilLog);
        }

        /// <summary>
        /// Threshold estimate in arcseconds: mean of the last N reversals (log domain).
        /// Returns NaN until at least two reversals exist.
        /// </summary>
        public double ThresholdArcsec()
        {
            if (reversalLogs.Count < 2) return double.NaN;
            int n = Math.Min(cfg.ReversalsForThreshold, reversalLogs.Count);
            double sum = 0;
            for (int i = reversalLogs.Count - n; i < reversalLogs.Count; i++) sum += reversalLogs[i];
            return DisparityMath.Pow10(sum / n);
        }

        /// <summary>True when the estimate sits on the display floor — report as "≤ floor", not as a number.</summary>
        public bool AtFloor() => !double.IsNaN(ThresholdArcsec()) && ThresholdArcsec() <= cfg.FloorArcsec * 1.05;
    }
}
