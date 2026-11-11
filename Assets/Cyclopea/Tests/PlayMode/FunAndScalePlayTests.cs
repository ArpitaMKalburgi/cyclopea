using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cyclopea.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cyclopea.PlayTests
{
    /// <summary>
    /// Critter Whack's fun layer (points pop up where you caught, a streak climbs the scale, the last seconds
    /// are a frenzy, a golden preview before goldens unlock), the play log (every round, its frame times, any
    /// error), and a soak: twenty rounds back to back leave nothing behind.
    /// </summary>
    public class FunAndScalePlayTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
            PlayerPrefs.DeleteKey(SpotSession.ProgressKey);
            PlayerPrefs.DeleteKey(SpotSession.CueKey);
            PlayerPrefs.DeleteKey(WhackRound.BestKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            Options.Reset();
            Health.Reset();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        [TearDown]
        public void Clean()
        {
            Time.timeScale = 1f;
            var arcade = Object.FindFirstObjectByType<WhackRound>();
            if (arcade != null) arcade.Abort();
            PlayerPrefs.DeleteKey(WhackRound.BestKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            Health.Reset();
        }

        static SpotSession Session(float roundSeconds = 10f)
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            Assert.NotNull(s.Arcade, "Critter Whack wired");
            s.Arcade.CountdownSeconds = 0.2f;
            s.Arcade.RoundSeconds = roundSeconds;
            return s;
        }

        static IEnumerator UntilPlaying(WhackRound a)
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (!a.Playing && Time.realtimeSinceStartup < until) yield return null;
            Assert.True(a.Playing, "the round started");
        }

        /// <summary>Waits for a real critter and slaps it. False if none came up in time.</summary>
        static IEnumerator CatchOne(WhackRound a, List<bool> ok, WhackRound.Kind? only = null)
        {
            var up = new List<int>();
            float until = Time.realtimeSinceStartup + 8f;
            while (a.Playing && Time.realtimeSinceStartup < until)
            {
                a.RealUp(up);
                foreach (int t in up)
                {
                    if (only.HasValue && (!a.TryKindAt(t, out var k) || k != only.Value)) continue;
                    int before = a.Caught;
                    a.Whack(a.Tiles[t], "slap");
                    if (a.Caught > before) { ok.Add(true); yield break; }
                }
                yield return null;
            }
            ok.Add(false);
        }

        [UnityTest]
        public IEnumerator ACatchShowsItsPointsAndAStreakClimbsTheScale()
        {
            var s = Session(20f);
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);

            var ok = new List<bool>();
            yield return CatchOne(a, ok);
            Assert.True(ok.Last(), "caught one");
            Assert.That(a.PopupsShown, Is.GreaterThanOrEqualTo(1), "its points float up from the tile");
            StringAssert.StartsWith("+100", a.LastPopup);
            Assert.That(a.LastCatchPitch, Is.EqualTo(1f).Within(1e-4), "the first catch of a streak at its own pitch");

            for (int i = 0; i < 3; i++) { yield return CatchOne(a, ok); Assert.True(ok.Last(), "caught " + (i + 2)); }
            Assert.AreEqual(4, a.Combo);
            Assert.That(a.LastCatchPitch, Is.EqualTo(Mathf.Pow(2f, 3f / 12f)).Within(1e-4), "four in a row: three semitones up");
            StringAssert.Contains("×2", a.LastPopup, "from three in a row the pop-up shows the multiplier");

            // An empty slap ends the streak; the next catch starts the scale again.
            yield return new WaitForSeconds(0.25f);   // past the tile debounce
            var free = Enumerable.Range(0, 9).First(t => !a.TryKindAt(t, out _));
            a.Whack(a.Tiles[free], "slap");
            Assert.AreEqual(0, a.Combo);
            yield return CatchOne(a, ok);
            Assert.True(ok.Last());
            Assert.That(a.LastCatchPitch, Is.EqualTo(1f).Within(1e-4));

            // The points fade away by themselves.
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(0, a.PopupsShown, "pop-ups are gone after their moment");
        }

        [UnityTest]
        public IEnumerator TheLastSecondsAreAFrenzyWorthDouble()
        {
            var s = Session(6f);
            var a = s.Arcade;
            a.FrenzySeconds = 2.5f;
            s.StartArcade();
            yield return UntilPlaying(a);
            Assert.False(a.Frenzy, "not at the start");
            float until = Time.realtimeSinceStartup + 6f;
            while (!a.Frenzy && Time.realtimeSinceStartup < until) yield return null;
            Assert.True(a.Frenzy, "the last seconds are a frenzy");
            yield return null;
            StringAssert.Contains("Frenzy", s.Title.text, "the HUD says so");

            int before = a.Score;
            var ok = new List<bool>();
            yield return CatchOne(a, ok);
            Assert.True(ok.Last(), "caught one in the frenzy");
            Assert.That(a.FrenzyCaught, Is.GreaterThanOrEqualTo(1));
            Assert.That(a.Score - before, Is.GreaterThanOrEqualTo(200), "a frenzy catch is worth double");
            StringAssert.Contains("×2", a.LastPopup);
        }

        [UnityTest]
        public IEnumerator ShortRoundsHaveNoFrenzy()
        {
            var s = Session(10f);   // tests and anything shorter than twice the frenzy stay as they were
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            float until = Time.time + 10.5f;
            bool seen = false;
            while (s.InArcade && Time.time < until) { seen |= a.Frenzy; yield return null; }
            Assert.False(seen);
        }

        [UnityTest]
        public IEnumerator AGoldenPreviewComesBeforeGoldensAreUnlocked()
        {
            var s = Session(12f);
            var a = s.Arcade;
            a.PreviewMinRoundSeconds = 0f;
            a.PreviewGoldenWindow = new Vector2(0.4f, 0.5f);
            s.StartArcade();
            yield return UntilPlaying(a);

            var ok = new List<bool>();
            float until = Time.realtimeSinceStartup + 10f;
            while (!a.PreviewShown && Time.realtimeSinceStartup < until) yield return null;
            Assert.True(a.PreviewShown, "a golden preview came up in its window");
            yield return CatchOne(a, ok, WhackRound.Kind.Golden);
            Assert.True(ok.Last(), "and could be caught");
            Assert.AreEqual(1, a.GoldenCaught);
            StringAssert.StartsWith("Golden!", a.LastPopup);
            StringAssert.Contains("+500", a.LastPopup);
            StringAssert.Contains("golden critter", s.Status.text.ToLowerInvariant(), "the player is told what it was");
        }

        [UnityTest]
        public IEnumerator NoPreviewOnceGoldensAreUnlocked()
        {
            PlayerPrefs.SetInt(WhackRound.TotalKey, WhackRound.GoldenAt);
            var s = Session(4f);
            var a = s.Arcade;
            a.PreviewMinRoundSeconds = 0f;
            a.PreviewGoldenWindow = new Vector2(0.2f, 0.3f);
            s.StartArcade();
            yield return UntilPlaying(a);
            yield return new WaitForSeconds(1.5f);
            Assert.False(a.PreviewShown, "unlocked goldens come by their own odds, no preview");
        }

        [UnityTest]
        public IEnumerator EveryRoundIsLoggedWithItsFrameTimes()
        {
            var log = Object.FindFirstObjectByType<PlayLog>();
            Assert.NotNull(log, "the play log is in the scene");
            var s = Session(3f);
            var a = s.Arcade;

            // A round to the end of its clock, with one error logged while it plays.
            s.StartArcade();
            yield return UntilPlaying(a);
            var ok = new List<bool>();
            yield return CatchOne(a, ok);
            LogAssert.Expect(LogType.Error, "play log test: a deliberate error");
            Debug.LogError("play log test: a deliberate error");
            float until = Time.realtimeSinceStartup + 8f;
            while (s.InArcade && Time.realtimeSinceStartup < until) yield return null;
            Assert.False(s.InArcade, "the round ended");
            yield return null;

            // A second round, left from the menu.
            s.StartArcade();
            yield return UntilPlaying(a);
            yield return new WaitForSeconds(0.4f);
            s.PressMenu("test");
            yield return null;
            s.MenuHome();
            yield return null;

            Assert.AreEqual(2, log.RoundsLogged, "both rounds logged, the finished one and the one left");
            var lines = log.Written;
            StringAssert.StartsWith("{\"type\":\"app\"", lines[0], "the log opens with what the launch runs on");
            var rounds = lines.Where(l => l.StartsWith("{\"type\":\"round\"")).ToList();
            Assert.AreEqual(2, rounds.Count);
            StringAssert.Contains("\"ended\":\"clock\"", rounds[0]);
            StringAssert.Contains("\"ended\":\"left\"", rounds[1]);
            StringAssert.Contains("\"inputs\":\"slap:", rounds[0]);
            foreach (var r in rounds)
            {
                StringAssert.Contains("\"ms_p99\":", r, "every round carries its frame times");
                StringAssert.Contains("\"fps_mean\":", r);
            }
            StringAssert.Contains("\"caught\":1", rounds[0]);
            Assert.False(rounds[0].Contains("\"errors\":0"), "the error during the round is counted in it");
            Assert.True(lines.Any(l => l.Contains("\"type\":\"error\"") && l.Contains("a deliberate error")),
                "and the error itself is in the log, once");
            Assert.True(lines.Any(l => l.StartsWith("{\"type\":\"health\"")), "with the system's health");

            Assert.NotNull(log.Path);
            Assert.True(File.Exists(log.Path), "written on the device's storage");
            var onDisk = File.ReadAllLines(log.Path);
            Assert.AreEqual(lines.Count, onDisk.Length, "the file holds every line");
        }

        /// <summary>Scene objects outside any creature, and creature bodies (each with its own parts).</summary>
        static Dictionary<string, int> CountObjects(out int other, out int creatures)
        {
            other = creatures = 0;
            var names = new Dictionary<string, int>();
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool inCreature = false;
                for (var t = go.transform; t != null; t = t.parent)
                    if (t.GetComponent<CreatureBody>() != null) { inCreature = true; break; }
                if (!inCreature) { other++; names[go.name] = names.TryGetValue(go.name, out int n) ? n + 1 : 1; }
                else if (go.GetComponent<CreatureBody>() != null) creatures++;
            }
            return names;
        }

        static string Added(Dictionary<string, int> before, Dictionary<string, int> after) =>
            string.Join(", ", after.Where(kv => kv.Value > (before.TryGetValue(kv.Key, out int b) ? b : 0))
                                   .Select(kv => kv.Key + " +" + (kv.Value - (before.TryGetValue(kv.Key, out int b2) ? b2 : 0))));

        [UnityTest, Timeout(300000)]
        public IEnumerator TwentyRoundsInARowLeaveNothingBehind()
        {
            var log = Object.FindFirstObjectByType<PlayLog>();
            var s = Session(8f);
            var a = s.Arcade;
            Time.timeScale = 4f;
            int baseline = -1;
            long memBaseline = 0;
            var namesBefore = new Dictionary<string, int>();
            var up = new List<int>();
            const int Rounds = 20;
            for (int round = 1; round <= Rounds; round++)
            {
                s.StartArcade();
                float until = Time.realtimeSinceStartup + 15f;
                while (!a.Playing && Time.realtimeSinceStartup < until) yield return null;
                while (s.InArcade && Time.realtimeSinceStartup < until)
                {
                    a.RealUp(up);
                    foreach (int t in up) a.Whack(a.Tiles[t], "slap");
                    yield return null;
                }
                Assert.False(s.InArcade, "round " + round + " ended");
                Assert.False(s.BreakDue, "no rest break in this many seconds");
                yield return null;
                if (round == 2)
                {
                    yield return new WaitForSeconds(8f);   // creatures still hopping home settle first
                    namesBefore = CountObjects(out baseline, out _);
                    memBaseline = System.GC.GetTotalMemory(true);
                }
            }
            yield return new WaitForSeconds(8f);
            Time.timeScale = 1f;

            var namesAfter = CountObjects(out int objects, out int creatures);
            string added = Added(namesBefore, namesAfter);
            long mem = System.GC.GetTotalMemory(true);
            Debug.Log("[Cyclopea] soak: " + Rounds + " rounds, objects (creatures aside) " + baseline + " -> " + objects +
                      ", creatures " + creatures + ", managed heap " + (memBaseline / 1048576.0).ToString("0.0") + " -> " +
                      (mem / 1048576.0).ToString("0.0") + " MB, total caught " + WhackRound.TotalCaught +
                      (added.Length > 0 ? ", added: " + added : ""));
            // A leak would add objects with every catch (hundreds over these rounds). Creatures are the game's own
            // bounded population: at most a full nest, one resident of each kind in the room, and a few mid-hop.
            Assert.That(objects, Is.LessThanOrEqualTo(baseline), "nothing but creatures is ever added, round after round: " + added);
            Assert.That(creatures, Is.LessThanOrEqualTo(s.Creatures.NestSize + Colony.Max + 4), "creatures stay a bounded population");
            Assert.That(mem - memBaseline, Is.LessThan(16L * 1024 * 1024), "the managed heap stays flat");
            Assert.That(s.Creatures.NestCount, Is.LessThanOrEqualTo(s.Creatures.NestSize), "the nest never overflows");
            Assert.That(a.GetComponentsInChildren<TextMesh>(true).Count(t => t.name == "Points"), Is.LessThanOrEqualTo(4), "a fixed pool of pop-ups");
            Assert.AreEqual(Rounds, log.RoundsLogged, "every round logged");
            Assert.That(log.Written.Count, Is.LessThanOrEqualTo(200), "the play log keeps a bounded tail in memory");
            Assert.That(WhackRound.TotalCaught, Is.GreaterThan(Rounds), "they were real rounds");
        }
    }
}
