using System;

namespace Cyclopea.Core
{
    /// <summary>
    /// A synthetic player with a known stereo threshold. Used by the unit tests and
    /// by the on-device XR Operator harness to prove the staircase converges.
    /// Psychometric function: cumulative normal in log disparity, 4-AFC guess rate,
    /// with a small lapse rate.
    /// </summary>
    public sealed class SimulatedObserver
    {
        readonly double logThreshold;
        readonly double slope;
        readonly double guessRate;
        readonly double lapseRate;
        readonly Random rng;

        public SimulatedObserver(double thresholdArcsec, double slopeLog = 0.15,
            double guessRate = 0.25, double lapseRate = 0.05, int seed = 1)
        {
            logThreshold = DisparityMath.Log10(thresholdArcsec);
            slope = slopeLog;
            this.guessRate = guessRate;
            this.lapseRate = lapseRate;
            rng = new Random(seed);
        }

        public double ProbabilityCorrect(double arcsec)
        {
            double x = (DisparityMath.Log10(arcsec) - logThreshold) / slope;
            double phi = 0.5 * (1 + Erf(x / Math.Sqrt(2)));
            return guessRate + (1 - guessRate - lapseRate) * phi;
        }

        public bool Respond(double arcsec) => rng.NextDouble() < ProbabilityCorrect(arcsec);

        /// <summary>
        /// Disparity at which this observer is correct with probability `p` — e.g. 0.794 for
        /// a 3-down/1-up staircase. This, not the 50 % point, is what the staircase estimates.
        /// </summary>
        public double ArcsecAtProbability(double p)
        {
            double lo = DisparityMath.Log10(1), hi = DisparityMath.Log10(100000);
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (ProbabilityCorrect(DisparityMath.Pow10(mid)) < p) lo = mid; else hi = mid;
            }
            return DisparityMath.Pow10(0.5 * (lo + hi));
        }

        // Abramowitz & Stegun 7.1.26 — plenty for a test observer.
        static double Erf(double x)
        {
            double sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);
            const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741,
                         a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }
    }
}
