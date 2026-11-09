using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cyclopea.Core
{
    /// <summary>
    /// The creatures that live in the player's room: one resident per kind found (16 at most), each
    /// with its shape (so it looks the same every day) and where it lives: a scene anchor's id plus a
    /// position in that anchor's frame, or "desk" (a slot beside the squares) when there is no room scan.
    /// The room itself shows the collection growing between sessions.
    /// </summary>
    public sealed class Colony
    {
        public const string Desk = "desk";
        public const int Max = Creatures.Kinds;

        public sealed class Resident
        {
            public Creatures.Species Species;
            public Creatures.Rarity Rarity;
            public double Radius, Wobble, Lobes, Seed;
            /// <summary>Scene anchor id, or <see cref="Desk"/>.</summary>
            public string Anchor = Desk;
            /// <summary>Position in the anchor's frame (metres). Desk: X is the slot index.</summary>
            public double X, Y, Z;
            /// <summary>Local date it moved in (yyyy-MM-dd).</summary>
            public string Day = "";

            public bool OnDesk => Anchor == Desk || string.IsNullOrEmpty(Anchor);
        }

        public readonly List<Resident> Residents = new List<Resident>();

        public int Count => Residents.Count;

        public bool Has(Creatures.Species s, Creatures.Rarity r) => Residents.Exists(x => x.Species == s && x.Rarity == r);

        /// <summary>Adds a resident unless its kind already lives here or the room is full.</summary>
        public bool Add(Resident r)
        {
            if (r == null || Has(r.Species, r.Rarity) || Residents.Count >= Max) return false;
            Residents.Add(r);
            return true;
        }

        /// <summary>Next free desk slot (0, 1, 2…).</summary>
        public int NextDeskSlot()
        {
            int slot = 0;
            while (Residents.Exists(x => x.OnDesk && (int)Math.Round(x.X) == slot)) slot++;
            return slot;
        }

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static bool TryD(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var r in Residents)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append((int)r.Species).Append(',').Append((int)r.Rarity).Append(',')
                  .Append(F(r.Radius)).Append(',').Append(F(r.Wobble)).Append(',').Append(F(r.Lobes)).Append(',').Append(F(r.Seed)).Append(',')
                  .Append(Clean(r.Anchor)).Append(',').Append(F(r.X)).Append(',').Append(F(r.Y)).Append(',').Append(F(r.Z)).Append(',')
                  .Append(Clean(r.Day));
            }
            return sb.ToString();
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", "").Replace("|", "");

        /// <summary>Reads a saved colony; damaged entries are skipped, never thrown.</summary>
        public static Colony Parse(string s)
        {
            var c = new Colony();
            if (string.IsNullOrEmpty(s)) return c;
            foreach (var entry in s.Split('|'))
            {
                var f = entry.Split(',');
                if (f.Length < 11) continue;
                if (!int.TryParse(f[0], out int sp) || sp < 0 || sp >= Creatures.SpeciesCount) continue;
                if (!int.TryParse(f[1], out int ra) || ra < 0 || ra >= Creatures.RarityCount) continue;
                if (!TryD(f[2], out double radius) || !TryD(f[3], out double wobble) || !TryD(f[4], out double lobes) ||
                    !TryD(f[5], out double seed) || !TryD(f[7], out double x) || !TryD(f[8], out double y) || !TryD(f[9], out double z))
                    continue;
                if (radius <= 0 || radius > 0.2) continue;
                c.Add(new Resident
                {
                    Species = (Creatures.Species)sp, Rarity = (Creatures.Rarity)ra,
                    Radius = radius, Wobble = wobble, Lobes = lobes, Seed = seed,
                    Anchor = string.IsNullOrEmpty(f[6]) ? Desk : f[6], X = x, Y = y, Z = z, Day = f[10]
                });
            }
            return c;
        }

        /// <summary>Home card line: how many live in the room ("" when none yet).</summary>
        public static string HomeLine(Colony c)
        {
            if (c == null || c.Count == 0) return "";
            return c.Count == 1 ? "1 creature lives in your room. Look around." : c.Count + " creatures live in your room. Look around.";
        }
    }
}
