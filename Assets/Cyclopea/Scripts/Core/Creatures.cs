using System;
using System.Collections.Generic;

namespace Cyclopea.Core
{
    /// <summary>
    /// The creatures hiding in the static. The outline a player sees in depth is the creature's body:
    /// its number of lobes is its species, and how fine the disparity was when it was found is its
    /// rarity. Finding rarer creatures means seeing finer depth, so the collection grows with the skill.
    /// </summary>
    public static class Creatures
    {
        public enum Species { Trifoil, Clover, Starfin, Sixling }
        public enum Rarity { Common, Uncommon, Rare, Elusive }

        public const int SpeciesCount = 4, RarityCount = 4, Kinds = SpeciesCount * RarityCount;

        /// <summary>Lobes 3..6 (the stereogram mask's lobe count) to species.</summary>
        public static Species FromLobes(double lobes) => (Species)Math.Max(0, Math.Min(3, (int)Math.Round(lobes) - 3));

        public static int Lobes(Species s) => (int)s + 3;

        /// <summary>Rarity from the disparity (arcseconds) at which the creature was found.</summary>
        public static Rarity FromArcsec(double arcsec) =>
            arcsec >= 480 ? Rarity.Common : arcsec >= 120 ? Rarity.Uncommon : arcsec >= 40 ? Rarity.Rare : Rarity.Elusive;

        public static string Name(Species s, Rarity r) => r + " " + s;

        /// <summary>
        /// The creature's outline, identical to the stereogram shader's mask:
        /// r(θ) = R · (1 + wobble · sin(lobes · θ + seed)), θ measured from the patch's +u toward +v.
        /// </summary>
        public static double OutlineRadius(double radius, double wobble, double lobes, double seed, double angle) =>
            radius * (1.0 + wobble * Math.Sin(lobes * angle + seed));
    }

    /// <summary>Which kinds the player has found and how many of each. Persists as a short CSV string.</summary>
    public sealed class CreatureCollection
    {
        readonly int[] counts = new int[Creatures.Kinds];

        static int Slot(Creatures.Species s, Creatures.Rarity r) => (int)s * Creatures.RarityCount + (int)r;

        public int Count(Creatures.Species s, Creatures.Rarity r) => counts[Slot(s, r)];

        /// <summary>Adds one; true when it is the first of its kind.</summary>
        public bool Add(Creatures.Species s, Creatures.Rarity r) => counts[Slot(s, r)]++ == 0;

        public int Total { get { int t = 0; foreach (var c in counts) t += c; return t; } }

        public int KindsFound { get { int k = 0; foreach (var c in counts) if (c > 0) k++; return k; } }

        /// <summary>The rarest tier found so far, or null if nothing yet.</summary>
        public Creatures.Rarity? Best
        {
            get
            {
                for (int r = Creatures.RarityCount - 1; r >= 0; r--)
                    for (int s = 0; s < Creatures.SpeciesCount; s++)
                        if (counts[s * Creatures.RarityCount + r] > 0) return (Creatures.Rarity)r;
                return null;
            }
        }

        public string Serialize() => string.Join(",", counts);

        /// <summary>Tolerant: anything unreadable counts as zero, so a bad save never blocks play.</summary>
        public static CreatureCollection Parse(string s)
        {
            var c = new CreatureCollection();
            if (string.IsNullOrEmpty(s)) return c;
            var parts = s.Split(',');
            for (int i = 0; i < parts.Length && i < c.counts.Length; i++)
                if (int.TryParse(parts[i], out int v) && v > 0) c.counts[i] = v;
            return c;
        }

        /// <summary>One line for the score card.</summary>
        public static string SessionLine(int foundThisSession, CreatureCollection all, IList<string> newKinds)
        {
            if (foundThisSession <= 0) return "";
            string line = "You found " + foundThisSession + (foundThisSession == 1 ? " creature" : " creatures") +
                          ". Field guide: " + all.KindsFound + " of " + Creatures.Kinds + ".";
            if (newKinds != null && newKinds.Count > 0)
                line += "\nNew: " + (newKinds.Count <= 2 ? string.Join(", ", newKinds) : newKinds[newKinds.Count - 1] + " and " + (newKinds.Count - 1) + " more");
            return line;
        }
    }
}
