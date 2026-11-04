using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cyclopea.Core
{
    /// <summary>
    /// Depth Scores across days: the finest score of each day, per cue, kept on the device. It is what
    /// the home card and the card left on the desk draw as growth between sessions. Fewer arcseconds is
    /// finer depth. Only measured sessions count; showing up is the streak's job, not this.
    /// </summary>
    public sealed class ProgressHistory
    {
        public struct Day
        {
            public string Date;              // local calendar day, yyyy-MM-dd
            public SessionLog.Cue Cue;
            public double BestArcsec;
            public int Sessions;
        }

        public const int KeepDays = 120;
        /// <summary>The graph's scale: from the first level a session shows down to finer than any headset can.</summary>
        public const double GraphCoarseArcsec = 1920, GraphFineArcsec = 10;

        readonly List<Day> days = new List<Day>();   // by date, then cue

        public int Count => days.Count;
        public IReadOnlyList<Day> Days => days;

        /// <summary>
        /// Adds a measured session. True when it beats every earlier score with that cue, as the card shows
        /// it (whole arcseconds); a first score is a start, not a personal best.
        /// </summary>
        public bool Record(DateTime localNow, SessionLog.Cue cue, double arcsec)
        {
            if (double.IsNaN(arcsec) || double.IsInfinity(arcsec) || arcsec <= 0) return false;
            var best = Best(cue);
            bool beaten = best.HasValue && Math.Round(arcsec) < Math.Round(best.Value.BestArcsec);
            string d = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int i = days.FindIndex(x => x.Date == d && x.Cue == cue);
            if (i >= 0)
            {
                var x = days[i];
                x.BestArcsec = Math.Min(x.BestArcsec, arcsec);
                x.Sessions++;
                days[i] = x;
            }
            else
            {
                days.Add(new Day { Date = d, Cue = cue, BestArcsec = arcsec, Sessions = 1 });
                days.Sort((a, b) => a.Date != b.Date ? string.CompareOrdinal(a.Date, b.Date) : a.Cue.CompareTo(b.Cue));
                Trim();
            }
            return beaten;
        }

        /// <summary>The finest day for a cue, or null if that cue has never been measured.</summary>
        public Day? Best(SessionLog.Cue cue)
        {
            Day? best = null;
            foreach (var x in days)
                if (x.Cue == cue && (!best.HasValue || x.BestArcsec < best.Value.BestArcsec)) best = x;
            return best;
        }

        /// <summary>The last <paramref name="n"/> days played with a cue, oldest first.</summary>
        public List<Day> Recent(SessionLog.Cue cue, int n)
        {
            var r = new List<Day>();
            for (int i = days.Count - 1; i >= 0 && r.Count < n; i--)
                if (days[i].Cue == cue) r.Insert(0, days[i]);
            return r;
        }

        /// <summary>Stereo once the player has a stereo score; otherwise motion (one-eye players), or stereo by default.</summary>
        public SessionLog.Cue MainCue => Best(SessionLog.Cue.Stereo).HasValue || !Best(SessionLog.Cue.Motion).HasValue
            ? SessionLog.Cue.Stereo : SessionLog.Cue.Motion;

        /// <summary>0 at the coarse end of the graph, 1 at the fine end, on a log scale (each halving is the same step).</summary>
        public static double GraphHeight(double arcsec)
        {
            if (double.IsNaN(arcsec) || arcsec <= 0) return 0;
            double t = (Math.Log(GraphCoarseArcsec) - Math.Log(arcsec)) / (Math.Log(GraphCoarseArcsec) - Math.Log(GraphFineArcsec));
            return Math.Max(0, Math.Min(1, t));
        }

        void Trim()
        {
            // Keep the last KeepDays calendar days that have any entry.
            var distinct = new List<string>();
            foreach (var x in days) if (distinct.Count == 0 || distinct[distinct.Count - 1] != x.Date) distinct.Add(x.Date);
            if (distinct.Count <= KeepDays) return;
            string cutoff = distinct[distinct.Count - KeepDays];
            days.RemoveAll(x => string.CompareOrdinal(x.Date, cutoff) < 0);
        }

        /// <summary>"2026-11-04,S,83.2,2;2026-11-05,M,240,1" (S = stereo, M = motion).</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var x in days)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(x.Date).Append(',').Append(x.Cue == SessionLog.Cue.Motion ? 'M' : 'S').Append(',')
                  .Append(x.BestArcsec.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                  .Append(x.Sessions.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Tolerant: an unreadable entry is skipped, so a damaged save never blocks play.</summary>
        public static ProgressHistory Parse(string s)
        {
            var h = new ProgressHistory();
            if (string.IsNullOrEmpty(s)) return h;
            foreach (var entry in s.Split(';'))
            {
                var f = entry.Split(',');
                if (f.Length != 4) continue;
                if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                if (f[1] != "S" && f[1] != "M") continue;
                if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) || !(a > 0)) continue;
                if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1) n = 1;
                var cue = f[1] == "M" ? SessionLog.Cue.Motion : SessionLog.Cue.Stereo;
                int i = h.days.FindIndex(x => x.Date == f[0] && x.Cue == cue);
                if (i >= 0) continue;   // first entry for a day and cue wins
                h.days.Add(new Day { Date = f[0], Cue = cue, BestArcsec = a, Sessions = n });
            }
            h.days.Sort((a, b) => a.Date != b.Date ? string.CompareOrdinal(a.Date, b.Date) : a.Cue.CompareTo(b.Cue));
            h.Trim();
            return h;
        }

        /// <summary>"Nov 5" for a stored day.</summary>
        public static string ShortDate(string isoDay) =>
            DateTime.TryParseExact(isoDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("MMM d", CultureInfo.InvariantCulture) : isoDay;
    }

    /// <summary>Lines for the home card: something to work toward, in the game's own terms.</summary>
    public static class Goals
    {
        /// <summary>The disparity a creature must be found below to reach a rarity tier (Common: any).</summary>
        public static double Below(Creatures.Rarity r) =>
            r == Creatures.Rarity.Uncommon ? 480 : r == Creatures.Rarity.Rare ? 120 : r == Creatures.Rarity.Elusive ? 40 : 1920;

        /// <summary>
        /// The next rarity tier to find, or the field guide to finish once every tier is found.
        /// "Next: a Rare creature. They hide below 120″." Rarity comes from how fine the depth was, so
        /// this goal is a depth goal in creature form.
        /// </summary>
        public static string Next(CreatureCollection c)
        {
            var best = c != null ? c.Best : null;
            if (!best.HasValue) return "Next: find your first creature.";
            if (best.Value < Creatures.Rarity.Elusive)
            {
                var next = best.Value + 1;
                return "Next: a" + (next == Creatures.Rarity.Uncommon || next == Creatures.Rarity.Elusive ? "n " : " ") +
                       next + " creature. They hide below " + Below(next).ToString("0") + "″.";
            }
            int left = Creatures.Kinds - c.KindsFound;
            return left > 0 ? "Next: finish the field guide. " + left + (left == 1 ? " kind" : " kinds") + " to go."
                            : "Field guide complete. Keep your streak going.";
        }

        /// <summary>"Best 62″ (Nov 5)" or "" before the first score.</summary>
        public static string BestLine(ProgressHistory h, SessionLog.Cue cue)
        {
            var b = h != null ? h.Best(cue) : null;
            if (!b.HasValue) return "";
            return (cue == SessionLog.Cue.Motion ? "Motion best " : "Best ") + b.Value.BestArcsec.ToString("0") + "″ (" +
                   ProgressHistory.ShortDate(b.Value.Date) + ")";
        }

        /// <summary>
        /// The streak as the home card says it: "Day 4 in a row" once today is played, "3 days in a row.
        /// Play today for 4" while it can still be kept, nothing once it has lapsed.
        /// </summary>
        public static string StreakLine(Streak.State s, DateTime today)
        {
            if (string.IsNullOrEmpty(s.LastDay) || s.Count <= 0) return "";
            string t = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string y = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (s.LastDay == t) return "Day " + s.Count + " in a row";
            if (s.LastDay == y) return s.Count == 1 ? "Played yesterday. Play today for 2 in a row"
                                                    : s.Count + " days in a row. Play today for " + (s.Count + 1);
            return "";
        }
    }
}
