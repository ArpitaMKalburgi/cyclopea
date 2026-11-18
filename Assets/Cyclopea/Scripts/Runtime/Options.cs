using System;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The player's settings, chosen on the home card's Options pages with any input (pinch, look and hold,
    /// thumb tap, controller trigger) and kept between launches:
    ///   High contrast  black and white static, darker room, bolder text and frames
    ///   Look and hold  choose a square by looking at it (no pinch needed)
    ///   Less movement  no reaching (Catch is skipped), and motion mode sways for you
    ///   Large text     every label, card and score is 1.3 times bigger
    ///   Sound          normal, quiet or off
    /// </summary>
    public static class Options
    {
        public const string Key = "cyc_options_v1";
        public const float BigTextScale = 1.3f;
        public const int SoundNormal = 0, SoundQuiet = 1, SoundOff = 2;

        public static bool HighContrast, LookAndHold, LessMovement, BigText;
        /// <summary>SoundNormal, SoundQuiet or SoundOff.</summary>
        public static int Sound;
        public static event Action Changed;

        /// <summary>The listener's volume for the chosen sound level.</summary>
        public static float Volume => Sound == SoundOff ? 0f : Sound == SoundQuiet ? 0.4f : 1f;
        public static string SoundName => Sound == SoundOff ? "Off" : Sound == SoundQuiet ? "Quiet" : "Normal";

        public static void Load()
        {
            string s = PlayerPrefs.GetString(Key, "");
            HighContrast = s.Contains("hc=1");
            LookAndHold = s.Contains("look=1");
            LessMovement = s.Contains("less=1");
            BigText = s.Contains("big=1");
            Sound = s.Contains("snd=2") ? SoundOff : s.Contains("snd=1") ? SoundQuiet : SoundNormal;
            Apply();
        }

        public static void Save()
        {
            // New settings go at the end, so a save from an older build still reads back.
            PlayerPrefs.SetString(Key, "hc=" + (HighContrast ? 1 : 0) + ";look=" + (LookAndHold ? 1 : 0) +
                                       ";less=" + (LessMovement ? 1 : 0) + ";big=" + (BigText ? 1 : 0) + ";snd=" + Sound);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Back to defaults (tests).</summary>
        public static void Reset()
        {
            HighContrast = LookAndHold = LessMovement = BigText = false;
            Sound = SoundNormal;
            PlayerPrefs.DeleteKey(Key);
            Apply();
            Changed?.Invoke();
        }

        static void Apply()
        {
            StereoPatch.SwayAssist = LessMovement;
            LegibleText.Scale = BigText ? BigTextScale : 1f;
            AudioListener.volume = Volume;
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
