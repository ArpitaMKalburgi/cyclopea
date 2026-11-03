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
    /// Loads the real game scene and plays whole sessions with a simulated player, through the same
    /// selection path a pinch takes. Catches wiring and runtime errors before the headset is on.
    /// </summary>
    public class SessionPlayTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            // XR is not running in the editor; the rig's OVR components log about it, which is expected.
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        static SpotSession Session()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s, "SpotSession missing from scene");
            s.FeedbackSeconds = 0f;
            return s;
        }

        static IEnumerator Play(SpotSession s, System.Func<SpotSession, int> choose, int guard = 400)
        {
            int n = 0;
            while (!s.IsDone && n++ < guard)
            {
                if (s.AwaitingChoice) s.Choose(choose(s), "test");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SceneIsWired()
        {
            var s = Session();
            Assert.That(s.Patches.Length, Is.EqualTo(4));
            Assert.That(s.Patches.All(p => p != null && p.GetComponent<Collider>() != null), "each patch needs a collider for the pinch ray");
            Assert.NotNull(s.Head, "head transform");
            Assert.NotNull(s.Surface, "surface finder");
            Assert.That(s.Selectors.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(s.Selectors.All(x => x != null), "every selector is a real component, not a missing script");
            foreach (var t in s.GetComponentsInChildren<Transform>(true))
                Assert.That(t.GetComponents<Component>().All(c => c != null), "missing script on " + t.name);
            var dev = Object.FindFirstObjectByType<DevControls>();
            Assert.That(dev.Isdk != null && dev.HandRay != null && dev.Dwell != null, "input modes wired");
            Assert.NotNull(Object.FindFirstObjectByType<DevControls>());
            Assert.That(s.Patches.Count(p => p.Arcsec > 0), Is.EqualTo(1), "exactly one square hides a creature");
            Assert.That(s.Patches.All(p => p.GetComponent<IsdkPatchEvents>() is IsdkPatchEvents e && e.Ray != null && e.Gaze != null),
                "each square is an Interaction SDK ray target and gaze target");
            Assert.NotNull(Object.FindFirstObjectByType<Oculus.Interaction.GazeInteractor>(), "ISDK gaze interactor");
            Assert.NotNull(Object.FindFirstObjectByType<IsdkSelector>());
            Assert.NotNull(Object.FindFirstObjectByType<MicrogestureSelector>());
            Assert.NotNull(Object.FindFirstObjectByType<Meta.XR.EnvironmentRaycastManager>(), "depth raycast fallback");
            Assert.NotNull(Object.FindFirstObjectByType<ScoreAnchor>());
            Assert.NotNull(Object.FindFirstObjectByType<TuningPanel>());
            yield return null;
        }

        [UnityTest]
        public IEnumerator SimulatedObserverCompletesSessionAndIsLogged()
        {
            var s = Session();
            s.Restart();
            var observer = new SimulatedObserver(60, seed: 4);
            var rng = new System.Random(9);
            yield return Play(s, x =>
            {
                bool sees = observer.Respond(x.CurrentArcsec);
                if (sees) return x.CurrentTarget;
                int wrong = rng.Next(0, 3);
                return wrong >= x.CurrentTarget ? wrong + 1 : wrong;
            });
            Assert.True(s.IsDone, "session should finish");
            Assert.True(File.Exists(s.LogPath), "log file " + s.LogPath);
            var lines = File.ReadAllLines(s.LogPath);
            Assert.That(lines.Count(l => l.Contains("\"type\":\"trial\"")), Is.GreaterThan(15));
            var summary = lines.Last();
            StringAssert.Contains("\"type\":\"summary\"", summary);
            StringAssert.DoesNotContain("NaN", summary);
            StringAssert.Contains("Depth Score", s.Title.text);
            Debug.Log("[Cyclopea test] " + s.Title.text.Replace('\n', ' ') + " | " + summary);
        }

        [UnityTest]
        public IEnumerator ChancePlayerGetsTheHonestCard()
        {
            var s = Session();
            s.Restart();
            var rng = new System.Random(5);
            yield return Play(s, x => rng.Next(0, 4));
            Assert.True(s.IsDone);
            StringAssert.Contains("didn't use depth from two eyes", s.Title.text);

            // The honest card leads straight into the motion version: the next pinch starts it.
            s.Choose(0, "test");
            Assert.That(s.Cue, Is.EqualTo(SessionLog.Cue.Motion));
            StringAssert.Contains("Sway your head", s.Title.text);
            s.StartCue(SessionLog.Cue.Stereo);
        }

        [UnityTest]
        public IEnumerator MotionModeMovesOnlyTheTargetAndCompletes()
        {
            var s = Session();
            s.StartCue(SessionLog.Cue.Motion);
            StereoPatch.AutoSway = true;   // the editor has no tracked head; simulate the sway
            try
            {
                Assert.That(s.Patches.All(p => p.Arcsec == 0), "no disparity in motion mode");
                Assert.That(s.Patches.Count(p => p.MotionArcsec > 0), Is.EqualTo(1));
                float target = 0, others = 0;
                for (int f = 0; f < 50; f++)   // 2.5 s real time: more than one full sway period
                {
                    yield return new WaitForSecondsRealtime(0.05f);
                    foreach (var p in s.Patches)
                        if (p.MotionArcsec > 0) target = Mathf.Max(target, Mathf.Abs(p.LastCommonShiftUV));
                        else others = Mathf.Max(others, Mathf.Abs(p.LastCommonShiftUV));
                }
                Assert.That(target, Is.GreaterThan(0.005f), "the creature's dots slide with the sway");
                Assert.That(others, Is.EqualTo(0f), "plain static never slides");

                var observer = new SimulatedObserver(120, seed: 8);
                var rng = new System.Random(3);
                yield return Play(s, x =>
                {
                    if (observer.Respond(x.CurrentArcsec)) return x.CurrentTarget;
                    int wrong = rng.Next(0, 3);
                    return wrong >= x.CurrentTarget ? wrong + 1 : wrong;
                });
                Assert.True(s.IsDone);
                StringAssert.Contains("Motion Depth Score", s.Title.text);
                var lines = File.ReadAllLines(s.LogPath);
                Assert.That(lines.Where(l => l.Contains("\"type\":\"trial\"")).All(l => l.Contains("\"cue\":\"Motion\"")));
                Debug.Log("[Cyclopea test] " + s.Title.text.Replace('\n', ' '));
            }
            finally
            {
                StereoPatch.AutoSway = false;
                s.StartCue(SessionLog.Cue.Stereo);
            }
        }
    }
}
