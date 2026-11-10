using System.Collections;
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
    /// Silent failures made visible, in the real scene: a broken two-eye view is never read as the
    /// player's eyes, a disturbed round is not counted and the player is told why, and every
    /// fallback is named in the log.
    /// </summary>
    public class HealthPlayTests
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
            PlayerPrefs.DeleteKey(WhackRound.TotalKey); PlayerPrefs.DeleteKey(WhackRound.BestKey); PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            PlayerPrefs.DeleteKey(SpotSession.CueKey);
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
            SystemCheck.SimulateSameImageBothEyes = false;
            SystemCheck.SimulateTrackingLost = false;
            Health.Reset();
        }

        static SpotSession Session()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            s.FeedbackSeconds = 0f; s.CatchFeedbackSeconds = 0f; s.RungIntroSeconds = 0f;
            return s;
        }

        static int Wrong(SpotSession x, System.Random rng) { int w = rng.Next(0, 3); return w >= x.CurrentTarget ? w + 1 : w; }

        [UnityTest]
        public IEnumerator ABrokenTwoEyeViewIsNeverBlamedOnTheEyes()
        {
            var check = Object.FindFirstObjectByType<SystemCheck>();
            Assert.NotNull(check, "SystemCheck missing from scene");
            check.CheckInEditor = true;
            SystemCheck.SimulateSameImageBothEyes = true;
            yield return new WaitForSeconds(check.FaultAfterSeconds + 0.3f);
            Assert.AreEqual(Health.Level.Fault, Health.Get(Health.View).Level, Health.Summary());

            // A player who can see nothing (both eyes get one picture) answers at chance. Without the
            // check this run ends on "no depth from two eyes" (ChancePlayerGetsTheHonestCard).
            var s = Session();
            s.Restart();
            var rng = new System.Random(5);
            int n = 0;
            while (!s.IsDone && n++ < 400)
            {
                if (s.AwaitingChoice) s.Choose(rng.Next(0, 4), "test");
                yield return null;
            }
            Assert.True(s.IsDone);
            Assert.AreEqual(Verdict.Outcome.Uncertain, s.LastVerdict.Result);
            Assert.That(s.Gate.Clean, Is.EqualTo(0), "no round counted while the view was broken");
            StringAssert.Contains("Not sure this time", s.Title.text);
            StringAssert.Contains("same picture", s.Title.text);
            StringAssert.DoesNotContain("didn't use depth", s.Title.text);

            var lines = File.ReadAllLines(s.LogPath);
            Assert.That(lines.Count(l => l.Contains("\"type\":\"trial\"") && l.Contains("\"clean\":false")), Is.EqualTo(TrialGate.GiveUpAfterDirty));
            Assert.True(lines.Any(l => l.Contains("\"type\":\"summary\"") && l.Contains("\"outcome\":\"Uncertain\"") && l.Contains("\"threshold_arcsec\":null")));
            Assert.True(lines.Any(l => l.Contains("\"type\":\"health\"") && l.Contains("\"level\":\"Fault\"")), "the fault is in the log");

            // "Not sure" never leads into the one-eye (motion) version: that is only for "no depth".
            SystemCheck.SimulateSameImageBothEyes = false;
            s.Choose(0, "test");
            Assert.AreEqual(SessionLog.Cue.Stereo, s.Cue);
        }

        [UnityTest]
        public IEnumerator AStutterInOneRoundDoesNotCountAndThePlayerIsTold()
        {
            var s = Session();
            s.Restart();
            var observer = new SimulatedObserver(60, seed: 4);
            var rng = new System.Random(9);
            int answered = 0, n = 0;
            bool inject = false, injected = false;
            string toast = null;
            while (!s.IsDone && n++ < 3000)
            {
                if (s.AwaitingChoice)
                {
                    inject = !injected && answered >= 5 && s.CurrentDecoy < 0 && s.CurrentRung == SkillLadder.Rung.Spot;
                    if (inject)
                        Health.Set(Health.Frames, Health.Level.Fault, "40 % late frames at 72 Hz", Time.time, "The view stuttered.");
                    bool sees = observer.Respond(s.CurrentArcsec);
                    int pick = s.CurrentRung == SkillLadder.Rung.Catch ? s.CurrentTarget
                             : sees ? s.CurrentTarget : s.CurrentRung == SkillLadder.Rung.Sort ? s.SortOther : Wrong(s, rng);
                    if (s.CurrentRung == SkillLadder.Rung.Catch) s.CatchAt(pick, s.CatchHeightM, "test");
                    else s.Choose(pick, "test");
                    if (inject) { toast = s.Status.text; injected = true; }
                    answered++;
                }
                yield return null;
            }
            Assert.True(s.IsDone);
            StringAssert.StartsWith("That one didn't count: the view stuttered.", toast, "said first, even when a new kind is found too");
            Assert.AreEqual(1, s.Gate.Dirty, "only the round the stutter touched");
            Assert.True(s.LastVerdict.IsScore, "one disturbed round costs a round, not the score: " + s.LastVerdict.Result + " " + s.LastVerdict.Reason);

            var lines = File.ReadAllLines(s.LogPath);
            Assert.AreEqual(1, lines.Count(l => l.Contains("\"clean\":false") && l.Contains("\"fault\":\"40 % late frames at 72 Hz\"")));
            var spot = lines.First(l => l.Contains("\"type\":\"summary\"") && l.Contains("\"rung\":\"Spot\""));
            StringAssert.Contains("\"dirty_trials\":1", spot);
            StringAssert.Contains("\"outcome\":\"" + s.LastVerdict.Result + "\"", spot);
            if (s.LastVerdict.Result == Verdict.Outcome.Measured)
            {
                StringAssert.Contains("\"plus_minus_pct\":", spot);
                StringAssert.Contains("(±", s.Title.text, "a score shows the range it is good to");
            }
        }

        [UnityTest]
        public IEnumerator EveryPartReportsAndFallbacksAreNamed()
        {
            yield return new WaitForSeconds(1.2f);
            foreach (var part in new[] { Health.View, Health.Head, Health.Frames, Health.Room, Health.Sound, Health.Save })
                Assert.NotNull(Health.Get(part), part + " never reported: " + Health.Summary());
            Assert.AreEqual(Health.Level.Ok, Health.Get(Health.Sound).Level, "every sound is in the bank: " + Health.Get(Health.Sound).Detail);
            // In the editor there is no headset: the view, head and frame checks say so instead of claiming ok.
            Assert.AreEqual(Health.Level.Fallback, Health.Get(Health.View).Level);
            StringAssert.Contains("n/a", Health.CheckLine());
            // No room scan in the editor: the player is told where the squares are and why.
            var room = Health.Get(Health.Room);
            Assert.AreNotEqual(Health.Level.Fault, room.Level);
            if (room.Level == Health.Level.Fallback) StringAssert.StartsWith("No room scan", room.PlayerLine);
        }
    }
}
