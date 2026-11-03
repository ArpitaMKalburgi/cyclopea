using System;
using System.Text.RegularExpressions;

namespace Cyclopea.Core
{
    /// <summary>
    /// Spoken alternative to the pinch (Accessibility: voice alternatives for hand interactions).
    /// Maps a transcript to a square of the 2x2 grid (0 far-left, 1 far-right, 2 near-left,
    /// 3 near-right) or to "play again". Engine-agnostic: any speech-to-text source feeds it.
    /// </summary>
    public static class VoiceCommands
    {
        public struct Command
        {
            public int Square;     // -1 = none
            public bool Again;
            public bool IsEmpty => Square < 0 && !Again;
        }

        static readonly string[][] Numbers =
        {
            new[] { "1", "one", "won", "first" },
            new[] { "2", "two", "second" },
            new[] { "3", "three", "tree", "third" },
            new[] { "4", "four", "fourth", "forth" },
        };

        public static Command Parse(string transcript)
        {
            var none = new Command { Square = -1 };
            if (string.IsNullOrWhiteSpace(transcript)) return none;
            string t = " " + Regex.Replace(transcript.ToLowerInvariant(), @"[^a-z0-9 ]", " ") + " ";
            t = Regex.Replace(t, @"\s+", " ");

            if (Has(t, "again", "restart", "play again", "new game")) return new Command { Square = -1, Again = true };

            bool top = Has(t, "top", "upper", "far", "back", "up");
            bool bottom = Has(t, "bottom", "lower", "near", "front", "down", "close");
            bool left = Has(t, "left");
            bool right = Has(t, "right");
            if ((top || bottom) && (left || right) && !(top && bottom) && !(left && right))
                return new Command { Square = (bottom ? 2 : 0) + (right ? 1 : 0) };

            // Numbers only when said as a choice ("number two", "two", "square three"), not inside other words.
            int found = -1;
            for (int i = 0; i < Numbers.Length; i++)
                foreach (var w in Numbers[i])
                    if (t.Contains(" " + w + " ")) { if (found >= 0 && found != i) return none; found = i; }
            return found >= 0 ? new Command { Square = found } : none;
        }

        static bool Has(string t, params string[] words)
        {
            foreach (var w in words) if (t.Contains(" " + w + " ")) return true;
            return false;
        }
    }
}
