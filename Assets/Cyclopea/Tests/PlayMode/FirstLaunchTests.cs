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
            PlayerPrefs.DeleteKey(WhackRound.TotalKey); PlayerPrefs.DeleteKey(WhackRound.BestKey); PlayerPrefs.DeleteKey(WhackRound.LevelKey);
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
                while (!s.InArcade && Time.time - t0 < 2f) yield return null;
                Assert.True(s.InArcade, "the first round of Critter Whack starts on its own");
                s.Arcade.Abort();
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

        [UnityTest, Timeout(300000)]
        public IEnumerator AutoplayPlaysTheWholeGameFromFirstLaunchToTheHomeCard()
        {
            // The device end-to-end run (Tools/device_e2e.py) sends autoplay=60 over adb; here the same path
            // runs through the remote file, fast.
            var s = Object.FindFirstObjectByType<SpotSession>();
            var dev = Object.FindFirstObjectByType<DevControls>();
            Assert.True(s.ShowingNotice);
            dev.AutoplayStepSeconds = 0.05f;
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "remote.txt"), "autoplay=60\n");
            var seen = new System.Collections.Generic.HashSet<string>();
            float t0 = Time.time;
            while (Time.time - t0 < 2f && !dev.Autoplaying) yield return null;
            Assert.True(dev.Autoplaying, "the remote command started autoplay");
            while (dev.Autoplaying && Time.time - t0 < 360f)
            {
                if (s.ShowingTutorial) seen.Add("tutorial");
                if (s.InArcade) seen.Add("arcade");
                if (s.AwaitingChoice) seen.Add("trial " + s.CurrentRung);
                if (s.IsDone) seen.Add("done");
                yield return null;
            }
            StringAssert.StartsWith("complete", dev.LastAutoplayStop, "stopped because: " + dev.LastAutoplayStop);
            Assert.True(s.ShowingHome, "ends on the home card");
            foreach (var need in new[] { "tutorial", "arcade", "trial Spot", "done" })
                Assert.That(seen, Does.Contain(need), "went through " + need);
            Assert.That(PlayerPrefs.GetInt(SpotSession.NoticeKey, 0), Is.EqualTo(1));
            Debug.Log("[Cyclopea test] autoplay saw: " + string.Join(", ", seen) + " in " + (Time.time - t0).ToString("0") + " s");
        }

        [UnityTest]
        public IEnumerator TheWinkShowsThatOneEyeCannotFindIt()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            d.IntroWaitSeconds = 0.2f;   // no finger: it hides on its own
            s.WinkSeeSeconds = s.WinkClosedSeconds = s.WinkOpenSeconds = 0.3f;
            s.Choose(0, "test");
            float t0 = Time.time;
            while (!s.TutorialCreatureHidden && Time.time - t0 < 4f) yield return null;
            Assert.True(s.TutorialCreatureHidden);

            var lines = new System.Collections.Generic.Dictionary<int, string>();
            t0 = Time.time;
            while (Time.time - t0 < 3f)
            {
                int beat = s.TutorialWinkBeat;
                if (beat >= 0 && !lines.ContainsKey(beat)) lines[beat] = s.Title.text;
                if (beat == 3) break;
                yield return null;
            }
            Assert.That(lines.Keys, Is.EquivalentTo(new[] { 0, 1, 2, 3 }), "see it, close one eye, open both, pinch");
            StringAssert.Contains("float", lines[0]);
            StringAssert.Contains("close one eye", lines[1]);
            StringAssert.Contains("Open both", lines[2]);
            StringAssert.Contains("pinch", lines[3]);
            foreach (var l in lines.Values) Assert.That(l.Split('\n').Length, Is.LessThanOrEqualTo(2), "no wall of text: " + l);

            s.Choose(s.CurrentTarget, "test");
            StringAssert.Contains("You found it", s.Title.text);
            Assert.That(s.TutorialWinkBeat, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator WordsSitOnPanelsThatNeverCoverTheStatic()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            yield return null; yield return null;
            Assert.True(s.ShowingNotice);
            foreach (var tm in new[] { s.Title, s.Status })
            {
                var panel = tm.GetComponent<TextPanel>();
                Assert.NotNull(panel, tm.name + " has a panel");
                Assert.True(panel.Panel.enabled, tm.name + " panel shows while it has words");
                // Every corner of the words lies inside the panel, measured in the panel's own plane.
                var pt = panel.Panel.transform;
                var lb = tm.GetComponent<Renderer>().localBounds;
                foreach (var cx in new[] { lb.min.x, lb.max.x })
                    foreach (var cy in new[] { lb.min.y, lb.max.y })
                    {
                        Vector3 inPanel = pt.InverseTransformPoint(tm.transform.TransformPoint(new Vector3(cx, cy, lb.center.z)));
                        Assert.That(Mathf.Abs(inPanel.x), Is.LessThan(0.5f), tm.name + ": the panel covers its words (x)");
                        Assert.That(Mathf.Abs(inPanel.y), Is.LessThan(0.5f), tm.name + ": the panel covers its words (y)");
                    }

                // From the head, no point of any square is hidden behind a panel.
                var plane = new Plane(pt.forward, pt.position);
                int covered = 0;
                foreach (var p in s.Patches)
                    for (int iy = 0; iy <= 4; iy++)
                    for (int ix = 0; ix <= 4; ix++)
                    {
                        Vector3 target = p.transform.TransformPoint(new Vector3(-0.45f + 0.225f * ix, -0.45f + 0.225f * iy, 0f));
                        var ray = new Ray(s.Head.position, (target - s.Head.position).normalized);
                        if (!plane.Raycast(ray, out float hit) || hit >= Vector3.Distance(s.Head.position, target)) continue;
                        Vector3 local = pt.InverseTransformPoint(ray.GetPoint(hit));
                        if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f) covered++;
                    }
                Assert.That(covered, Is.EqualTo(0), tm.name + "'s panel hides part of the static");
            }
            s.Status.text = "";
            yield return null;
            Assert.False(s.Status.GetComponent<TextPanel>().Panel.enabled, "no empty panels");
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
