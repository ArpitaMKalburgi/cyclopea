using Cyclopea.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cyclopea.Tests
{
    public class CreatureTests
    {
        [Test]
        public void SpeciesComesFromTheOutlinesLobes()
        {
            Assert.That(Creatures.FromLobes(3), Is.EqualTo(Creatures.Species.Trifoil));
            Assert.That(Creatures.FromLobes(4), Is.EqualTo(Creatures.Species.Clover));
            Assert.That(Creatures.FromLobes(5), Is.EqualTo(Creatures.Species.Starfin));
            Assert.That(Creatures.FromLobes(6), Is.EqualTo(Creatures.Species.Sixling));
            for (int l = 3; l <= 6; l++) Assert.That(Creatures.Lobes(Creatures.FromLobes(l)), Is.EqualTo(l));
            Assert.That(Creatures.FromLobes(2), Is.EqualTo(Creatures.Species.Trifoil), "clamped");
            Assert.That(Creatures.FromLobes(9), Is.EqualTo(Creatures.Species.Sixling), "clamped");
        }

        [Test]
        public void RarityFollowsHowFineTheDepthWas()
        {
            Assert.That(Creatures.FromArcsec(1920), Is.EqualTo(Creatures.Rarity.Common));
            Assert.That(Creatures.FromArcsec(480), Is.EqualTo(Creatures.Rarity.Common));
            Assert.That(Creatures.FromArcsec(479), Is.EqualTo(Creatures.Rarity.Uncommon));
            Assert.That(Creatures.FromArcsec(120), Is.EqualTo(Creatures.Rarity.Uncommon));
            Assert.That(Creatures.FromArcsec(119), Is.EqualTo(Creatures.Rarity.Rare));
            Assert.That(Creatures.FromArcsec(40), Is.EqualTo(Creatures.Rarity.Rare));
            Assert.That(Creatures.FromArcsec(39), Is.EqualTo(Creatures.Rarity.Elusive));
            Assert.That(Creatures.FromArcsec(15), Is.EqualTo(Creatures.Rarity.Elusive));
        }

        [Test]
        public void CollectionCountsFirstsAndSurvivesASave()
        {
            var c = new CreatureCollection();
            Assert.That(c.Best, Is.Null);
            Assert.True(c.Add(Creatures.Species.Starfin, Creatures.Rarity.Rare), "first of its kind");
            Assert.False(c.Add(Creatures.Species.Starfin, Creatures.Rarity.Rare), "second is not new");
            Assert.True(c.Add(Creatures.Species.Clover, Creatures.Rarity.Common));
            Assert.That(c.Total, Is.EqualTo(3));
            Assert.That(c.KindsFound, Is.EqualTo(2));
            Assert.That(c.Best, Is.EqualTo(Creatures.Rarity.Rare));

            var back = CreatureCollection.Parse(c.Serialize());
            Assert.That(back.Count(Creatures.Species.Starfin, Creatures.Rarity.Rare), Is.EqualTo(2));
            Assert.That(back.Count(Creatures.Species.Clover, Creatures.Rarity.Common), Is.EqualTo(1));
            Assert.That(back.Total, Is.EqualTo(3));
        }

        [Test]
        public void BadSaveNeverBlocksPlay()
        {
            Assert.That(CreatureCollection.Parse(null).Total, Is.EqualTo(0));
            Assert.That(CreatureCollection.Parse("").Total, Is.EqualTo(0));
            var c = CreatureCollection.Parse("x,,-3,4");
            Assert.That(c.Total, Is.EqualTo(4));
            Assert.That(c.Count(Creatures.Species.Trifoil, Creatures.Rarity.Elusive), Is.EqualTo(4));
            Assert.That(CreatureCollection.Parse(new string('1', 200)).Total, Is.EqualTo(0), "overflowing numbers count as zero");
        }

        [Test]
        public void ScoreCardLineReadsWell()
        {
            var c = new CreatureCollection();
            c.Add(Creatures.Species.Starfin, Creatures.Rarity.Rare);
            Assert.That(CreatureCollection.SessionLine(0, c, null), Is.EqualTo(""));
            Assert.That(CreatureCollection.SessionLine(1, c, new[] { "Rare Starfin" }),
                Is.EqualTo("You found 1 creature. Field guide: 1 of 16.\nNew: Rare Starfin"));
            Assert.That(CreatureCollection.SessionLine(5, c, new[] { "A", "B", "C" }),
                Is.EqualTo("You found 5 creatures. Field guide: 1 of 16.\nNew: C and 2 more"));
        }

        [Test]
        public void BodyFootprintIsExactlyTheOutlineSeenInDepth()
        {
            const float r = 0.0324f, wobble = 0.15f, lobes = 5f, seed = 123.4f;
            var mesh = CreatureBody.BuildMesh(r, wobble, lobes, seed, 48, 6);
            try
            {
                var v = mesh.vertices;
                for (int i = 0; i < 48; i++)
                {
                    float a = Mathf.Atan2(v[i].z, v[i].x);
                    float rad = new Vector2(v[i].x, v[i].z).magnitude;
                    float expected = (float)Creatures.OutlineRadius(r, wobble, lobes, seed, a);
                    Assert.That(rad, Is.EqualTo(expected).Within(2e-5f), "base vertex " + i);
                    Assert.That(v[i].y, Is.EqualTo(0f), "base sits on the surface");
                }
                Assert.That(mesh.vertexCount, Is.LessThan(2000), "cheap enough to keep a nest of them at 90 Hz");
                foreach (var n in mesh.normals) Assert.That(n.sqrMagnitude, Is.EqualTo(1f).Within(1e-3f));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
