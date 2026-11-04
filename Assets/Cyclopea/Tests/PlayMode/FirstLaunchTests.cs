using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cyclopea.PlayTests
{
    /// <summary>First launch shows the notice, starts nothing until it is accepted, and never shows it again.</summary>
    public class FirstLaunchTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerPrefs.DeleteKey(SpotSession.NoticeKey);
            PlayerPrefs.DeleteKey(SpotSession.TutorialKey);
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
            PlayerPrefs.DeleteKey(SpotSession.ProgressKey);
            Options.Reset();
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoticeFirstThenPlayAndNeverAgain()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            Assert.True(s.ShowingNotice, "first launch opens on the notice");
            Assert.False(s.AwaitingChoice);
            Assert.That(s.Patches.All(p => p.Arcsec == 0 && p.MotionArcsec == 0), "no creature behind the notice");
            StringAssert.Contains("not a medical device", s.Title.text);
            foreach (var banned in new[] { "treat", "therap", "diagnos", "amblyop", "cure" })
                StringAssert.DoesNotContain(banned, SpotSession.NoticeText.ToLowerInvariant());

            s.Choose(2, "test");
            Assert.True(s.ShowingTutorial, "one pinch accepts, and the tutorial creature appears");
            Assert.That(PlayerPrefs.GetInt(SpotSession.NoticeKey, 0), Is.EqualTo(1), "remembered for next launch");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TutorialCreatureRidesYourFingerHidesAndYouPinchItOut()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            d.IntroWaitSeconds = 5f;
            d.RideSeconds = 1f;
            s.TutorialRewardSeconds = 0.4f;
            var tip = new GameObject("SimulatedTip").transform;
            tip.position = new Vector3(0f, -50f, 0f);   // hand far away at first
            d.Tips.SimulatedTip = tip;
            try
            {
                s.Choose(0, "test");   // accept the notice
                Assert.True(s.ShowingTutorial);
                StringAssert.Contains("Hold out a finger", s.Title.text);
                Assert.True(d.IntroRunning, "a creature appears beside the squares");
                Assert.That(s.Patches.All(p => p.Arcsec == 0), "nothing hidden yet");
                var c = d.Latest;
                Vector3 mid = s.Patches.Aggregate(Vector3.zero, (acc, p) => acc + p.transform.position) / 4f;
                Assert.That(Vector3.Distance(c.transform.position, mid), Is.LessThan(0.01f),
                    "it starts in the middle of the four squares, where a first-timer is already looking");

                yield return new WaitForSeconds(0.6f);
                tip.position = c.transform.position + Vector3.up * 0.1f;   // reach toward it
                float t0 = Time.time;
                while (!d.Riding && Time.time - t0 < 2f) yield return null;
                Assert.True(d.Riding, "it climbs onto the finger");

                t0 = Time.time;
                while (!s.TutorialCreatureHidden && Time.time - t0 < 4f) yield return null;
                Assert.True(s.TutorialCreatureHidden, "then hops into a square and sinks into the static");
                yield return null;   // Destroy lands at the end of the frame the dive finished in
                Assert.True(c == null,"the visible creature is gone into the static");
                Assert.That(s.Patches.Count(p => p.Arcsec == 1920), Is.EqualTo(1), "it floats in exactly one square");
                int hid = s.CurrentTarget;
                Assert.That(s.Patches[hid].Arcsec, Is.EqualTo(1920));

                s.Choose((hid + 1) % 4, "test");
                Assert.True(s.ShowingTutorial, "a wrong pinch keeps teaching");
                Assert.That(d.Collection.Total, Is.EqualTo(0));

                s.Choose(hid, "test");
                Assert.That(d.Collection.Total, Is.EqualTo(1), "found: the first creature is collected");
                StringAssert.Contains("You found it", s.Title.text);
                t0 = Time.time;
                while (!s.AwaitingChoice && Time.time - t0 < 2f) yield return null;
                Assert.True(s.AwaitingChoice, "round 1 starts on its own");
                Assert.That(PlayerPrefs.GetInt(SpotSession.TutorialKey, 0), Is.EqualTo(1), "tutorial only once");
            }
            finally
            {
                d.Tips.SimulatedTip = null;
                Object.Destroy(tip.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AcceptingTheNoticeByLookingMakesLookAndHoldTheInput()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            yield return null;
            var dev = Object.FindFirstObjectByType<DevControls>();
            Assert.True(dev.Dwell.enabled, "look and hold works on the notice, so nobody is stuck behind a pinch");
            StringAssert.Contains("look at one and hold", s.Status.text);
            s.Choose(1, "head-dwell");
            Assert.True(s.ShowingTutorial);
            Assert.True(Options.LookAndHold, "chosen by looking: the game will use looking");
            yield return null;
            Assert.That(DevControls.Mode, Is.EqualTo(DevControls.InputMode.Dwell));
            Assert.True(dev.Dwell.enabled);
            Options.Reset();
        }

        [UnityTest]
        public IEnumerator TutorialWithoutHandsStillHidesAndAPinchHurriesIt()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            d.IntroWaitSeconds = 30f;
            s.Choose(0, "test");   // accept the notice
            yield return new WaitForSeconds(0.6f);
            Assert.False(s.TutorialCreatureHidden, "still waiting for a finger");
            s.Choose(1, "test");   // impatient pinch
            float t0 = Time.time;
            while (!s.TutorialCreatureHidden && Time.time - t0 < 3f) yield return null;
            Assert.True(s.TutorialCreatureHidden, "a pinch sends it into hiding without waiting");
        }
    }
}
