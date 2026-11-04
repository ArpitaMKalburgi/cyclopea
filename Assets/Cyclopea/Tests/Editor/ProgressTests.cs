using System;
using Cyclopea.Core;
using NUnit.Framework;

namespace Cyclopea.Tests
{
    /// <summary>Growth between sessions: daily bests, personal bests, the graph scale, goals and the streak line.</summary>
    public class ProgressTests
    {
        static DateTime Day(int d, int hour = 10) => new DateTime(2026, 11, d, hour, 0, 0);

        [Test]
        public void ADayKeepsItsFinestScoreAndAPersonalBestNeedsAnEarlierScore()
        {
            var h = new ProgressHistory();
            Assert.False(h.Record(Day(4), SessionLog.Cue.Stereo, 120), "a first score is a start, not a best");
            Assert.False(h.Record(Day(4, 11), SessionLog.Cue.Stereo, 150), "worse than today's");
            Assert.That(h.Count, Is.EqualTo(1));
            Assert.That(h.Days[0].BestArcsec, Is.EqualTo(120));
            Assert.That(h.Days[0].Sessions, Is.EqualTo(2));
            Assert.True(h.Record(Day(4, 12), SessionLog.Cue.Stereo, 95), "finer than ever, even on the same day");
            Assert.That(h.Days[0].BestArcsec, Is.EqualTo(95));
            Assert.False(h.Record(Day(5), SessionLog.Cue.Stereo, 95.3), "the same whole arcseconds on the card is not a new best");
            Assert.True(h.Record(Day(6), SessionLog.Cue.Stereo, 62));
            Assert.That(h.Count, Is.EqualTo(3));
            Assert.False(h.Record(Day(6), SessionLog.Cue.Motion, 30), "motion keeps its own bests");
            Assert.That(h.Best(SessionLog.Cue.Stereo).Value.BestArcsec, Is.EqualTo(62));
            Assert.That(h.Best(SessionLog.Cue.Motion).Value.BestArcsec, Is.EqualTo(30));
            Assert.False(h.Record(Day(7), SessionLog.Cue.Stereo, double.NaN), "unmeasured sessions are not recorded");
            Assert.That(h.Count, Is.EqualTo(4));
        }

        [Test]
        public void RecentIsOldestFirstAndPerCue()
        {
            var h = new ProgressHistory();
            for (int d = 1; d <= 20; d++) h.Record(Day(d), SessionLog.Cue.Stereo, 400 - d * 10);
            h.Record(Day(20), SessionLog.Cue.Motion, 500);
            var r = h.Recent(SessionLog.Cue.Stereo, 14);
            Assert.That(r.Count, Is.EqualTo(14));
            Assert.That(r[0].Date, Is.EqualTo("2026-11-07"));
            Assert.That(r[13].Date, Is.EqualTo("2026-11-20"));
            Assert.That(h.Recent(SessionLog.Cue.Motion, 14).Count, Is.EqualTo(1));
            Assert.That(h.MainCue, Is.EqualTo(SessionLog.Cue.Stereo));
            var m = new ProgressHistory();
            m.Record(Day(3), SessionLog.Cue.Motion, 300);
            Assert.That(m.MainCue, Is.EqualTo(SessionLog.Cue.Motion), "a one-eye player's graph is their motion scores");
        }

        [Test]
        public void SavesRoundTripAndDamageIsSkipped()
        {
            var h = new ProgressHistory();
            h.Record(Day(4), SessionLog.Cue.Stereo, 83.24);
            h.Record(Day(4), SessionLog.Cue.Stereo, 90);
            h.Record(Day(5), SessionLog.Cue.Motion, 240);
            string s = h.Serialize();
            Assert.That(s, Is.EqualTo("2026-11-04,S,83.2,2;2026-11-05,M,240.0,1"));
            var back = ProgressHistory.Parse(s);
            Assert.That(back.Serialize(), Is.EqualTo(s));
            var hurt = ProgressHistory.Parse("garbage;2026-13-40,S,1,1;2026-11-06,S,-3,1;2026-11-07,X,50,1;2026-11-08,S,50,zz;" + s);
            Assert.That(hurt.Count, Is.EqualTo(3), "only the readable entries");
            Assert.That(hurt.Days[0].Date, Is.EqualTo("2026-11-04"), "sorted by day");
            Assert.That(ProgressHistory.Parse(null).Count, Is.EqualTo(0));
        }

        [Test]
        public void OldDaysAreDropped()
        {
            var h = new ProgressHistory();
            var start = new DateTime(2026, 1, 1, 9, 0, 0);
            for (int d = 0; d < ProgressHistory.KeepDays + 10; d++) h.Record(start.AddDays(d), SessionLog.Cue.Stereo, 200);
            Assert.That(h.Count, Is.EqualTo(ProgressHistory.KeepDays));
            Assert.That(h.Days[0].Date, Is.EqualTo(start.AddDays(10).ToString("yyyy-MM-dd")));
        }

