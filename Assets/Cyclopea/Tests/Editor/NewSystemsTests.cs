using System;
using System.Collections.Generic;
using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    public class CamouflageTests
    {
        static List<Camouflage.Rgb> Desk(double r, double g, double b, double spread, int seed)
        {
            var rng = new Random(seed);
            var px = new List<Camouflage.Rgb>();
            for (int i = 0; i < 400; i++)
            {
                double k = 1 + (rng.NextDouble() - 0.5) * spread;
                px.Add(new Camouflage.Rgb(Math.Min(1, r * k), Math.Min(1, g * k), Math.Min(1, b * k)));
            }
            return px;
        }

        [TestCase(0.55, 0.38, 0.22, 0.4, TestName = "Oak desk")]
        [TestCase(0.95, 0.95, 0.93, 0.05, TestName = "White desk, almost no texture")]
        [TestCase(0.05, 0.05, 0.06, 0.1, TestName = "Black desk")]
        [TestCase(0.20, 0.35, 0.60, 0.3, TestName = "Blue mat")]
        public void PaletteKeepsDotsResolvable(double r, double g, double b, double spread)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var p = Camouflage.FromPixels(Desk(r, g, b, spread, seed));
                Assert.That(p.LumaGap, Is.GreaterThanOrEqualTo(Camouflage.MinGap - 1e-3), "dots must stay visible on any desk");
                Assert.That(p.Background.Luma, Is.InRange(Camouflage.MinBackground - 1e-3, Camouflage.MaxBackground + 1e-3));
                foreach (var c in new[] { p.Background, p.Dots })
                    Assert.That(new[] { c.R, c.G, c.B }, Has.All.InRange(0.0, 1.0));
            }
        }

        [Test]
        public void PaletteFollowsTheDeskHue()
        {
            var p = Camouflage.FromPixels(Desk(0.55, 0.38, 0.22, 0.4, 1));
            Assert.That(p.Background.R, Is.GreaterThan(p.Background.B), "a warm desk gives a warm ground");
        }

        [Test]
        public void TooFewPixelsFallsBackToDefault()
        {
            var p = Camouflage.FromPixels(new List<Camouflage.Rgb>());
            Assert.That(p.Background.Luma, Is.EqualTo(Camouflage.Default.Background.Luma).Within(1e-9));
        }
    }

    public class StreakTests
    {
        [Test]
        public void CountsConsecutiveDaysAndResetsOnAGap()
        {
            var s = new Streak.State();
            s = Streak.Update(s, new DateTime(2026, 11, 4));
            Assert.That(s.Count, Is.EqualTo(1));
            s = Streak.Update(s, new DateTime(2026, 11, 4, 22, 0, 0));
            Assert.That(s.Count, Is.EqualTo(1), "same day counts once");
            s = Streak.Update(s, new DateTime(2026, 11, 5));
            s = Streak.Update(s, new DateTime(2026, 11, 6));
            Assert.That(s.Count, Is.EqualTo(3));
            s = Streak.Update(s, new DateTime(2026, 11, 9));
            Assert.That(s.Count, Is.EqualTo(1));
            Assert.That(s.Best, Is.EqualTo(3));
        }

        [Test]
        public void SurvivesMonthEnd()
        {
            var s = Streak.Update(new Streak.State(), new DateTime(2026, 11, 30));
            s = Streak.Update(s, new DateTime(2026, 12, 1));
            Assert.That(s.Count, Is.EqualTo(2));
        }
    }

    public class MicrogestureTests
    {
        [Test]
        public void SwipesWalkTheGrid()
        {
            int c = MicrogestureSelector.Move(-1, OVRHand.MicrogestureType.SwipeRight);
            Assert.That(c, Is.EqualTo(0), "first gesture lands on the first square");
            c = MicrogestureSelector.Move(c, OVRHand.MicrogestureType.SwipeRight);
            Assert.That(c, Is.EqualTo(1));
            c = MicrogestureSelector.Move(c, OVRHand.MicrogestureType.SwipeBackward);
            Assert.That(c, Is.EqualTo(3));
            c = MicrogestureSelector.Move(c, OVRHand.MicrogestureType.SwipeLeft);
            Assert.That(c, Is.EqualTo(2));
            c = MicrogestureSelector.Move(c, OVRHand.MicrogestureType.SwipeForward);
            Assert.That(c, Is.EqualTo(0));
            c = MicrogestureSelector.Move(c, OVRHand.MicrogestureType.SwipeLeft);
            Assert.That(c, Is.EqualTo(0), "edges hold");
        }
    }

    public class VoiceCommandTests
    {
        [TestCase("top left", 0)]
        [TestCase("Top right!", 1)]
        [TestCase("bottom left", 2)]
        [TestCase("the lower right one", 3)]
        [TestCase("near left", 2)]
        [TestCase("number two", 1)]
        [TestCase("three", 2)]
        [TestCase("Four.", 3)]
        [TestCase("first", 0)]
        public void SquaresAreHeard(string said, int square)
        {
            Assert.That(VoiceCommands.Parse(said).Square, Is.EqualTo(square));
        }

        [TestCase("")]
        [TestCase("hmm")]
        [TestCase("left")]
        [TestCase("one or two")]
        [TestCase("go to the store for milk")]
        [TestCase("top bottom left")]
        public void AmbiguousOrUnrelatedSpeechPicksNothing(string said)
        {
            Assert.That(VoiceCommands.Parse(said).IsEmpty, Is.True, said);
        }

        [Test]
        public void PlayAgain()
        {
            Assert.That(VoiceCommands.Parse("play again").Again, Is.True);
        }
    }

    public class MotionParallaxTests
    {
        [Test]
        public void EyeOnTheLeftSeesTheCrossedHalfDisparity()
        {
            // An eye ipd/2 to the left of a point floating at the stereo pop-out depth must see the
            // same slide that stereo mode gives that eye: +ipd/2 * h / (z - h), i.e. ~ShiftMeters/2.
            const double z = 0.6, arcsec = 60;
            double h = DisparityMath.PopOutMeters(arcsec, z);
            double slide = DisparityMath.MotionShiftMeters(-DisparityMath.DefaultIpd / 2, h, z);
            double half = DisparityMath.ShiftMeters(arcsec, z) / 2;
            Assert.That(slide, Is.GreaterThan(0), "left eye sees the near point shifted right (crossed)");
            Assert.That(slide, Is.EqualTo(half).Within(half * 0.01));
        }

        [Test]
        public void SlideGrowsWithHeadSwayAndDepthAndIsZeroWithoutEither()
        {
            Assert.That(DisparityMath.MotionShiftMeters(0.05, 0, 0.6), Is.EqualTo(0));
            Assert.That(DisparityMath.MotionShiftMeters(0, 0.02, 0.6), Is.EqualTo(0));
            double a = Math.Abs(DisparityMath.MotionShiftMeters(0.03, 0.01, 0.6));
            Assert.That(Math.Abs(DisparityMath.MotionShiftMeters(0.06, 0.01, 0.6)), Is.EqualTo(2 * a).Within(1e-12));
            Assert.That(Math.Abs(DisparityMath.MotionShiftMeters(0.03, 0.02, 0.6)), Is.GreaterThan(2 * a));
            Assert.That(double.IsInfinity(DisparityMath.MotionShiftMeters(0.03, 5, 0.6)), Is.False, "depth is clamped in front of the eye");
        }

        [Test]
        public void MotionScoreSpeaksMillimetresNotArcseconds()
        {
            string line = MotionScore.Describe(480, false, 0.6);
            StringAssert.Contains("mm step at 60 cm", line);
            StringAssert.DoesNotContain("″", line);
        }

        [Test]
        public void LogRecordsTheCue()
        {
            var log = new SessionLog();
            log.Add(new SessionLog.Trial { SessionId = "s", Phase = "Descent", Cue = SessionLog.Cue.Motion });
            log.Add(new SessionLog.Summary { SessionId = "s", ThresholdArcsec = double.NaN });
            StringAssert.Contains("\"cue\":\"Motion\"", log.Lines[0]);
            StringAssert.Contains("\"cue\":\"Stereo\"", log.Lines[1], "default cue is stereo");
        }
    }
}
