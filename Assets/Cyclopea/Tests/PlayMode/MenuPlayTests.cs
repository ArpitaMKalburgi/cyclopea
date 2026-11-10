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
    /// <summary>
    /// A way back from anywhere (first headset playtest, Nov 10: "we need a back button, options everywhere").
    /// The Menu button pauses a round, the Depth Check or the tutorial and offers Resume | Home | start over |
    /// Options; every score card says where each square goes; Back returns to where the player came from.
    /// Also: one hand coming down is one slap, however many of its points land.
    /// </summary>
    public class MenuPlayTests
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
            if (arcade != null) arcade.Abort();
            PlayerPrefs.DeleteKey(WhackRound.BestKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            Options.Reset();
            Health.Reset();
        }

        static SpotSession Session()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            Assert.NotNull(s.Arcade, "Critter Whack wired");
            Assert.NotNull(s.MenuButton, "the Menu button is in the scene");
            s.Arcade.CountdownSeconds = 0.2f;
            s.Arcade.RoundSeconds = 20f;
            return s;
        }

        static string[] FirstLines(SpotSession s) => s.Home.Labels.Select(l => l.text.Split('\n')[0]).ToArray();
        static bool Shown(Component c) => c.GetComponent<Renderer>().enabled;
        static IEnumerator Wait() { yield return new WaitForSecondsRealtime(0.4f); }   // the card ignores a second pick within 0.35 s

        static IEnumerator UntilPlaying(WhackRound a)
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (!a.Playing && Time.realtimeSinceStartup < until) yield return null;
            Assert.True(a.Playing, "the round started");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMenuIsOneTapAwayInARoundAndTheClockWaits()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            Assert.True(Shown(s.MenuButton), "the Menu button sits beside the grid");
            Assert.True(s.MenuLabel.gameObject.activeSelf);
            Assert.AreEqual("Menu", s.MenuLabel.text);
            float left = a.TimeLeft;

            s.PressMenu("test");
            Assert.True(s.MenuOpen);
            Assert.AreEqual(HomeCard.Page.Pause, s.Home.Current);
            Assert.That(FirstLines(s), Is.EqualTo(new[] { "Resume", "Home", "New", "Options" }));
            StringAssert.StartsWith("Paused", s.Title.text);
            yield return null;
            Assert.That(a.Tiles.All(t => !Shown(t)), "the tiles step aside for the menu");
            Assert.That(s.Patches.All(p => Shown(p) && p.Arcsec == 0 && !p.Decoy), "the four squares are the menu's buttons");
            Assert.False(Shown(s.MenuButton), "the Menu button hides while the menu is open");
            Assert.True(s.WantsLookAndHold, "look and hold works in the menu, as in every menu");

            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(a.TimeLeft, Is.EqualTo(left).Within(0.15f), "the clock waits");
            int caught = a.Caught;

            s.Choose(0, "test");   // Resume
            Assert.False(s.MenuOpen);
            Assert.True(s.InArcade);
            yield return null;
            Assert.That(a.Tiles.All(t => Shown(t)), "the round is back");
            Assert.That(s.Patches.All(p => !Shown(p)));
            Assert.AreEqual(caught, a.Caught, "the menu answered nothing in the round");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(a.TimeLeft, Is.LessThan(left - 0.2f), "and its clock runs again");
        }

        [UnityTest]
        public IEnumerator FromTheMenuAPlayerCanStartOverOrGoHome()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            var up = new List<int>();
            float until = Time.realtimeSinceStartup + 10f;
            while (up.Count == 0 && Time.realtimeSinceStartup < until) { a.RealUp(up); yield return null; }
            Assert.That(up.Count, Is.GreaterThan(0));
            a.Whack(a.Tiles[up[0]], "test");
            Assert.AreEqual(1, a.Caught);

            s.PressMenu("test");
            yield return Wait();
            s.Choose(2, "test");   // New round
            Assert.False(s.MenuOpen);
            Assert.True(s.InArcade);
            Assert.AreEqual(0, a.Caught, "a fresh round");
            Assert.AreEqual(1, PlayerPrefs.GetInt(WhackRound.TotalKey), "the catch from the round left behind still counts");
            Assert.AreEqual(0, PlayerPrefs.GetInt(WhackRound.BestKey, 0), "...but a round left early sets no best score");

            yield return UntilPlaying(a);
            s.PressMenu("test");
            yield return Wait();
            s.Choose(1, "test");   // Home
            Assert.True(s.ShowingHome);
            Assert.False(s.InArcade);
            Assert.False(s.MenuOpen);
            yield return null;
            Assert.That(a.Tiles.All(t => !Shown(t)), "the round is gone");
            Assert.False(Shown(s.MenuButton), "home has its own Options square: no Menu button there");
            Assert.That(FirstLines(s), Is.EqualTo(new[] { "Play", "Depth", "Field guide", "Options" }));
        }

        [UnityTest]
        public IEnumerator TheDepthCheckPausesAndResumesTheSameTrial()
        {
            var s = Session();
            s.StartCue(SessionLog.Cue.Stereo);
            yield return null;
            Assert.True(s.AwaitingChoice);
            int target = s.CurrentTarget;
            double arcsec = s.Patches[target].Arcsec;
            Assert.That(arcsec, Is.GreaterThan(0));
            int trials = s.TrialsDone;
            yield return null;
            Assert.True(Shown(s.MenuButton), "the Menu button shows in the Depth Check too");

            s.PressMenu("test");
            Assert.That(s.Patches.All(p => p.Arcsec == 0), "nothing floats behind a menu label");
            Assert.That(FirstLines(s), Is.EqualTo(new[] { "Resume", "Home", "Start", "Options" }));
            StringAssert.Contains("Depth Check waits", s.Title.text);
            yield return Wait();
            s.Choose(0, "test");   // Resume
            Assert.True(s.AwaitingChoice);
            Assert.AreEqual(target, s.CurrentTarget);
            Assert.AreEqual(arcsec, s.Patches[target].Arcsec, "the same trial, as it was");
            Assert.AreEqual(trials, s.TrialsDone, "the menu's picks were never answers");
            StringAssert.DoesNotContain("Paused", s.Title.text, "the instructions are back");

            yield return null;
            s.PressMenu("test");
            yield return Wait();
            s.Choose(2, "test");   // Start over
            Assert.True(s.AwaitingChoice);
            Assert.AreEqual(0, s.TrialsDone, "a fresh Depth Check");
            Assert.False(s.MenuOpen);
        }

        [UnityTest]
        public IEnumerator TheTutorialCanBeSkippedFromTheMenu()
        {
            var s = Session();
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 0);
            s.StartTutorial();
            yield return null;
            yield return null;
            Assert.True(s.ShowingTutorial);
            Assert.True(Shown(s.MenuButton), "a way out from the very first screen of play");
            s.PressMenu("test");
            Assert.AreEqual("Skip to\nthe game", s.Home.Labels[2].text);
            yield return Wait();
            s.Choose(2, "test");
            Assert.True(s.InArcade, "straight into Critter Whack");
            Assert.AreEqual(1, PlayerPrefs.GetInt(SpotSession.TutorialKey), "and the tutorial won't come back");
        }

        [UnityTest]
        public IEnumerator OptionsFromTheMenuComeBackToTheMenu()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            s.PressMenu("test");
            yield return Wait();
            s.Choose(3, "test");
            Assert.AreEqual(HomeCard.Page.Options, s.Home.Current);
            yield return Wait();
            s.Choose(0, "test");
            Assert.True(Options.HighContrast, "options change mid-round");
            yield return Wait();
            s.Choose(3, "test");   // Back
            Assert.AreEqual(HomeCard.Page.Pause, s.Home.Current, "Back goes back to the menu, not home");
            Assert.True(s.MenuOpen);
            Assert.True(s.InArcade, "the round is still waiting");
            yield return Wait();
            s.Choose(0, "test");
            Assert.False(s.MenuOpen);
            Assert.True(a.Playing);
        }

        [UnityTest]
        public IEnumerator EveryScoreCardSaysWhereEachSquareGoes()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            a.EndNow();
            Assert.True(s.IsDone);
            Assert.AreEqual(HomeCard.Page.Card, s.Home.Current);
            Assert.That(FirstLines(s), Is.EqualTo(new[] { "Play", "Home", "Depth", "Options" }));
            Assert.That(s.Home.Labels[0].text, Is.EqualTo("Play\nagain"));
            StringAssert.StartsWith("Round over", s.Title.text);

            yield return Wait();
            s.Choose(2, "test");
            Assert.AreEqual(HomeCard.Page.Check, s.Home.Current, "the Depth Check, straight from the card");
            yield return Wait();
            s.Choose(3, "test");   // Back
            Assert.AreEqual(HomeCard.Page.Card, s.Home.Current, "Back returns to the card");
            StringAssert.StartsWith("Round over", s.Title.text, "with its score");
            yield return Wait();
            s.Choose(3, "test");
            Assert.AreEqual(HomeCard.Page.Options, s.Home.Current);
            yield return Wait();
            s.Choose(3, "test");
            Assert.AreEqual(HomeCard.Page.Card, s.Home.Current);
            yield return Wait();
            s.Choose(0, "test");   // Play again
            Assert.True(s.InArcade);

            // The Depth Check's card: Check again | Home | Play | Options.
            a.Abort();
            s.FeedbackSeconds = 0.01f; s.RungIntroSeconds = 0f; s.LadderRungs = false; s.DecoyChance = 0f;
            s.StartCue(SessionLog.Cue.Stereo);
            int guard = 0;
            while (!s.IsDone && guard++ < 3000)
            {
                if (s.AwaitingChoice)
                {
                    if (s.CurrentRung == SkillLadder.Rung.Catch) s.CatchAt(s.CurrentTarget, s.CatchHeightM, "test");
                    else s.Choose(s.CurrentTarget, "test");
                }
                yield return null;
            }
            s.FeedbackSeconds = 0.75f; s.RungIntroSeconds = 2.5f; s.LadderRungs = true; s.DecoyChance = 0.18f;
            Assert.True(s.IsDone);
            Assert.That(FirstLines(s), Is.EqualTo(new[] { "Check", "Home", "Play", "Options" }));
            yield return Wait();
            s.Choose(2, "test");
            Assert.True(s.InArcade, "Play from the Depth Check's card");
        }

        [UnityTest]
        public IEnumerator TakingTheHeadsetOffMidRoundOpensTheMenu()
        {
            var s = Session();
            var a = s.Arcade;
            s.StartArcade();
            yield return UntilPlaying(a);
            float left = a.TimeLeft;

            s.HeadsetOff();
            Assert.True(s.MenuOpen, "the menu opens while the headset is off");
            Assert.True(s.Paused);
            s.Choose(0, "test");
            Assert.True(s.MenuOpen, "nothing is chosen while nobody wears the headset");
            yield return new WaitForSecondsRealtime(0.5f);

            s.HeadsetOn();
            Assert.True(s.MenuOpen, "back on: the menu, never a clock already running");
            Assert.AreEqual(HomeCard.Page.Pause, s.Home.Current);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(a.TimeLeft, Is.EqualTo(left).Within(0.15f), "the round waited the whole time");
            yield return Wait();
            s.Choose(0, "test");   // Resume
            Assert.False(s.MenuOpen);
            Assert.True(a.Playing);

            // Outside a game (home, cards) the headset coming off just pauses: no menu over the menu.
            a.EndNow();
            s.HeadsetOff();
            Assert.False(s.MenuOpen);
            s.HeadsetOn();
            Assert.False(s.Paused);
        }

        [UnityTest]
        public IEnumerator TheRestBreakIsOfferedNotImposed()
        {
            var s = Session();
            float cap = s.CapSeconds, skip = s.BreakSkipAfterSeconds;
            try
            {
                s.CapSeconds = 0f;              // the budget is used up: the next game starts with the break
                s.BreakSkipAfterSeconds = 0.3f;
                s.StartArcade();
                Assert.True(s.OnBreak);
                Assert.False(s.InArcade);
                s.Choose(0, "test");
                Assert.True(s.OnBreak, "not in the first moments");
                yield return new WaitForSecondsRealtime(0.4f);
                yield return null;
                StringAssert.Contains("go on now", s.Status.text, "the player is told they may go on");
                s.CapSeconds = cap;
                s.Choose(0, "test");
                Assert.True(s.ShowingHome, "a judge at minute ten is never locked out");
            }
            finally
            {
                s.CapSeconds = cap;
                s.BreakSkipAfterSeconds = skip;
            }
        }

        [UnityTest]
        public IEnumerator FastHandTrackingOnlyWhileARoundIsInPlay()
        {
            var s = Session();
            var a = s.Arcade;
            Assert.False(a.FastHands);
            s.StartArcade();
            Assert.False(a.FastHands, "not during the countdown");
            yield return UntilPlaying(a);
            Assert.True(a.FastHands, "fast slaps get Meta's Fast Motion Mode");
            s.PressMenu("test");
            Assert.False(a.FastHands, "menus get the steadier default tracking");
            yield return Wait();
            s.Choose(0, "test");
            Assert.True(a.FastHands);
            a.EndNow();
            Assert.False(a.FastHands, "and off once the round is over");
        }

        [UnityTest]
        public IEnumerator OneHandIsOneSlap()
        {
            var s = Session();
            var a = s.Arcade;
            var tips = Object.FindFirstObjectByType<FingerTips>();
            var tip = new GameObject("TestTip").transform;
            var palm = new GameObject("TestPalm").transform;
            tips.SimulatedTip = tip;
            tips.SimulatedPalm = palm;
            try
            {
                s.StartArcade();
                yield return UntilPlaying(a);
                var up = new List<int>();
                float until = Time.realtimeSinceStartup + 10f;
                while (up.Count == 0 && Time.realtimeSinceStartup < until) { a.RealUp(up); yield return null; }
                Assert.That(up.Count, Is.GreaterThan(0));
                int t = up[0], n = t % 3 == 0 ? t + 1 : t - 1;   // the tile beside it, same row
                Assert.False(a.IsRealUp(a.Tiles[n]), "a new player has one critter up at a time");
                Transform T = a.Tiles[t].transform, N = a.Tiles[n].transform;

                // Fingertips over the critter, palm over the empty tile beside it; the palm lands a frame first.
                tip.position = T.position - T.forward * 0.08f;
                palm.position = N.position - N.forward * 0.08f;
                yield return null;
                yield return null;
                palm.position = N.position - N.forward * 0.005f;
                yield return null;
                tip.position = T.position - T.forward * 0.005f;
                yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.AreEqual(1, a.Caught, "the hand landed on the critter");
                Assert.AreEqual(0, a.Whiffs, "its palm on the next tile is the same slap, not an empty one");

                // Lift and slap the empty tile with the whole hand: one empty tap, not two.
                tip.position = N.position - N.forward * 0.08f;
                palm.position = N.position - N.forward * 0.08f;
                yield return new WaitForSecondsRealtime(0.4f);
                if (a.IsRealUp(a.Tiles[n])) Assert.Inconclusive("a critter popped up in the test tile");
                tip.position = N.position - N.forward * 0.005f;
                palm.position = N.position - N.forward * 0.005f;
                yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(a.Whiffs, Is.LessThanOrEqualTo(1), "one hand, one slap");
            }
            finally
            {
                tips.SimulatedTip = null;
                tips.SimulatedPalm = null;
                Object.Destroy(tip.gameObject);
                Object.Destroy(palm.gameObject);
            }
        }
    }
}
