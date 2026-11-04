using System;

namespace Cyclopea.Core
{
    /// <summary>
    /// Catch rung: a creature held at a fixed physical height above the surface, and a fingertip that
    /// touches it. Pure geometry in metres, no engine types.
    ///
    /// The shader shifts the static inside the outline by +s/2 in one eye and -s/2 in the other, along
    /// the surface. The two lines of sight then cross on the sightline from the eyes' midpoint to the
    /// outline's centre, at distance z·s/(e+s) from the surface. That is a height of h·s/(e+s) above it,
    /// where h is the eyes' height above the surface and e the distance between the eyes. So a creature
    /// is seen where a real object at that height would be, and a real fingertip can touch it.
    /// </summary>
    public static class Reach
    {
        /// <summary>Surface shift (both eyes, total) that shows a point <paramref name="heightM"/> above the surface.</summary>
        public static double ShiftForHeight(double heightM, double eyeHeightM, double ipd)
        {
            if (heightM <= 0 || eyeHeightM <= 0 || ipd <= 0) return 0;
            heightM = Math.Min(heightM, 0.8 * eyeHeightM);
            return ipd * heightM / (eyeHeightM - heightM);
        }

        /// <summary>Height above the surface at which a surface shift <paramref name="shiftM"/> is seen.</summary>
        public static double HeightForShift(double shiftM, double eyeHeightM, double ipd)
            => shiftM <= 0 || ipd <= 0 ? 0 : eyeHeightM * shiftM / (ipd + shiftM);

        /// <summary>How far from the surface point, along the sightline toward the eyes, a shift is seen.</summary>
        public static double SightlineDistance(double shiftM, double viewDistanceM, double ipd)
            => shiftM <= 0 || ipd <= 0 ? 0 : viewDistanceM * shiftM / (ipd + shiftM);

        /// <summary>Hand tracking puts the index tip within about a centimetre, so no window is tighter than this.</summary>
        public const double MinToleranceM = 0.012;
        /// <summary>Beyond that, a touch within 40 % of the creature's height counts.</summary>
        public const double RelativeTolerance = 0.4;

        /// <summary>How far off a touch may be and still catch a creature floating at <paramref name="heightM"/>.</summary>
        public static double Tolerance(double heightM) => Math.Max(MinToleranceM, RelativeTolerance * heightM);

        public enum Outcome { Caught, TooHigh, TooLow, WrongSquare }

        public static Outcome Judge(bool rightSquare, double tipHeightM, double creatureHeightM)
        {
            if (!rightSquare) return Outcome.WrongSquare;
            double err = tipHeightM - creatureHeightM;
            if (Math.Abs(err) <= Tolerance(creatureHeightM)) return Outcome.Caught;
            return err > 0 ? Outcome.TooHigh : Outcome.TooLow;
        }
    }

    /// <summary>
    /// Fires once a point (a fingertip) has stayed within <see cref="RadiusM"/> of where it settled for
    /// <see cref="HoldSeconds"/>. Hand-tracking jitter of a few millimetres does not reset it; a real move
    /// does. When it fires, <see cref="MeanX"/>/<see cref="MeanY"/>/<see cref="MeanZ"/> hold the average
    /// position over the hold, which is steadier than any single sample.
    /// </summary>
    public sealed class StillDetector
    {
        public double HoldSeconds = 0.5;
        public double RadiusM = 0.012;

        double ax, ay, az, since = double.NaN, sx, sy, sz;
        int n;

        public double Progress { get; private set; }
        public double MeanX { get; private set; }
        public double MeanY { get; private set; }
        public double MeanZ { get; private set; }

        public void Reset()
        {
            since = double.NaN;
            Progress = 0;
            n = 0;
            sx = sy = sz = 0;
        }

        /// <summary>Feed a sample at time <paramref name="t"/> (seconds); true on the sample that completes the hold.</summary>
        public bool Feed(double t, double x, double y, double z)
        {
            double dx = x - ax, dy = y - ay, dz = z - az;
            if (double.IsNaN(since) || dx * dx + dy * dy + dz * dz > RadiusM * RadiusM)
            {
                Reset();
                ax = x; ay = y; az = z; since = t;
            }
            sx += x; sy += y; sz += z; n++;
            Progress = Math.Min(1.0, (t - since) / HoldSeconds);
            if (Progress < 1.0) return false;
            MeanX = sx / n; MeanY = sy / n; MeanZ = sz / n;
            Reset();
            return true;
        }
    }
}
