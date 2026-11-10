using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cyclopea.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cyclopea.PlayTests
{
    /// <summary>Critter Whack in the real scene: a round plays, scores and ends on its card; slaps land;
    /// fakes cost; one eye never sees a single tile change; the home card leads to both modes.</summary>
    public class ArcadePlayTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
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
            var arcade = Object.FindFirstObjectByType<WhackRound>();
            if (arcade != null) arcade.Abort();   // a round left running must never finish later and save a score
            PlayerPrefs.DeleteKey(WhackRound.BestKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            Health.Reset();
        }

        static SpotSession Session()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            Assert.NotNull(s.Arcade, "Critter Whack wired");
            s.Arcade.CountdownSeconds = 0.2f;
            s.Arcade.RoundSeconds = 10f;
            return s;
        }

        [UnityTest]
        public IEnumerator ARoundPlaysScoresAndEndsOnItsCard()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            Assert.True(s.InArcade);
            yield return null;
            Assert.That(a.Tiles.All(t => t.GetComponent<Renderer>().enabled), "the nine tiles show");
            Assert.That(s.Patches.All(p => !p.GetComponent<Renderer>().enabled), "the four squares step aside");

            var up = new List<int>();
            float until = Time.time + 30f;
            while (s.InArcade && Time.time < until)
            {
                a.RealUp(up);
                foreach (int t in up) a.Whack(a.Tiles[t], "test");
                yield return null;
            }
            Assert.False(s.InArcade, "the round ends on its own");
            Assert.True(s.IsDone);
            Assert.That(a.Caught, Is.GreaterThanOrEqualTo(4), "a perfect player catches every one");
            Assert.That(a.Score, Is.GreaterThan(0));
            StringAssert.StartsWith("Round over: ", s.Title.text);
            StringAssert.Contains("caught", s.Title.text);
            StringAssert.Contains("New best score!", s.Title.text);
            StringAssert.Contains("Hoppers join at 15", s.Title.text);
            Assert.That(PlayerPrefs.GetInt(WhackRound.TotalKey), Is.EqualTo(a.Caught));
            Assert.That(a.Tiles.All(t => !t.GetComponent<Renderer>().enabled && t.Arcsec == 0 && !t.Decoy), "tiles cleared and hidden");
            Assert.That(s.Patches.All(p => p.GetComponent<Renderer>().enabled), "the squares are back for the menu");
            Assert.That(s.Creatures.FoundThisSession, Is.GreaterThan(0), "catches go to the field guide");
            Assert.That(a.LevelArcsec, Is.LessThan(1920), "a player who catches everything gets finer critters");

            // Next pick: the home card, with the best round on it.
            yield return new WaitForSecondsRealtime(0.4f);
            s.Choose(0, "test");
            Assert.True(s.ShowingHome);
            StringAssert.Contains("Best round " + a.Score.ToString("N0"), s.Title.text);
            Assert.That(s.Home.Labels.Select(l => l.text.Split('\n')[0]), Is.EqualTo(new[] { "Play", "Depth", "Field guide", "Options" }));
        }

        [UnityTest]
        public IEnumerator AFlatFakeCostsAndARealOneMustFloat()
        {
            var s = Session();
            var a = s.Arcade;
            a.ForceNext = WhackRound.Kind.Trickster;
            s.StartArcade();
            float until = Time.time + 10f;
            WhackRound.Kind kind = WhackRound.Kind.Normal;
            int tile = -1;
            while ((tile = a.UpTile(out kind)) < 0 && Time.time < until) yield return null;
            Assert.That(tile, Is.GreaterThanOrEqualTo(0));
            Assert.AreEqual(WhackRound.Kind.Trickster, kind);
            yield return null;
            Assert.True(a.Tiles[tile].Decoy, "a fake is a flat outline either eye can see");
            Assert.AreEqual(0, a.Tiles[tile].Arcsec, "...and it does not float");
            a.Whack(a.Tiles[tile], "test");
            Assert.AreEqual(1, a.Fooled);
            Assert.AreEqual(0, a.Caught);
            StringAssert.Contains("Flat fake", s.Status.text);
            a.Abort();
        }

        [UnityTest]
        public IEnumerator OneEyeNeverSeesASingleTileChange()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            float[] before = a.Tiles.Select(t => t.Seed).ToArray();
            float until = Time.time + 10f;
            while (a.UpTile(out _) < 0 && Time.time < until)
            {
                before = a.Tiles.Select(t => t.Seed).ToArray();
                yield return null;
            }
            // The frame a critter appeared, every tile's dots were redrawn, not just its own.
            float[] after = a.Tiles.Select(t => t.Seed).ToArray();
            Assert.That(Enumerable.Range(0, 9).All(i => after[i] != before[i]), "all nine tiles redraw together");
            a.Abort();
        }

        [UnityTest]
        public IEnumerator ASlapOnTheDeskCatchesIt()
        {
            var s = Session();
            var a = s.Arcade;
            var tips = Object.FindFirstObjectByType<FingerTips>();
            var tip = new GameObject("TestTip").transform;
            tips.SimulatedTip = tip;
            s.StartArcade();
            var up = new List<int>();
            float until = Time.time + 10f;
            while (up.Count == 0 && Time.time < until) { a.RealUp(up); yield return null; }
            Assert.That(up.Count, Is.GreaterThan(0));
            var t = a.Tiles[up[0]].transform;
            tip.position = t.position - t.forward * 0.08f;   // above the tile
            yield return null;
            yield return null;
            tip.position = t.position - t.forward * 0.005f;  // down onto the desk
            yield return null;
            yield return null;
            Assert.AreEqual(1, a.Caught, "the slap caught it");
            tip.position = t.position - t.forward * 0.005f;  // resting there does not slap again
            yield return null;
            Assert.AreEqual(0, a.Whiffs, "a hand resting on the desk never slaps twice");
            tips.SimulatedTip = null;
            Object.Destroy(tip.gameObject);
            a.Abort();
        }

        [UnityTest]
        public IEnumerator DepthCheckIsOneTapFromHome()
        {
            var s = Session();
            s.ShowHome();
            yield return new WaitForSecondsRealtime(0.4f);
            s.Choose(1, "test");
            Assert.AreEqual(HomeCard.Page.Check, s.Home.Current);
            Assert.That(s.Home.Labels.Select(l => l.text), Is.EqualTo(new[] { "Two eyes", "One eye", "", "Back" }));
            yield return new WaitForSecondsRealtime(0.4f);   // the card ignores a second pick within 0.35 s
            s.Choose(0, "test");
            yield return null;
            Assert.True(s.AwaitingChoice, "two eyes starts the measured Depth Check");
            Assert.AreEqual(SessionLog.Cue.Stereo, s.Cue);
        }
    }
}
