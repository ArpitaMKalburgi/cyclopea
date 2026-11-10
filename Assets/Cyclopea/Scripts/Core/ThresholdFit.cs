using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// Maximum-likelihood fit of the player's psychometric function to every counted trial, with the
    /// lapse rate as a free parameter (Wichmann &amp; Hill 2001). A staircase alone drifts coarse when a
    /// player's attention wanders; fitting the lapses separately recovers what their eyes can do when
    /// they are looking. Also gives a likelihood interval (±) for the score.
    ///   p(correct | x) = g + (1 − g − λ) · Φ((log10 x − μ) / σ)
    /// σ is fixed (a typical stereo slope); μ and λ are fitted on a grid.
    /// </summary>
    public static class ThresholdFit
    {
        public struct Result
        {
            public bool Ok;
            public double Arcsec;       // attentive 79.4 % point
            public double LowArcsec, HighArcsec; // ~95 % likelihood interval of that point
            public double Lapse;        // fitted lapse rate
        }

        public const double Sigma = 0.15;
        public const double AttentiveLapse = 0.04;
        const double Step = 0.01;
        const double MaxLapse = 0.30, LapseStep = 0.02;

        public static Result Fit(IReadOnlyList<double> levels, IReadOnlyList<bool> correct, double guess,
                                 double floorArcsec, double ceilingArcsec)
        {
            var r = new Result { Arcsec = double.NaN, LowArcsec = double.NaN, HighArcsec = double.NaN };
            int n = Math.Min(levels.Count, correct.Count);
            if (n < 8) return r;

            double lo = DisparityMath.Log10(floorArcsec) - 0.6, hi = DisparityMath.Log10(ceilingArcsec) + 0.3;
            int steps = (int)Math.Ceiling((hi - lo) / Step) + 1;
            // Staircase levels repeat, so group trials by level: hits and misses per distinct level.
            var at = new Dictionary<long, int>();
            var lx = new List<double>(); var hits = new List<int>(); var miss = new List<int>();
            for (int i = 0; i < n; i++)
            {
                long key = (long)Math.Round(DisparityMath.Log10(levels[i]) * 1e4);
                if (!at.TryGetValue(key, out int j)) { j = lx.Count; at[key] = j; lx.Add(key / 1e4); hits.Add(0); miss.Add(0); }
                if (correct[i]) hits[j]++; else miss[j]++;
            }
            int m = lx.Count;
            var phi = new double[m];

            var profile = new double[steps];   // best log-likelihood over λ, for each μ
            var bestLapse = new double[steps];
            double best = double.NegativeInfinity; int bestI = 0;
            for (int k = 0; k < steps; k++)
            {
                double mu = lo + k * Step;
                for (int j = 0; j < m; j++) phi[j] = Phi((lx[j] - mu) / Sigma);
                double pk = double.NegativeInfinity, pl = 0;
                for (double lam = 0; lam <= MaxLapse + 1e-9; lam += LapseStep)
                {
                    double ll = 0;
                    for (int j = 0; j < m; j++)
                    {
                        double p = guess + (1 - guess - lam) * phi[j];
                        p = Math.Min(1 - 1e-6, Math.Max(1e-6, p));
                        if (hits[j] > 0) ll += hits[j] * Math.Log(p);
                        if (miss[j] > 0) ll += miss[j] * Math.Log(1 - p);
                    }
                    // A weak prior that lapses are rare: without it a short run can explain misses either way.
                    ll += -lam * 6.0;
                    if (ll > pk) { pk = ll; pl = lam; }
                }
                profile[k] = pk; bestLapse[k] = pl;
                if (pk > best) { best = pk; bestI = k; }
            }

            // The attentive 79.4 % point sits a fixed distance above μ.
            double offset = Sigma * InvPhi((0.794 - guess) / (1 - guess - AttentiveLapse));
            double muHat = lo + bestI * Step;
            int a = bestI, b = bestI;
            while (a > 0 && profile[a - 1] >= best - 1.92) a--;
            while (b < steps - 1 && profile[b + 1] >= best - 1.92) b++;

            r.Ok = true;
            r.Arcsec = DisparityMath.Pow10(muHat + offset);
            r.LowArcsec = DisparityMath.Pow10(lo + a * Step + offset);
            r.HighArcsec = DisparityMath.Pow10(lo + b * Step + offset);
            r.Lapse = bestLapse[bestI];
            return r;
        }

        static double Phi(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));

        static double InvPhi(double p)
        {
            double lo = -8, hi = 8;
            for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (Phi(m) < p) lo = m; else hi = m; }
            return 0.5 * (lo + hi);
        }

        static double Erf(double x)
        {
            double sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return sign * y;
        }
    }
}
