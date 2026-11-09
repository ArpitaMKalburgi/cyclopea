using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    public class ColonyTests
    {
        static Colony.Resident R(Creatures.Species s, Creatures.Rarity r, string anchor = Colony.Desk, double x = 0) =>
            new Colony.Resident { Species = s, Rarity = r, Radius = 0.021, Wobble = 0.15, Lobes = Creatures.Lobes(s), Seed = 12.5,
                                  Anchor = anchor, X = x, Y = -0.25, Z = 1.5e-3, Day = "2026-11-09" };

        [Test]
        public void OneResidentPerKindAndSixteenAtMost()
        {
            var c = new Colony();
            Assert.True(c.Add(R(Creatures.Species.Clover, Creatures.Rarity.Rare)));
            Assert.False(c.Add(R(Creatures.Species.Clover, Creatures.Rarity.Rare)), "a kind moves in once");
            for (int s = 0; s < Creatures.SpeciesCount; s++)
                for (int r = 0; r < Creatures.RarityCount; r++)
                    c.Add(R((Creatures.Species)s, (Creatures.Rarity)r));
            Assert.That(c.Count, Is.EqualTo(Creatures.Kinds));
            Assert.That(c.Count, Is.EqualTo(Colony.Max));
        }

        [Test]
        public void SavedColonyReadsBackExactly()
        {
            var c = new Colony();
            c.Add(R(Creatures.Species.Trifoil, Creatures.Rarity.Common));
            c.Add(R(Creatures.Species.Sixling, Creatures.Rarity.Elusive, "0a1b2c3d4e5f60718293a4b5c6d7e8f9", 0.731));
            var back = Colony.Parse(c.Serialize());
            Assert.That(back.Count, Is.EqualTo(2));
            var e = back.Residents[1];
            Assert.That(e.Species, Is.EqualTo(Creatures.Species.Sixling));
            Assert.That(e.Rarity, Is.EqualTo(Creatures.Rarity.Elusive));
            Assert.That(e.Anchor, Is.EqualTo("0a1b2c3d4e5f60718293a4b5c6d7e8f9"));
            Assert.That(e.X, Is.EqualTo(0.731));
            Assert.That(e.Y, Is.EqualTo(-0.25));
            Assert.That(e.Z, Is.EqualTo(1.5e-3));
            Assert.That(e.Seed, Is.EqualTo(12.5));
            Assert.That(e.Day, Is.EqualTo("2026-11-09"));
            Assert.False(e.OnDesk);
            Assert.True(back.Residents[0].OnDesk);
        }

        [Test]
        public void DamagedSavesNeverThrow()
        {
            Assert.That(Colony.Parse(null).Count, Is.EqualTo(0));
            Assert.That(Colony.Parse("").Count, Is.EqualTo(0));
            Assert.That(Colony.Parse("garbage|1,2|9,9,0.02,0,4,1,desk,0,0,0,x").Count, Is.EqualTo(0), "bad species skipped");
            Assert.That(Colony.Parse("1,1,NaN,0,4,1,desk,0,0,0,x").Count, Is.EqualTo(0), "bad numbers skipped");
            var good = new Colony();
            good.Add(R(Creatures.Species.Starfin, Creatures.Rarity.Uncommon));
            Assert.That(Colony.Parse("junk|" + good.Serialize()).Count, Is.EqualTo(1), "good entries survive a bad one");
        }

        [Test]
        public void DeskSlotsFillTheFirstGap()
        {
            var c = new Colony();
            Assert.That(c.NextDeskSlot(), Is.EqualTo(0));
            c.Add(R(Creatures.Species.Trifoil, Creatures.Rarity.Common, Colony.Desk, 0));
            c.Add(R(Creatures.Species.Clover, Creatures.Rarity.Common, Colony.Desk, 2));
            c.Add(R(Creatures.Species.Starfin, Creatures.Rarity.Common, "abc", 1));   // a room resident does not hold slot 1
            Assert.That(c.NextDeskSlot(), Is.EqualTo(1));
        }

        [Test]
        public void HomeCardSaysHowManyLiveInYourRoom()
        {
            var c = new Colony();
            Assert.That(Colony.HomeLine(c), Is.EqualTo(""));
            c.Add(R(Creatures.Species.Trifoil, Creatures.Rarity.Common));
            StringAssert.StartsWith("1 creature lives in your room", Colony.HomeLine(c));
            c.Add(R(Creatures.Species.Clover, Creatures.Rarity.Common));
            StringAssert.StartsWith("2 creatures live in your room", Colony.HomeLine(c));
        }
    }
}
