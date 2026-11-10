using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// Two-phase adaptive staircase on log10 disparity.
    ///   Descent:  1-down with a large step until the first miss — gets a good player
    ///             from 1920" to their range in a handful of trials (first-five-minutes).
    ///   Tracking: transformed 3-down / 1-up (Levitt 1971), converging on ~79.4 % correct.
    ///             Only tracking-phase reversals are counted; the threshold is the mean of
    ///             the last N reversals in the log domain.
    /// Engine-independent so it can be unit-tested with a simulated observer and driven
    /// on device by the XR Operator harness.
    /// </summary>
    [Serializable]
    public sealed class Staircase
    {
        public sealed class Config
        {
            public double StartArcsec = 1920;
            public double FloorArcsec = 15;      // display limit; set per device (Quest 3 ≈ 15", 3S higher)
            public double CeilingArcsec = 3840;
            public double DescentStepLog = 0.3;
            public int DownRule = 3;             // correct responses needed to step down in tracking
            public double InitialStepLog = 0.2;
            public double FineStepLog = 0.1;
            public int ReversalsBeforeFineStep = 4;
            public int ReversalsToStop = 10;
            public int ReversalsForThreshold = 6;   // last N of ReversalsToStop; the first ones are settling
            public int MaxTrials = 150;
            // Early exit for players who beat the display: this many trials at the floor
            // with at least this fraction correct ends the run as "≤ floor".
            public int FloorTrialsToStop = 12;
            public double FloorAccuracyToStop = 0.8;
        }

        public enum Phase { Descent, Tracking }
        public enum Direction { None, Down, Up }

        readonly Config cfg;
        double logLevel;
        int consecutiveCorrect;
        Direction lastMove = Direction.None;
        readonly List<double> reversalLogs = new List<double>();

        int floorTrials, floorCorrect;
        bool floorExit;
        bool descentMissPending; // a miss during descent is retested once before ending the phase

        public Phase CurrentPhase { get; private set; } = Phase.Descent;
        public int Trials { get; private set; }
        public int Reversals => reversalLogs.Count;
        public bool IsFinished => floorExit || Reversals >= cfg.ReversalsToStop || Trials >= cfg.MaxTrials;
        bool AtFloorLevel => logLevel <= DisparityMath.Log10(cfg.FloorArcsec) + 1e-9;
        public double CurrentArcsec => DisparityMath.Pow10(logLevel);
        public double CeilingArcsec => cfg.CeilingArcsec;
        public double FloorArcsec => cfg.FloorArcsec;
        public IReadOnlyList<double> ReversalLogs => reversalLogs;

        public Staircase(Config config = null)
        {
            cfg = config ?? new Config();
            logLevel = DisparityMath.Log10(cfg.StartArcsec);
        }

        double TrackingStepLog => Reversals >= cfg.ReversalsBeforeFineStep ? cfg.FineStepLog : cfg.InitialStepLog;

        /// <summary>Record the outcome of a trial at <see cref="CurrentArcsec"/>.</summary>
        public void Respond(bool correct)
        {
            if (IsFinished) return;
            Trials++;

            if (AtFloorLevel)
            {
                floorTrials++;
                if (correct) floorCorrect++;
                if (floorTrials >= cfg.FloorTrialsToStop &&
                    floorCorrect >= cfg.FloorAccuracyToStop * floorTrials)
                {
                    floorExit = true;
                    return;
                }
            }

            if (CurrentPhase == Phase.Descent)
            {
                if (correct)
                {
                    descentMissPending = false;
                    Move(-cfg.DescentStepLog);
                    if (AtFloorLevel)
                        CurrentPhase = Phase.Tracking; // hit the floor without a miss: track from here
                    return;
                }
                if (!descentMissPending)
                {
                    // One miss may be a lapse: retest the same level before giving up the descent.
                    descentMissPending = true;
                    return;
                }
                // Two misses at one level end the descent. Step back up and start tracking;
                // this direction change is not a measured reversal.
                CurrentPhase = Phase.Tracking;
                lastMove = Direction.Up;
                consecutiveCorrect = 0;
                Move(+cfg.InitialStepLog);
                return;
            }

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

            Move(TrackingStepLog * (move == Direction.Down ? -1 : 1));
        }

        void Move(double deltaLog)
        {
            double floorLog = DisparityMath.Log10(cfg.FloorArcsec);
            double ceilLog = DisparityMath.Log10(cfg.CeilingArcsec);
            logLevel = Math.Clamp(logLevel + deltaLog, floorLog, ceilLog);
        }

        /// <summary>
        /// Threshold estimate in arcseconds: mean of the last N tracking reversals (log domain).
        /// Returns NaN until at least two reversals exist.
        /// </summary>
        public double ThresholdArcsec()
        {
            if (floorExit) return cfg.FloorArcsec;
            if (reversalLogs.Count < 2) return double.NaN;
            int n = Math.Min(cfg.ReversalsForThreshold, reversalLogs.Count);
            double sum = 0;
            for (int i = reversalLogs.Count - n; i < reversalLogs.Count; i++) sum += reversalLogs[i];
            return DisparityMath.Pow10(sum / n);
        }

        /// <summary>
        /// Spread (standard deviation, log10 units) of the reversals the estimate averages. A steady
        /// player's reversals sit close together; a distracted one's scatter. NaN with fewer than two.
        /// </summary>
        public double ThresholdSpreadLog()
        {
            if (reversalLogs.Count < 2) return double.NaN;
            int n = Math.Min(cfg.ReversalsForThreshold, reversalLogs.Count);
            double mean = 0;
            for (int i = reversalLogs.Count - n; i < reversalLogs.Count; i++) mean += reversalLogs[i];
            mean /= n;
            double ss = 0;
            for (int i = reversalLogs.Count - n; i < reversalLogs.Count; i++) ss += (reversalLogs[i] - mean) * (reversalLogs[i] - mean);
            return Math.Sqrt(ss / (n - 1));
        }

        /// <summary>How many reversals the estimate averages (the last N).</summary>
        public int ThresholdReversals => Math.Min(cfg.ReversalsForThreshold, reversalLogs.Count);

        /// <summary>True if the run ended because it reached its trial cap, not because it settled.</summary>
        public bool HitTrialCap => !floorExit && Reversals < cfg.ReversalsToStop && Trials >= cfg.MaxTrials;

        /// <summary>True when the estimate sits on the display floor — report as "≤ floor", not as a number.</summary>
        public bool AtFloor()
        {
            if (floorExit) return true;
            double t = ThresholdArcsec();
            return !double.IsNaN(t) && t <= cfg.FloorArcsec * DisparityMath.Pow10(cfg.FineStepLog);
        }
    }
}
