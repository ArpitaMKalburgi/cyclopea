using System;
using System.Collections.Generic;

namespace Cyclopea
{
    /// <summary>
    /// The steps of a first session, each marked once per launch: launch, started (the first screen accepted),
    /// tutorial_found, round_started, first_catch, round_done, motion_offered. The play log writes each with the
    /// seconds since launch, so "how long until the first catch" is a number, not a guess.
    /// </summary>
    public static class Milestones
    {
        static readonly HashSet<string> seen = new HashSet<string>();
        public static event Action<string, string> Marked;

        public static void Mark(string step, string detail = "")
        {
            if (!seen.Add(step)) return;
            Marked?.Invoke(step, detail ?? "");
        }

        /// <summary>A new launch (or a test's fresh scene).</summary>
        public static void Reset() => seen.Clear();
    }
}
