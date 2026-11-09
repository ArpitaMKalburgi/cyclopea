using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// Two-tone palette for the static, taken from pixels of the player's real surface (passthrough
    /// camera). Dot positions stay a Poisson process, so the palette carries no monocular cue; only
    /// the colours change. Contrast is guaranteed so the dots stay resolvable on any desk.
    /// All values are sRGB in 0..1.
    /// </summary>
    public static class Camouflage
    {
        public struct Rgb
        {
            public double R, G, B;
            public Rgb(double r, double g, double b) { R = r; G = g; B = b; }
            public double Luma => 0.2126 * R + 0.7152 * G + 0.0722 * B;
            public override string ToString() => $"({R:0.00},{G:0.00},{B:0.00})";
        }

        public struct Palette
        {
            public Rgb Background, Dots;
            public double LumaGap => Dots.Luma - Background.Luma;
        }

        public const double MinGap = 0.42;        // luma difference between dots and background
        public const double MaxBackground = 0.40;
        public const double MinBackground = 0.05;
        public const double Desaturate = 0.35;    // pull colours toward grey so dots never read as a pattern

        public static readonly Palette Default = new Palette
        {
            // Deep ink ground, warm cream dots: the look of the game (luma gap 0.84, far above MinGap).
            Background = new Rgb(0.07, 0.08, 0.14),
            Dots = new Rgb(0.98, 0.92, 0.80)
        };

        /// <summary>High-contrast option: pure black ground, pure white dots. Same dot positions, so no new cue.</summary>
        public static readonly Palette HighContrast = new Palette
        {
            Background = new Rgb(0, 0, 0),
            Dots = new Rgb(1, 1, 1)
        };

        /// <summary>Palette from surface pixels: darker half becomes the ground, lighter half the dots.</summary>
        public static Palette FromPixels(IReadOnlyList<Rgb> pixels)
        {
            if (pixels == null || pixels.Count < 8) return Default;
            var sorted = new List<Rgb>(pixels);
            sorted.Sort((a, b) => a.Luma.CompareTo(b.Luma));
            int half = sorted.Count / 2;
            Rgb dark = Mean(sorted, 0, half), light = Mean(sorted, half, sorted.Count);
            if (double.IsNaN(dark.Luma) || double.IsNaN(light.Luma)) return Default;

            double bgLuma = Clamp(dark.Luma, MinBackground, MaxBackground);
            double dotLuma = Math.Min(0.97, Math.Max(light.Luma, bgLuma + MinGap));
            if (dotLuma - bgLuma < MinGap) bgLuma = Math.Max(MinBackground, dotLuma - MinGap);

            return new Palette
            {
                Background = WithLuma(Toward(dark, Desaturate), bgLuma),
                Dots = WithLuma(Toward(light, Desaturate), dotLuma)
            };
        }

        static Rgb Mean(List<Rgb> v, int from, int to)
        {
            double r = 0, g = 0, b = 0; int n = Math.Max(1, to - from);
            for (int i = from; i < to; i++) { r += v[i].R; g += v[i].G; b += v[i].B; }
            return new Rgb(r / n, g / n, b / n);
        }

        static Rgb Toward(Rgb c, double t)
        {
            double y = c.Luma;
            return new Rgb(c.R + (y - c.R) * t, c.G + (y - c.G) * t, c.B + (y - c.B) * t);
        }

        /// <summary>Scale a colour to a target luma, keeping hue; spill into grey where a channel clips.</summary>
        static Rgb WithLuma(Rgb c, double target)
        {
            double y = c.Luma;
            if (y < 1e-4) return new Rgb(target, target, target);
            for (int i = 0; i < 8; i++)
            {
                double k = target / Math.Max(1e-4, c.Luma);
                c = new Rgb(Math.Min(1, c.R * k), Math.Min(1, c.G * k), Math.Min(1, c.B * k));
                double miss = target - c.Luma;
                if (Math.Abs(miss) < 1e-4) break;
                c = new Rgb(Math.Min(1, c.R + miss), Math.Min(1, c.G + miss), Math.Min(1, c.B + miss));
            }
            return c;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
