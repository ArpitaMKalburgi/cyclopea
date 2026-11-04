using System;
using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    /// <summary>Catch rung geometry, the touch judgement, the hold-still detector, and the ladder/log plumbing for Sort and Catch.</summary>
    public class ReachTests
    {
        const double Ipd = 0.063;

        [Test]
        public void HeightAndShiftAreInverses()
        {
            foreach (double eyeH in new[] { 0.35, 0.5, 0.7 })
                foreach (double h in new[] { 0.005, 0.02, 0.05, 0.1 })
                {
                    double s = Reach.ShiftForHeight(h, eyeH, Ipd);
                    Assert.That(Reach.HeightForShift(s, eyeH, Ipd), Is.EqualTo(h).Within(1e-12));
                }
            Assert.That(Reach.ShiftForHeight(0, 0.5, Ipd), Is.EqualTo(0));
            Assert.That(Reach.ShiftForHeight(1.0, 0.5, Ipd), Is.LessThan(1.0), "never past the eyes");
        }

        /// <summary>
        /// Independent check by ray tracing: eyes above an oblique surface, each eye's ray through its
        /// shifted copy of the outline centre. Where the two rays cross is where the creature is seen; its
        /// height above the surface and its distance along the sightline must match the closed forms.
        /// </summary>
        [Test]
        public void TheTwoLinesOfSightCrossAtTheClosedFormPoint()
        {
            foreach (double eyeH in new[] { 0.3, 0.45, 0.6 })
                foreach (double D in new[] { 0.0, 0.25, 0.5 })      // how far back the head is, along the surface
                    foreach (double s in new[] { 0.0005, 0.003, 0.01 })
                    {
                        // Surface = plane y = 0; outline centre at the origin; eyes at (±e/2, eyeH, D); shift along x.
                        var L = new[] { -Ipd / 2, eyeH, D };
                        var R = new[] { Ipd / 2, eyeH, D };
                        var tl = new[] { s / 2, 0.0, 0.0 };   // what the left eye sees at the centre
                        var tr = new[] { -s / 2, 0.0, 0.0 };
                        // Rays L + a (tl - L) and R + b (tr - R) meet where x agrees (y and z agree for a = b by symmetry).
                        double a = (R[0] - L[0]) / ((tl[0] - L[0]) - (tr[0] - R[0]));
                        var P = new[] { L[0] + a * (tl[0] - L[0]), L[1] + a * (tl[1] - L[1]), L[2] + a * (tl[2] - L[2]) };
                        double viaR = R[0] + a * (tr[0] - R[0]);
                        Assert.That(P[0], Is.EqualTo(viaR).Within(1e-12), "the rays really cross");
                        Assert.That(P[1], Is.EqualTo(Reach.HeightForShift(s, eyeH, Ipd)).Within(1e-12), "height above the surface");
                        double z = Math.Sqrt(eyeH * eyeH + D * D);
                        double along = Math.Sqrt(P[0] * P[0] + P[1] * P[1] + P[2] * P[2]);
                        Assert.That(along, Is.EqualTo(Reach.SightlineDistance(s, z, Ipd)).Within(1e-12), "distance along the sightline");
                        Assert.That(P[0], Is.EqualTo(0).Within(1e-12), "on the sightline to the centre, not off to a side");
                    }
        }

        [Test]
        public void HeadOnTheHeldHeightMatchesTheSpotDisparity()
        {
            // Small heights, viewed head-on: the held height and Spot's arcsecond convention agree.
            double z = 0.6, arcsec = 480;
            double h = DisparityMath.PopOutMeters(arcsec, z, Ipd);
            Assert.That(Reach.ShiftForHeight(h, z, Ipd), Is.EqualTo(DisparityMath.ShiftMeters(arcsec, z)).Within(0.03 * DisparityMath.ShiftMeters(arcsec, z)));
        }

        [Test]
        public void TouchesAreJudgedByHeight()
        {
            Assert.That(Reach.Judge(true, 0.050, 0.053), Is.EqualTo(Reach.Outcome.Caught));
            Assert.That(Reach.Judge(true, 0.090, 0.053), Is.EqualTo(Reach.Outcome.TooHigh));
            Assert.That(Reach.Judge(true, 0.010, 0.053), Is.EqualTo(Reach.Outcome.TooLow));
            Assert.That(Reach.Judge(false, 0.053, 0.053), Is.EqualTo(Reach.Outcome.WrongSquare));
            Assert.That(Reach.Tolerance(0.005), Is.EqualTo(Reach.MinToleranceM), "never tighter than hand tracking");
            Assert.That(Reach.Tolerance(0.05), Is.EqualTo(0.02).Within(1e-12));
        }

        [Test]
        public void HoldingStillFiresOnceThroughJitterButNotWhileMoving()
        {
            var rng = new Random(3);
            var still = new StillDetector();
            double t = 0, dt = 1.0 / 90;
            int fired = 0;
            double mx = 0, my = 0;
            for (int i = 0; i < 60; i++, t += dt)   // 0.67 s held with ±3 mm jitter
            {
                if (still.Feed(t, 0.1 + (rng.NextDouble() - 0.5) * 0.006, 0.05 + (rng.NextDouble() - 0.5) * 0.006, 0.4))
                {
                    fired++;
                    mx = still.MeanX; my = still.MeanY;
                }
            }
            Assert.That(fired, Is.EqualTo(1), "one catch per hold");
            Assert.That(mx, Is.EqualTo(0.1).Within(0.002), "the mean over the hold is steadier than any sample");
            Assert.That(my, Is.EqualTo(0.05).Within(0.002));

            still.Reset();
            for (int i = 0; i < 180; i++, t += dt)   // 2 s sweeping at 5 cm/s
                Assert.False(still.Feed(t, 0.05 * i * dt, 0.05, 0.4), "a moving finger never catches");
        }

        [Test]
        public void LadderSkipsADisabledRung()
        {
            var ladder = new SkillLadder(15, new[]
            {
                new SkillLadder.RungConfig { Rung = SkillLadder.Rung.Spot, Alternatives = 4, Stair = new Staircase.Config() },
                new SkillLadder.RungConfig { Rung = SkillLadder.Rung.Sort, Alternatives = 2, Stair = new Staircase.Config() },
                new SkillLadder.RungConfig { Rung = SkillLadder.Rung.Catch, Alternatives = 1, Stair = new Staircase.Config() },
            });
            ladder.SetEnabled(SkillLadder.Rung.Sort, false);
            Assert.That(ladder.CurrentRung, Is.EqualTo(SkillLadder.Rung.Spot), "turning off a later rung leaves the current one");
            Assert.False(ladder.Has(SkillLadder.Rung.Sort));
            ladder.SetEnabled(SkillLadder.Rung.Spot, false);
            Assert.That(ladder.CurrentRung, Is.EqualTo(SkillLadder.Rung.Catch), "turning off the current rung moves past the disabled one");
            ladder.SetEnabled(SkillLadder.Rung.Catch, false);
            Assert.True(ladder.IsFinished);
        }

        [Test]
        public void LogCarriesTheRungAndTheCatchError()
        {
            var log = new SessionLog();
            log.Add(new SessionLog.Trial { SessionId = "s", Phase = "Descent" });
            log.Add(new SessionLog.Trial
            {
                SessionId = "s", Phase = "Descent", Mode = SessionLog.CatchMode.ReachToDepth, Rung = "Catch",
                HeightM = 0.05, DepthErrorM = -0.004
            });
            log.Add(new SessionLog.Summary { SessionId = "s", ThresholdArcsec = 95, Rung = "Sort" });
            StringAssert.Contains("\"rung\":\"Spot\"", log.Lines[0], "default rung is Spot");
            StringAssert.DoesNotContain("depth_err_m", log.Lines[0]);
            StringAssert.Contains("\"rung\":\"Catch\"", log.Lines[1]);
            StringAssert.Contains("\"height_m\":0.05", log.Lines[1]);
            StringAssert.Contains("\"depth_err_m\":-0.004", log.Lines[1]);
            StringAssert.Contains("\"rung\":\"Sort\"", log.Lines[2]);
        }
    }
}
