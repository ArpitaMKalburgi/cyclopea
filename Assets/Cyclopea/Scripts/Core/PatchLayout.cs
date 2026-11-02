using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// Chooses where dot-static patches go on a detected surface so that every one is inside
    /// the airplane-seat radius and none overlap. Pure 2D math in the surface's own plane; the
    /// Unity layer converts to/from world space.
    /// </summary>
    public static class PatchLayout
    {
        public struct Rect { public double X, Y, W, H; }
        public struct Point { public double X, Y; public Point(double x, double y) { X = x; Y = y; } }

        /// <summary>
        /// Place up to <paramref name="count"/> square patches of <paramref name="size"/> metres
        /// inside <paramref name="surface"/>, within <paramref name="reach"/> metres of
        /// <paramref name="origin"/> (the head or shoulder projected onto the plane), at least
        /// <paramref name="gap"/> apart. Deterministic for a given seed so a layout can be replayed.
        /// </summary>
        public static List<Point> Place(Rect surface, Point origin, double reach, int count, double size,
            double gap, int seed)
        {
            var rng = new Random(seed);
            var placed = new List<Point>();
            double half = size / 2;
            double minX = surface.X + half, maxX = surface.X + surface.W - half;
            double minY = surface.Y + half, maxY = surface.Y + surface.H - half;
            if (maxX < minX || maxY < minY) return placed;

            for (int attempt = 0; attempt < 400 && placed.Count < count; attempt++)
            {
                // Sample inside the reach disc, then clamp to the surface.
                double a = rng.NextDouble() * Math.PI * 2;
                double r = Math.Sqrt(rng.NextDouble()) * (reach - half);
                var p = new Point(
                    Clamp(origin.X + Math.Cos(a) * r, minX, maxX),
                    Clamp(origin.Y + Math.Sin(a) * r, minY, maxY));
                if (Dist(p, origin) > reach - half) continue;
                bool ok = true;
                foreach (var q in placed)
                    if (Dist(p, q) < size + gap) { ok = false; break; }
                if (ok) placed.Add(p);
            }
            return placed;
        }

        /// <summary>Largest patch size that still fits <paramref name="count"/> patches on the surface within reach.</summary>
        public static double FitSize(Rect surface, Point origin, double reach, int count, double gap, double maxSize,
            int seed = 1)
        {
            for (double s = maxSize; s >= 0.04; s -= 0.01)
                if (Place(surface, origin, reach, count, s, gap, seed).Count >= count) return s;
            return 0;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        static double Dist(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
    }
}
