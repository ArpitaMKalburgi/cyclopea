using System;
using System.Threading.Tasks;
using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    /// <summary>Frame-time percentiles, the error tally and the round record: what the play log is made of.</summary>
    public class ObservabilityTests
    {
        [Test]
        public void AStepLineCarriesTheSecondsSinceLaunch()
        {
            var log = new SessionLog();
            log.AddStep("s1", new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc), "first_catch", 41.26, "slap");
            string line = log.Lines[0];
            StringAssert.Contains("\"type\":\"step\"", line);
            StringAssert.Contains("\"step\":\"first_catch\"", line);
            StringAssert.Contains("\"t\":41.3", line);
            StringAssert.Contains("\"detail\":\"slap\"", line);
        }

        [Test]
        public void FramePercentilesMatchAKnownMix()
        {
            var f = new FrameStats();
            f.Reset(90);
            // 990 frames on time (11.1 ms), 10 dropped frames (25 ms).
            for (int i = 0; i < 990; i++) f.Add(0.0111);
            for (int i = 0; i < 10; i++) f.Add(0.025);
            Assert.AreEqual(1000, f.Count);
            Assert.That(f.Percentile(0.5), Is.EqualTo(11.25).Within(0.25), "p50 sits in the 11.1 ms bin");
            Assert.That(f.Percentile(0.99), Is.EqualTo(11.25).Within(0.25), "99 % of frames were on time");
            Assert.That(f.Percentile(0.995), Is.EqualTo(25.0).Within(0.25), "the slowest half percent were the drops");
            Assert.AreEqual(10, f.Late, "a 25 ms frame at 90 Hz is more than 1.5 budgets: late");
            Assert.That(f.LatePercent, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(f.MaxMs, Is.EqualTo(25.0).Within(1e-6));
            Assert.That(f.MeanFps, Is.EqualTo(1000.0 / (0.99 * 11.1 + 0.01 * 25)).Within(0.01));
            StringAssert.Contains("at 90 Hz", f.Line());
        }

        [Test]
        public void FrameStatsStayTheSameSizeHoweverLongThePlay()
        {
            var f = new FrameStats();
            f.Reset(72);
            int cap = f.Capacity;
            var rng = new Random(3);
            // Ten hours at 72 Hz, with the odd hitch and one huge stall.
            for (int i = 0; i < 72 * 3600 * 10; i++) f.Add(0.0139 + (rng.NextDouble() < 0.002 ? 0.02 : 0));
            f.Add(2.5);
            Assert.AreEqual(cap, f.Capacity, "a fixed histogram: no growth with play time");
            Assert.That(f.MaxMs, Is.EqualTo(2500).Within(1e-6), "a stall past the last bin is still the max");
            Assert.That(f.Percentile(1.0), Is.EqualTo(2500).Within(1e-6));
            Assert.That(f.Percentile(0.5), Is.EqualTo(14.0).Within(0.25));
            f.Reset(90);
            Assert.AreEqual(0, f.Count);
            Assert.True(double.IsNaN(f.Percentile(0.5)));
            Assert.AreEqual("no frames", f.Line());
        }

        [Test]
        public void ErrorsAreCountedOnceEachAndCapped()
        {
            var t = new ErrorTally();
            var now = DateTime.UtcNow;
            Assert.True(t.Record("Exception", "NullReferenceException: x", "Cyclopea.WhackRound.Update () (at WhackRound.cs:10)\nUnityEngine.X", now));
            Assert.False(t.Record("Exception", "NullReferenceException: x", "", now), "a repeat is counted, not kept again");
            Assert.True(t.Record("Error", "Shader error", "UnityEngine.Shader.Find\nCyclopea.StereoPatch.Awake ()", now));
            var fresh = t.TakeNew();
            Assert.AreEqual(2, fresh.Count);
            Assert.AreEqual(2, fresh[0].Count);
            StringAssert.StartsWith("Cyclopea.WhackRound.Update", fresh[0].Where);
            StringAssert.StartsWith("Cyclopea.StereoPatch.Awake", fresh[1].Where, "where = the first frame in our own code");
            Assert.AreEqual(0, t.TakeNew().Count, "taken once");
            Assert.AreEqual(3, t.Total);
            Assert.AreEqual(2, t.Exceptions);

            // A flood of different errors: only MaxDistinct kinds kept, every one still counted.
            for (int i = 0; i < 5000; i++) t.Record("Error", "flood " + i, "", now);
            Assert.AreEqual(ErrorTally.MaxDistinct, t.Distinct);
            Assert.AreEqual(5003, t.Total);
            Assert.AreEqual(5000 - (ErrorTally.MaxDistinct - 2), t.Overflow);
            Assert.AreEqual(ErrorTally.MaxDistinct - 2, t.TakeNew().Count);
        }

        [Test]
        public void ErrorsFromManyThreadsAreAllCounted()
        {
            var t = new ErrorTally();
            Parallel.For(0, 8, k => { for (int i = 0; i < 1000; i++) t.Record("Error", "thread " + (i % 3), "", DateTime.UtcNow); });
            Assert.AreEqual(8000, t.Total);
            Assert.AreEqual(3, t.Distinct);
        }

        [Test]
        public void ARoundIsOneJsonLineWithItsFrameTimes()
        {
            var f = new FrameStats();
            f.Reset(90);
            for (int i = 0; i < 100; i++) f.Add(0.0111);
            var log = new SessionLog();
            log.Add(new SessionLog.Round
            {
                SessionId = "20261110-1-play", SubjectId = "P01", Device = "Quest3 \"dev\"", UtcStart = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc),
                Index = 1, Ended = "clock", Seconds = 60, Score = 2350, Caught = 14, Escaped = 3, Whiffs = 2, BestCombo = 7,
                FrenzyCaught = 4, Golden = 1, FinestArcsec = 120, FinestMm = 2.13, LevelArcsec = 140, ViewingDistanceM = 0.55,
                Beginner = 0.8, Inputs = "slap:15,pinch:1", Surface = "table", FastHands = true, Frames = f
            });
            log.AddError("20261110-1-play", DateTime.UtcNow, "Exception", "Null \"ref\"\nline", "Cyclopea.X", 1);
            log.AddApp("20261110-1-play", DateTime.UtcNow, "Quest3", "1.0", "release", 90);
            Assert.AreEqual(3, log.Lines.Count);
            string r = log.Lines[0];
            StringAssert.StartsWith("{\"type\":\"round\"", r);
            foreach (var part in new[] { "\"score\":2350", "\"caught\":14", "\"frenzy_caught\":4", "\"golden\":1", "\"ended\":\"clock\"",
                                         "\"inputs\":\"slap:15,pinch:1\"", "\"fast_hands\":true", "\"clean\":true", "\"hz\":90", "\"ms_p99\":11.1",
                                         "\"late_pct\":0", "\"device\":\"Quest3 \\\"dev\\\"\"" })
                StringAssert.Contains(part, r);
            Assert.False(r.Contains("\n"));
            StringAssert.Contains("\"message\":\"Null \\\"ref\\\"\\nline\"", log.Lines[1], "messages are escaped onto one line");
            StringAssert.Contains("\"build\":\"release\"", log.Lines[2]);
        }
    }
}
