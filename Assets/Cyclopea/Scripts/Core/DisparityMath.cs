using System;

namespace Cyclopea.Core
{
    /// <summary>
    /// Pure geometry between binocular disparity (arcseconds), the lateral shift
    /// applied to a surface-anchored patch, and the depth the shape appears to
    /// pop out of that surface. All distances in metres.
    /// </summary>
    public static class DisparityMath
    {
        public const double ArcsecPerRadian = 206264.806;

        /// <summary>Default interpupillary distance when the runtime doesn't report one.</summary>
        public const double DefaultIpd = 0.063;

        /// <summary>Lateral shift on the surface that yields `arcsec` of disparity at viewing distance `z`.</summary>
        public static double ShiftMeters(double arcsec, double z) => arcsec / ArcsecPerRadian * z;

        /// <summary>Disparity produced by a lateral surface shift `s` viewed from distance `z`.</summary>
        public static double ArcsecFromShift(double s, double z) => s / z * ArcsecPerRadian;

        /// <summary>Perceived pop-out depth in front of the surface for `arcsec` at distance `z`.</summary>
        public static double PopOutMeters(double arcsec, double z, double ipd = DefaultIpd)
            => arcsec / ArcsecPerRadian * z * z / ipd;

        /// <summary>
        /// Motion parallax: how far a point `depth` metres in front of the surface appears to slide
        /// along it when the viewer's eye is `lateral` metres to the side of it, at perpendicular
        /// distance `zPerp`. Positive = toward +x. Same projection as stereo: an eye at -ipd/2 sees
        /// +ipd/2 * depth / (z - depth), which is the crossed half-disparity.
        /// </summary>
        public static double MotionShiftMeters(double lateral, double depth, double zPerp)
        {
            if (zPerp <= 0 || depth <= 0) return 0;
            depth = Math.Min(depth, 0.9 * zPerp);
            return -lateral * depth / (zPerp - depth);
        }

        /// <summary>Shader `_EyeOffset` (UV units) for a patch `patchWidth` metres wide.</summary>
        public static float EyeOffsetUV(double arcsec, double z, double patchWidth)
            => (float)(ShiftMeters(arcsec, z) / patchWidth);

        /// <summary>Angular size of one screen pixel at the given pixels-per-degree, in arcseconds.</summary>
        public static double PixelArcsec(double ppd) => 3600.0 / ppd;

        public static double Log10(double v) => Math.Log10(v);
        public static double Pow10(double v) => Math.Pow(10, v);
    }
}
