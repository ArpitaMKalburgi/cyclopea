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
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);   // these tests start past the first-launch notice
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1); // ...and past the tutorial creature
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
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
            Assert.That(s.CreatureVoice != null && s.CreatureVoice.spatialBlend == 1f &&
                        s.CreatureVoice.GetComponent<MetaXRAudioSource>() != null, "creature voice is spatialised by Meta XR Audio");
            Assert.NotNull(Object.FindFirstObjectByType<RoomAcoustics>(), "room acoustics");
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            Assert.That(d != null && s.Creatures == d, "creature director wired to the session");
            Assert.That(d.Material != null && d.Material.shader.name == "Cyclopea/Creature",
                "creature material and shader");
            Assert.That(d.Tips != null && d.Head == s.Head && d.Patches.Length == 4 && d.Voice == s.CreatureVoice, "creature director wiring");
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

        static CreatureDirector Director()
        {
            var d = Object.FindFirstObjectByType<CreatureDirector>();
            Assert.NotNull(d, "CreatureDirector missing from scene");
            d.Tips.SimulatedTip = null;
            return d;
        }

        /// <summary>The shader's mask SDF in patch UV, written out again here as the independent reference.</summary>
        static float MaskSdf(StereoPatch p, Vector2 uv)
        {
            Vector2 d = uv - p.MaskCenter;
            float ang = Mathf.Atan2(d.y, d.x);
            float r = p.MaskRadius * (1f + p.MaskWobble * Mathf.Sin(p.MaskLobes * ang + p.Seed));
            return d.magnitude - r;
        }

        [UnityTest]
        public IEnumerator CorrectPickRevealsTheCreatureYouSaw()
        {
            var s = Session();
            var d = Director();
            s.Restart();
            var p = s.Patches[s.CurrentTarget];
            double level = s.CurrentArcsec;
            int before = d.Collection.Total;
            s.Choose(s.CurrentTarget, "test");

            var c = d.Latest;
            Assert.NotNull(c, "a found creature comes out of the static");
            Assert.That(c.Species, Is.EqualTo(Creatures.FromLobes(p.MaskLobes)), "species = the outline's lobes");
            Assert.That(c.Rarity, Is.EqualTo(Creatures.FromArcsec(level)), "rarity = how fine the depth was");
            Assert.That(c.GetComponentsInChildren<Collider>(true), Is.Empty, "creatures never block a pick");

            // Its footprint, carried into the square's own UV space, lies on the hidden outline.
            var mesh = c.GetComponentInChildren<MeshFilter>().sharedMesh;
            var body = c.GetComponentInChildren<MeshFilter>().transform;
            float worst = 0f;
            for (int i = 0; i < 48; i++)
            {
                Vector3 local = p.transform.InverseTransformPoint(body.TransformPoint(mesh.vertices[i]));
                worst = Mathf.Max(worst, Mathf.Abs(MaskSdf(p, new Vector2(local.x + 0.5f, local.y + 0.5f))));
            }
            Assert.That(worst, Is.LessThan(2e-3f), "the creature's outline is the shape hidden in the static (UV)");

            Assert.That(d.Collection.Total, Is.EqualTo(before + 1), "collected");
            Assert.That(PlayerPrefs.GetString(CreatureDirector.CollectionKey), Is.EqualTo(d.Collection.Serialize()), "saved");
            Assert.That(d.FoundThisSession, Is.EqualTo(1));

            float t0 = Time.time;
            while (d.NestCount == 0 && Time.time - t0 < 4f) yield return null;
            Assert.That(d.NestCount, Is.EqualTo(1), "with no hand near, it settles in the nest");
            Assert.That(c.Dissolve, Is.EqualTo(0f), "fully visible in the nest");
        }

        [UnityTest]
        public IEnumerator CreatureClimbsOntoAFingertipAndRides()
        {
            var s = Session();
            var d = Director();
            var tip = new GameObject("SimulatedTip").transform;
            try
            {
                s.Restart();
                var p = s.Patches[s.CurrentTarget];
                tip.position = p.transform.position - p.transform.forward * 0.25f;
                d.Tips.SimulatedTip = tip;
                s.Choose(s.CurrentTarget, "test");
                var c = d.Latest;

                float t0 = Time.time;
                while (!d.Riding && Time.time - t0 < 3f) yield return null;
                Assert.True(d.Riding, "it hops onto the fingertip");

                tip.position += new Vector3(0.08f, 0.03f, 0f);   // a real hand moves far less per frame
                yield return new WaitForSeconds(0.35f);
                Assert.That(Vector3.Distance(c.transform.position, tip.position), Is.LessThan(0.02f), "it rides along with the finger");

                tip.gameObject.SetActive(false);   // hand lost: it hops down to the nest
                t0 = Time.time;
                while (d.NestCount == 0 && Time.time - t0 < 3f) yield return null;
                Assert.That(d.NestCount, Is.EqualTo(1), "hand gone: it goes to the nest");
            }
            finally
            {
                d.Tips.SimulatedTip = null;
                Object.Destroy(tip.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator WrongPickPeeksAndCollectsNothing()
        {
            var s = Session();
            var d = Director();
            s.Restart();
            int before = d.Collection.Total;
            s.Choose((s.CurrentTarget + 1) % 4, "test");
            var peek = d.Latest;
            Assert.NotNull(peek, "the real one peeks out");
            Assert.That(d.Collection.Total, Is.EqualTo(before), "nothing collected");
            yield return new WaitForSeconds(1.5f);
            Assert.True(peek == null, "and ducks back into the static");
            Assert.That(d.NestCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ScoreCardCountsTheCreatures()
        {
            var s = Session();
            var d = Director();
            s.Restart();
            yield return Play(s, x => x.CurrentTarget);
            Assert.True(s.IsDone);
            Assert.That(d.FoundThisSession, Is.EqualTo(s.TrialsDone), "every correct pick is a creature");
            StringAssert.Contains("You found " + d.FoundThisSession + " creatures", s.Title.text);
            StringAssert.Contains("Field guide", s.Title.text);
            yield return new WaitForSeconds(2.5f);
            Assert.That(d.NestCount, Is.LessThanOrEqualTo(d.NestSize), "the nest stays small");
        }

        [UnityTest]
        public IEnumerator PauseFreezesTheTrialAndResumes()
        {
            var s = Session();
            s.Restart();
            Assert.True(s.AwaitingChoice);
            s.SetPaused(SpotSession.PauseManual, true);
            s.Choose(s.CurrentTarget, "test");
            Assert.That(s.TrialsDone, Is.EqualTo(0), "a pinch while paused does nothing");
            yield return new WaitForSecondsRealtime(0.3f);
            s.SetPaused(SpotSession.PauseManual, false);
            s.Choose(s.CurrentTarget, "test");
            Assert.That(s.TrialsDone, Is.EqualTo(1), "play continues after resume");
            var trial = File.ReadAllLines(s.LogPath).First(l => l.Contains("\"type\":\"trial\""));
            int rt = int.Parse(System.Text.RegularExpressions.Regex.Match(trial, "\"rt_ms\":(\\d+)").Groups[1].Value);
            Assert.That(rt, Is.LessThan(250), "paused time is not counted as reaction time");
        }

        [UnityTest]
        public IEnumerator PlayBudgetEndsInARestBreak()
        {
            var s = Session();
            s.CapSeconds = 0.3f;
            s.BreakSeconds = 0.6f;
            s.Restart();
            yield return new WaitForSecondsRealtime(0.5f);
            for (int f = 0; f < 5; f++) yield return null;
            s.Choose(s.CurrentTarget, "test");
            for (int f = 0; f < 5; f++) yield return null;
            Assert.True(s.OnBreak, "after the budget, the next trial becomes a rest break");
            StringAssert.Contains("Rest your eyes", s.Title.text);
            Assert.That(s.Patches.All(p => p.Arcsec == 0 && p.MotionArcsec == 0), "no creature during the break");
            s.Choose(0, "test");
            Assert.True(s.OnBreak, "pinching during the break does nothing");
            yield return new WaitForSecondsRealtime(0.7f);
            s.Choose(0, "test");
            Assert.True(s.AwaitingChoice, "after the break, a pinch starts a fresh session");
            s.CapSeconds = 600f;
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
