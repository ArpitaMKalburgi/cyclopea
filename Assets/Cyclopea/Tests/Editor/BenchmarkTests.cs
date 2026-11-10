using System.IO;
using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    /// <summary>
    /// The Depth Score against players whose true answer is known. Writes Tools/benchmark.md, the
    /// table the submission quotes, and fails if any of the errors that matter most creeps up.
    /// </summary>
    public class BenchmarkTests
    {
        [Test]
        public void TheDepthScoreIsRightAndSaysWhenItIsNotSure()
        {
            var r = MeasurementBenchmark.Run(15, 20261110, MeasurementBenchmark.DefaultGroups(1000));
            string md = r.Markdown();
            try { File.WriteAllText(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Tools/benchmark.md")), md); }
            catch (IOException) { }

            var fnd = r.FalseNoDepth();
            Assert.That(fnd.bad, Is.LessThanOrEqualTo(fnd.n / 200), "players with depth told they have none:\n" + md);
            Assert.That(r.FakeNumbers().bad, Is.EqualTo(0), "a number for a player with no two-eye depth:\n" + md);

            var broken = r["Broken two-eye view"];
            Assert.That(broken.NoDepth + broken.Measured + broken.AtFloor, Is.EqualTo(0), "a broken view blamed on the eyes:\n" + md);
            var off = r["Broken view, no health check"];
            Assert.That(off.NoDepth, Is.GreaterThan(off.G.N * 9 / 10), "the counterfactual should show why the health check exists");

            var steady = r["Steady players"];
            Assert.That(steady.Pct(steady.Right), Is.GreaterThanOrEqualTo(97), md);
            Assert.That(steady.Pct(steady.Uncertain), Is.LessThanOrEqualTo(3), md);
            Assert.That(100.0 * steady.Within158 / steady.Measured, Is.GreaterThanOrEqualTo(98), md);
            Assert.That(100.0 * steady.Covered / steady.Measured, Is.GreaterThanOrEqualTo(88), "the ± range must hold the truth: " + md);

            Assert.That(r["Finer than the display"].Pct(r["Finer than the display"].AtFloor), Is.GreaterThanOrEqualTo(97), md);
            Assert.That(r["Stutter in 1 of 4 rounds"].Pct(r["Stutter in 1 of 4 rounds"].Right), Is.GreaterThanOrEqualTo(97), md);

            int flat = 0, scored = 0;
            foreach (var row in r.Rows) if (!row.G.GuessOnly && row.G.GateOn) { flat += row.Flattering; scored += row.Measured; }
            Assert.That(flat, Is.LessThanOrEqualTo(scored / 200), "scores finer than the player's eyes: " + md);
        }

        [Test]
        public void TheGateIgnoresDisturbedRoundsAndGivesUpOnlyWhenTheFaultStays()
        {
            var g = new TrialGate();
            for (int i = 0; i < 30; i++) Assert.AreEqual(i % 4 != 0, g.Pass(i % 4 != 0, "stutter"));
            Assert.False(g.GaveUp, "one disturbed round in four is not a reason to give up");
            for (int i = 0; i < TrialGate.GiveUpAfterDirty; i++) g.Pass(false, "the two eyes got the same picture");
            Assert.True(g.GaveUp);
            Assert.AreEqual("the two eyes got the same picture", g.LastReason);
        }

        [Test]
        public void ACriticalFaultTaintsOnlyTheRoundsItTouched()
        {
            Health.Reset();
            Health.Ok(Health.View, "63 mm", 0);
            Assert.False(Health.CriticalFaultSince(0));
            Health.Set(Health.Frames, Health.Level.Fault, "41 % slow frames", 5, "The view stuttered.");
            Health.Ok(Health.Frames, "90 Hz", 6);
            Assert.True(Health.CriticalFaultSince(4), "a round that started before the stutter was touched by it");
            Assert.False(Health.CriticalFaultSince(7), "a round that started after it was not");
            Health.Set(Health.Room, Health.Level.Fallback, "no scan", 8, "No room scan: using the surface in front of you.");
            Assert.False(Health.CriticalFaultSince(7), "a fallback never taints a round");
            Assert.AreEqual("No room scan: using the surface in front of you.", Health.PlayerLine());
            StringAssert.Contains("Room fallback (no scan)", Health.Summary());
            Health.Reset();
        }

        [Test]
        public void TheFitFindsAKnownThresholdAndLapseRate()
        {
            // 600 trials spread across levels from a player with a known function.
            var obs = new SimulatedObserver(80, 0.15, 0.25, 0.04, seed: 3);
            var lv = new System.Collections.Generic.List<double>(); var co = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < 600; i++) { double x = DisparityMath.Pow10(1.3 + 1.7 * (i % 30) / 29.0); lv.Add(x); co.Add(obs.Respond(x)); }
            var f = ThresholdFit.Fit(lv, co, 0.25, 15, 3840);
            double truth = obs.ArcsecAtProbability(0.794);
            Assert.True(f.Ok);
            Assert.That(System.Math.Abs(DisparityMath.Log10(f.Arcsec / truth)), Is.LessThan(0.06), $"fit {f.Arcsec:F0} vs {truth:F0}");
            // One run's 95 % interval misses the truth now and then by design; coverage is the benchmark's job.
            Assert.That(f.LowArcsec, Is.LessThan(f.Arcsec)); Assert.That(f.HighArcsec, Is.GreaterThan(f.Arcsec));
            Assert.That(f.HighArcsec / f.LowArcsec, Is.LessThan(1.6), "600 trials pin it down");
            Assert.That(f.Lapse, Is.LessThanOrEqualTo(0.08));
        }

        [Test]
        public void AScoreComesWithItsRangeAndNotSureNeverCounts()
        {
            var obs = new SimulatedObserver(60, seed: 11);
            var ladder = new SkillLadder(15, new[] { SkillLadder.SpotRung(15) });
            while (!ladder.IsFinished) ladder.Respond(obs.Respond(ladder.CurrentArcsec));
            var v = Verdict.Judge(ladder, new TrialGate());
            Assert.AreEqual(Verdict.Outcome.Measured, v.Result);
            Assert.True(v.IsScore);
            StringAssert.StartsWith(" (±", v.RangeText());

            var gate = new TrialGate();
            for (int i = 0; i < TrialGate.GiveUpAfterDirty; i++) gate.Pass(false, "head tracking was lost");
            var u = Verdict.Judge(new SkillLadder(15, new[] { SkillLadder.SpotRung(15) }), gate);
            Assert.AreEqual(Verdict.Outcome.Uncertain, u.Result);
            Assert.False(u.IsScore);
            StringAssert.Contains("head tracking was lost", u.Reason);
            Assert.AreEqual("", u.RangeText());
        }
    }
}
