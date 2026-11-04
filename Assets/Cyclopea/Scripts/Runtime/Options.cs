using System;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The player's settings, chosen on the home card's Options page with any input (pinch, look and hold,
    /// thumb tap, voice) and kept between launches:
    ///   High contrast  black and white static, darker room, bolder text and frames
    ///   Look and hold  choose a square by looking at it (no pinch needed)
    ///   Less movement  no reaching (Catch is skipped), and motion mode sways for you
    /// </summary>
    public static class Options
    {
        public const string Key = "cyc_options_v1";

        public static bool HighContrast, LookAndHold, LessMovement;
        public static event Action Changed;

        public static void Load()
        {
            string s = PlayerPrefs.GetString(Key, "");
            HighContrast = s.Contains("hc=1");
            LookAndHold = s.Contains("look=1");
            LessMovement = s.Contains("less=1");
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, "hc=" + (HighContrast ? 1 : 0) + ";look=" + (LookAndHold ? 1 : 0) +
                                       ";less=" + (LessMovement ? 1 : 0));
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Back to defaults (tests).</summary>
        public static void Reset()
        {
            HighContrast = LookAndHold = LessMovement = false;
            PlayerPrefs.DeleteKey(Key);
            Apply();
            Changed?.Invoke();
        }

        static void Apply()
        {
            StereoPatch.SwayAssist = LessMovement;
            ApplyPalette();
        }

        /// <summary>The static's colours now. Round 2's desk camouflage defers to high contrast.</summary>
        public static void ApplyPalette()
        {
            var p = HighContrast ? Camouflage.HighContrast : Camouflage.Default;
            StereoPatch.BackgroundColor = new Color((float)p.Background.R, (float)p.Background.G, (float)p.Background.B, 1f);
            StereoPatch.DotColor = new Color((float)p.Dots.R, (float)p.Dots.G, (float)p.Dots.B, 1f);
        }
    }
}
