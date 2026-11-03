using System;
using System.Globalization;

namespace Cyclopea.Core
{
    /// <summary>
    /// Days-in-a-row counter for the daily Depth Check. It rewards showing up, not eyesight, so a
    /// stereoblind player can top it too. Dates are local calendar days in ISO form (yyyy-MM-dd).
    /// </summary>
    public static class Streak
    {
        public struct State { public string LastDay; public int Count; public int Best; }

        public static State Update(State s, DateTime today)
        {
            string t = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (s.LastDay == t) return s;   // already counted today
            bool yesterday = DateTime.TryParseExact(s.LastDay ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                    DateTimeStyles.None, out var last)
                             && (today.Date - last.Date).TotalDays == 1;
            int count = yesterday ? s.Count + 1 : 1;
            return new State { LastDay = t, Count = count, Best = Math.Max(s.Best, count) };
        }
    }
}
