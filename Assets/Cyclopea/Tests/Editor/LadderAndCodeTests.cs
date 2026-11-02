using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    public class LadderAndCodeTests
    {
        [Test]
        public void LadderClimbsAllRungsAndBuildsProfile()
        {
            var ladder = new SkillLadder(15);
            var observer = new SimulatedObserver(60, seed: 3);
            int guard = 0;
            while (!ladder.IsFinished && guard++ < 2000)
                ladder.Respond(observer.Respond(ladder.CurrentArcsec));
            var p = ladder.BuildProfile();
            Assert.True(p.StereoMeasured);
            Assert.That(p.ThresholdArcsec.Count, Is.EqualTo(4));
            Assert.That(p.ThresholdArcsec[SkillLadder.Rung.Catch], Is.GreaterThanOrEqualTo(480 * 0.99));
        }

        [Test]
        public void StereoblindPlayerIsDetectedHonestly()
        {
            var ladder = new SkillLadder(15);
            // Guess-only player: 25 % correct regardless of level.
            var rng = new System.Random(5);
            int guard = 0;
            while (!ladder.IsFinished && guard++ < 2000)
                ladder.Respond(rng.NextDouble() < 0.25);
            var p = ladder.BuildProfile();
            Assert.False(p.StereoMeasured, "A chance-level player must not receive a stereo number");
        }

        [TestCase("HELLO", 4, (ushort)1234)]
        [TestCase("I LOVE YOU", 0, (ushort)65535)]
        [TestCase("WATER?", 15, (ushort)0)]
        public void SecretCodeRoundTrips(string text, int disp, ushort seed)
        {
            string code = SecretCode.Encode(text, disp, seed);
            Assert.That(code, Does.StartWith("CYC-"));
            Assert.True(SecretCode.TryDecode(code, out var s), code);
            Assert.That(s.Text, Is.EqualTo(text));
            Assert.That(s.DisparityIndex, Is.EqualTo(disp));
            Assert.That(s.Seed, Is.EqualTo(seed));
        }

        [Test]
        public void SecretCodeRejectsCorruption()
        {
            string code = SecretCode.Encode("SECRET", 6, 42);
            char[] c = code.ToCharArray();
            c[6] = c[6] == 'A' ? 'B' : 'A';
            Assert.False(SecretCode.TryDecode(new string(c), out _));
            Assert.False(SecretCode.TryDecode("CYC-NOPE", out _));
        }

        [Test]
        public void PatchesStayInsideReachAndSurface()
        {
            var surface = new PatchLayout.Rect { X = -0.6, Y = 0, W = 1.2, H = 0.8 };
            var origin = new PatchLayout.Point(0, 0.1);
            var pts = PatchLayout.Place(surface, origin, reach: 0.6, count: 4, size: 0.12, gap: 0.03, seed: 7);
            Assert.That(pts.Count, Is.EqualTo(4));
            foreach (var p in pts)
            {
                Assert.That(p.X, Is.InRange(surface.X + 0.06, surface.X + surface.W - 0.06));
                Assert.That(p.Y, Is.InRange(surface.Y + 0.06, surface.Y + surface.H - 0.06));
                Assert.That(System.Math.Sqrt(p.X * p.X + (p.Y - 0.1) * (p.Y - 0.1)), Is.LessThanOrEqualTo(0.6));
            }
            Assert.That(PatchLayout.FitSize(surface, origin, 0.6, 4, 0.03, 0.2), Is.GreaterThan(0.1));
        }
    }
}