        [Test]
        public void GraphGoesUpForFinerDepthOneStepPerHalving()
        {
            Assert.That(ProgressHistory.GraphHeight(1920), Is.EqualTo(0).Within(1e-9));
            Assert.That(ProgressHistory.GraphHeight(10), Is.EqualTo(1).Within(1e-9));
            Assert.That(ProgressHistory.GraphHeight(5000), Is.EqualTo(0), "clamped");
            Assert.That(ProgressHistory.GraphHeight(1), Is.EqualTo(1), "clamped");
            double a = ProgressHistory.GraphHeight(240) - ProgressHistory.GraphHeight(480);
            double b = ProgressHistory.GraphHeight(30) - ProgressHistory.GraphHeight(60);
            Assert.That(a, Is.GreaterThan(0));
            Assert.That(a, Is.EqualTo(b).Within(1e-9), "log scale: every halving is the same step");
        }

        [Test]
        public void TheNextGoalIsTheNextRarityThenTheRestOfTheGuide()
        {
            var c = new CreatureCollection();
            Assert.That(Goals.Next(c), Is.EqualTo("Next: find your first creature."));
            c.Add(Creatures.Species.Clover, Creatures.Rarity.Common);
            Assert.That(Goals.Next(c), Is.EqualTo("Next: an Uncommon creature. They hide below 480″."));
            c.Add(Creatures.Species.Clover, Creatures.Rarity.Uncommon);
            Assert.That(Goals.Next(c), Is.EqualTo("Next: a Rare creature. They hide below 120″."));
            c.Add(Creatures.Species.Trifoil, Creatures.Rarity.Rare);
            Assert.That(Goals.Next(c), Is.EqualTo("Next: an Elusive creature. They hide below 40″."));
            c.Add(Creatures.Species.Sixling, Creatures.Rarity.Elusive);
            Assert.That(Goals.Next(c), Is.EqualTo("Next: finish the field guide. 12 kinds to go."));
            for (int s = 0; s < Creatures.SpeciesCount; s++)
                for (int r = 0; r < Creatures.RarityCount; r++) c.Add((Creatures.Species)s, (Creatures.Rarity)r);
            StringAssert.StartsWith("Field guide complete", Goals.Next(c));
            // The tiers in the goal are the tiers rarity is given by.
            foreach (Creatures.Rarity r in Enum.GetValues(typeof(Creatures.Rarity)))
                if (r != Creatures.Rarity.Common)
                    Assert.That(Creatures.FromArcsec(Goals.Below(r) - 0.01), Is.EqualTo(r));
        }

        [Test]
        public void TheStreakLineSaysWhatTodayDoes()
        {
            var today = Day(10);
            Assert.That(Goals.StreakLine(new Streak.State(), today), Is.EqualTo(""));
            Assert.That(Goals.StreakLine(new Streak.State { LastDay = "2026-11-10", Count = 4, Best = 4 }, today), Is.EqualTo("Day 4 in a row"));
            Assert.That(Goals.StreakLine(new Streak.State { LastDay = "2026-11-09", Count = 3, Best = 3 }, today),
                Is.EqualTo("3 days in a row. Play today for 4"));
            Assert.That(Goals.StreakLine(new Streak.State { LastDay = "2026-11-09", Count = 1, Best = 1 }, today),
                Is.EqualTo("Played yesterday. Play today for 2 in a row"));
            Assert.That(Goals.StreakLine(new Streak.State { LastDay = "2026-11-02", Count = 5, Best = 5 }, today), Is.EqualTo(""),
                "a lapsed streak is not mentioned");
        }

        [Test]
        public void BestLineAndHomeWordsCarryNoMedicalClaims()
        {
            var h = new ProgressHistory();
            Assert.That(Goals.BestLine(h, SessionLog.Cue.Stereo), Is.EqualTo(""));
            h.Record(Day(5), SessionLog.Cue.Stereo, 61.6);
            Assert.That(Goals.BestLine(h, SessionLog.Cue.Stereo), Is.EqualTo("Best 62″ (Nov 5)"));
            var c = new CreatureCollection();
            c.Add(Creatures.Species.Starfin, Creatures.Rarity.Rare);
            foreach (var text in new[] { Goals.BestLine(h, SessionLog.Cue.Stereo), Goals.Next(c),
                                         Goals.StreakLine(new Streak.State { LastDay = "2026-11-09", Count = 3 }, Day(10)) })
                foreach (var banned in new[] { "treat", "therap", "diagnos", "amblyop", "cure", "improve your vision", "heal" })
                    StringAssert.DoesNotContain(banned, text.ToLowerInvariant());
        }

        [Test]
        public void DecoyTrialsAreLoggedWithWhetherTheFakeWasPicked()
        {
            var log = new SessionLog();
            log.Add(new SessionLog.Trial { SessionId = "s", SubjectId = "P01", Device = "d", Decoy = true, DecoyPicked = true, Phase = "Decoy" });
            log.Add(new SessionLog.Trial { SessionId = "s", SubjectId = "P01", Device = "d", Phase = "Descent" });
            StringAssert.Contains("\"decoy\":true,\"decoy_picked\":true", log.Lines[0]);
            StringAssert.DoesNotContain("decoy_picked", log.Lines[1]);
        }
    }
}
