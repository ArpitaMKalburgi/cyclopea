using System;
using System.IO;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    /// <summary>Levels that never run out, and a play-log folder that never grows without bound.</summary>
    public class LevelsAndStorageTests
    {
        [Test]
        public void LevelsFollowTheKindsThenGoOnForEver()
        {
            Assert.AreEqual(1, WhackRound.Level(0));
            Assert.AreEqual(2, WhackRound.Level(WhackRound.HopperAt));
            Assert.AreEqual(3, WhackRound.Level(WhackRound.ShyAt));
            Assert.AreEqual(4, WhackRound.Level(WhackRound.GoldenAt));
            Assert.AreEqual(4, WhackRound.Level(WhackRound.GoldenAt + WhackRound.LevelEvery - 1));
            Assert.AreEqual(5, WhackRound.Level(WhackRound.GoldenAt + WhackRound.LevelEvery));
            Assert.AreEqual(54, WhackRound.Level(WhackRound.GoldenAt + 50 * WhackRound.LevelEvery), "a long-time player still has a next level");
            int last = 0;
            for (int total = 0; total < 5000; total++)
            {
                int level = WhackRound.Level(total);
                Assert.GreaterOrEqual(level, last, "levels never go down");
                Assert.Greater(WhackRound.NextLevelAt(total), total, "the next level is always ahead");
                Assert.AreEqual(level + 1, WhackRound.Level(WhackRound.NextLevelAt(total)), "and reaching it is one level up");
                last = level;
            }
        }

        [Test]
        public void PaceRisesGentlyAndIsCapped()
        {
            for (int level = 1; level <= 4; level++) Assert.AreEqual(1f, WhackRound.Pace(level), "the first four levels teach; they don't rush");
            Assert.That(WhackRound.Pace(5), Is.EqualTo(1.06f).Within(1e-4));
            for (int level = 5; level < 200; level++)
            {
                Assert.GreaterOrEqual(WhackRound.Pace(level + 1), WhackRound.Pace(level));
                Assert.LessOrEqual(WhackRound.Pace(level), 1.6f, "never faster than a person can slap");
            }
        }

        [Test]
        public void TheCardAlwaysNamesSomethingToPlayFor()
        {
            StringAssert.Contains("Hoppers join", WhackRound.NextUnlock(0));
            StringAssert.Contains("Golden critters join", WhackRound.NextUnlock(WhackRound.ShyAt));
            string after = WhackRound.NextUnlock(WhackRound.GoldenAt + 10);
            StringAssert.Contains("Level 4", after);
            StringAssert.Contains("Level 5 at " + (WhackRound.GoldenAt + WhackRound.LevelEvery) + " caught: 50 to go", after);
            Assert.IsNotEmpty(WhackRound.NextUnlock(100000), "there is always a next level");
        }

        [Test]
        public void OldAndSurplusSessionFilesAreRemoved()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cyclopea-prune-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
                for (int i = 0; i < 12; i++)
                {
                    string f = Path.Combine(dir, "s" + i.ToString("00") + ".jsonl");
                    File.WriteAllText(f, "{}");
                    // s00 is the newest (1 day old); s10 and s11 are 41 and 45 days old.
                    File.SetLastWriteTimeUtc(f, now.AddDays(-(i < 10 ? 1 + i : 31 + i)));
                }
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "kept: not a session file");
                int removed = PlayLog.Prune(dir, keepDays: 30, keepFiles: 8, nowUtc: now);
                Assert.AreEqual(4, removed, "two too old, two beyond the newest eight");
                Assert.AreEqual(8, Directory.GetFiles(dir, "*.jsonl").Length);
                Assert.True(File.Exists(Path.Combine(dir, "s00.jsonl")), "the newest stays");
                Assert.False(File.Exists(Path.Combine(dir, "s11.jsonl")), "the oldest goes");
                Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
                Assert.AreEqual(0, PlayLog.Prune(Path.Combine(dir, "missing"), 30, 8, now), "a missing folder is not an error");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
