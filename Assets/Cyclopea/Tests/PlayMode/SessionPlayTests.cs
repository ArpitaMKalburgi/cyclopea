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
            PlayerPrefs.DeleteKey(SpotSession.ProgressKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey); PlayerPrefs.DeleteKey(WhackRound.BestKey); PlayerPrefs.DeleteKey(WhackRound.LevelKey);  // ...and with no history, so play starts at once (no home card)
            PlayerPrefs.DeleteKey(SpotSession.CueKey);
            Options.Reset();
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
            s.CatchFeedbackSeconds = 0f;
            s.RungIntroSeconds = 0f;
            return s;
        }

        /// <summary>Plays to the end: a pinch for Spot and Sort, a touch at the creature's height for Catch.</summary>
        static IEnumerator Play(SpotSession s, System.Func<SpotSession, int> choose, int guard = 2000)
        {
            int n = 0;
            while (!s.IsDone && n++ < guard)
            {
                if (s.AwaitingChoice)
                {
                    int i = choose(s);
                    if (s.CurrentRung == SkillLadder.Rung.Catch) s.CatchAt(i, s.CatchHeightM, "test");
                    else s.Choose(i, "test");
                }
                yield return null;
            }
        }

        /// <summary>The point at <paramref name="heightM"/> above the surface on the sightline from the head to the outline.</summary>
        static Vector3 SeenPoint(SpotSession s, StereoPatch p, double heightM)
        {
            Vector3 m = p.transform.TransformPoint(new Vector3(p.MaskCenter.x - 0.5f, p.MaskCenter.y - 0.5f, 0f));
            float hH = Vector3.Dot(s.Head.position - m, -p.transform.forward);
            return m + (s.Head.position - m) * (float)(heightM / hH);
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
                if (x.CurrentRung == SkillLadder.Rung.Sort) return x.SortOther;   // took the farther one for the nearer
                int wrong = rng.Next(0, 3);
                return wrong >= x.CurrentTarget ? wrong + 1 : wrong;
            });
            Assert.True(s.IsDone, "session should finish");
            Assert.True(File.Exists(s.LogPath), "log file " + s.LogPath);
            var lines = File.ReadAllLines(s.LogPath);
            Assert.That(lines.Count(l => l.Contains("\"type\":\"trial\"") && l.Contains("\"rung\":\"Spot\"")), Is.GreaterThan(15));
            Assert.That(lines.Count(l => l.Contains("\"mode\":\"TwoAFC\"") && l.Contains("\"rung\":\"Sort\"")), Is.GreaterThan(5),
                "a 60\u2033 observer goes on to Sort");
            Assert.That(lines.Count(l => l.Contains("\"mode\":\"ReachToDepth\"") && l.Contains("\"depth_err_m\":")), Is.GreaterThan(2),
                "and Catch, with the depth error logged");
            var summaries = lines.Where(l => l.Contains("\"type\":\"summary\"")).ToArray();
            Assert.That(summaries.Select(l => l.Contains("\"rung\":\"Spot\"") ? "Spot" : l.Contains("\"rung\":\"Sort\"") ? "Sort" : "Catch"),
                Is.EqualTo(new[] { "Spot", "Sort", "Catch" }), "one summary per rung, Spot first");
            foreach (var sm in summaries) StringAssert.DoesNotContain("NaN", sm);
            StringAssert.Contains("Depth Score", s.Title.text);
            StringAssert.Contains("Sort: ", s.Title.text);
            StringAssert.Contains("Catch: ", s.Title.text);
            var summary = summaries[0];
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

            // It comes out where it was seen floating: on the sightline, at the height the disparity showed.
            var mesh = c.GetComponentInChildren<MeshFilter>().sharedMesh;
            var body = c.GetComponentInChildren<MeshFilter>().transform;
            Vector3 n = -p.transform.forward, H = s.Head.position;
            float hH = Vector3.Dot(H - p.transform.position, n);
            double shown = System.Math.Max(level, 1920);
            float expectH = (float)Reach.HeightForShift(DisparityMath.ShiftMeters(shown, p.LastViewingDistance), hH, StereoPatch.Ipd);
            Assert.That(Vector3.Dot(c.transform.position - p.transform.position, n), Is.EqualTo(expectH).Within(0.002f),
                "it floats at the height it was seen");
            Assert.That(expectH, Is.GreaterThan(0.01f));

            // Seen from the head, its footprint covers exactly the outline that floated (square UV).
            float worst = 0f;
            for (int i = 0; i < 48; i++)
            {
                Vector3 v = body.TransformPoint(mesh.vertices[i]);
                float hv = Vector3.Dot(v - p.transform.position, n);
                Vector3 q = H + (v - H) * (hH / (hH - hv));
                Vector3 local = p.transform.InverseTransformPoint(q);
                worst = Mathf.Max(worst, Mathf.Abs(MaskSdf(p, new Vector2(local.x + 0.5f, local.y + 0.5f))));
            }
            Assert.That(worst, Is.LessThan(2e-3f), "the creature's outline is the shape hidden in the static (UV, from the eyes)");

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
            Assert.True(s.ShowingHome, "after the break, a pinch goes to the home card");
            s.CapSeconds = 600f;
            yield return new WaitForSecondsRealtime(0.4f);   // the card ignores a second pick within 0.35 s
            s.Choose(1, "test");
            yield return new WaitForSecondsRealtime(0.4f);
            s.Choose(0, "test");
            Assert.True(s.AwaitingChoice, "and the budget starts again");
        }

        [UnityTest]
        public IEnumerator SortShowsTwoCreaturesAndTheNearerIsTheAnswer()
        {
            var s = Session();
            var d = Director();
            s.StartAtRung(SkillLadder.Rung.Sort);
            StringAssert.Contains("Two creatures", s.Title.text);
            float t0 = Time.time;
            while (!s.AwaitingChoice && Time.time - t0 < 2f) yield return null;
            Assert.That(s.CurrentRung, Is.EqualTo(SkillLadder.Rung.Sort));
            Assert.That(s.Patches.Count(p => p.Arcsec > 0), Is.EqualTo(2), "two creatures hide");
            Assert.That(s.SortOther, Is.Not.EqualTo(s.CurrentTarget));
            var near = s.Patches[s.CurrentTarget];
            var far = s.Patches[s.SortOther];
            Assert.That(near.Arcsec - far.Arcsec, Is.EqualTo(s.CurrentArcsec).Within(1e-6), "the level is the depth difference");
            Assert.That(far.Arcsec, Is.InRange(s.SortBaseMin, s.SortBaseMax));

            int empty = Enumerable.Range(0, 4).First(i => i != s.CurrentTarget && i != s.SortOther);
            s.Choose(empty, "test");
            Assert.That(s.TrialsDone, Is.EqualTo(0), "an empty square is not an answer");
            int before = d.Collection.Total;
            s.Choose(s.CurrentTarget, "test");
            Assert.That(s.TrialsDone, Is.EqualTo(1));
            Assert.That(d.Collection.Total, Is.EqualTo(before + 1), "the nearer one is found");
            Assert.That(d.Alive, Is.EqualTo(2), "it comes out, and the farther one peeks");
            var line = File.ReadAllLines(s.LogPath).Last(l => l.Contains("\"type\":\"trial\""));
            StringAssert.Contains("\"rung\":\"Sort\"", line);
            StringAssert.Contains("\"mode\":\"TwoAFC\"", line);
            s.StartAllRungs();
        }

        [UnityTest]
        public IEnumerator CatchItByTouchingItsBackWithAFingertip()
        {
            var s = Session();
            var d = Director();
            var tip = new GameObject("SimulatedTip").transform;
            tip.position = new Vector3(0f, -50f, 0f);   // hand far away at first
            d.Tips.SimulatedTip = tip;
            s.CatchRearmM = 0.02f;
            try
            {
                s.StartAtRung(SkillLadder.Rung.Catch);
                StringAssert.Contains("Touch its back", s.Title.text);
                float t0 = Time.time;
                while (!s.AwaitingChoice && Time.time - t0 < 2f) yield return null;
                Assert.That(s.CurrentRung, Is.EqualTo(SkillLadder.Rung.Catch));
                var p = s.Patches[s.CurrentTarget];
                Assert.That(s.Patches.Count(q => q.HoldHeightM > 0), Is.EqualTo(1), "one creature, held at a height");
                Assert.That(p.HoldHeightM, Is.EqualTo(s.CatchHeightM).Within(1e-9));
                Assert.That(s.CatchHeightM, Is.InRange(0.02, 0.10), "1920\u2033 at arm's length is a few centimetres");
                yield return null;   // LateUpdate presents it
                Assert.That(p.LastShiftM, Is.EqualTo(Reach.ShiftForHeight(s.CatchHeightM, p.LastEyeHeight, StereoPatch.Ipd)).Within(1e-9),
                    "the shift shows it at that height from where the eyes are");

                // Touch it where it is seen and hold still.
                int caughtBefore = d.Collection.Total;
                tip.position = SeenPoint(s, p, s.CatchHeightM);
                t0 = Time.time;
                while (s.TrialsDone == 0 && Time.time - t0 < 2f) yield return null;
                Assert.That(s.TrialsDone, Is.EqualTo(1), "holding still on it is a catch");
                Assert.That(Time.time - t0, Is.GreaterThan(0.4f), "only after holding still, not on first contact");
                StringAssert.Contains("Caught it", s.Status.text);
                Assert.That(d.Collection.Total, Is.EqualTo(caughtBefore + 1));
                t0 = Time.time;
                while (!d.Riding && Time.time - t0 < 3f) yield return null;
                Assert.True(d.Riding, "it climbs onto the finger that caught it");

                // The next one: touch well above it.
                t0 = Time.time;
                while (!s.AwaitingChoice && Time.time - t0 < 2f) yield return null;
                var p2 = s.Patches[s.CurrentTarget];
                tip.position = SeenPoint(s, p2, s.CatchHeightM + 0.08);
                t0 = Time.time;
                while (s.TrialsDone == 1 && Time.time - t0 < 2f) yield return null;
                Assert.That(s.TrialsDone, Is.EqualTo(2));
                StringAssert.Contains("lower", s.Status.text, "too high: the hint says which way");
                var line = File.ReadAllLines(s.LogPath).Last(l => l.Contains("\"type\":\"trial\""));
                StringAssert.Contains("\"rung\":\"Catch\"", line);
                StringAssert.Contains("\"mode\":\"ReachToDepth\"", line);
                StringAssert.Contains("\"correct\":false", line);
                StringAssert.Contains("\"depth_err_m\":0.0", line);
            }
            finally
            {
                d.Tips.SimulatedTip = null;
                Object.Destroy(tip.gameObject);
                s.StartAllRungs();
            }
        }

        [UnityTest]
        public IEnumerator SharpEyesGoOnToSortAndCatchIsSkippedWithoutHands()
        {
            var s = Session();
            s.CatchHandWaitSeconds = 0.3f;
            s.Restart();
            bool sawSort = false, sawCatch = false;
            int n = 0;
            while (!s.IsDone && n++ < 2000)
            {
                if (s.AwaitingChoice)
                {
                    if (s.CurrentRung == SkillLadder.Rung.Sort) sawSort = true;
                    if (s.CurrentRung == SkillLadder.Rung.Catch) sawCatch = true;   // no fingertip: it waits, then skips
                    else s.Choose(s.CurrentTarget, "test");
                }
                yield return null;
            }
            Assert.True(s.IsDone);
            Assert.True(sawSort, "a fine Spot leads into Sort");
            Assert.True(sawCatch, "then Catch starts");
            StringAssert.Contains("Depth Score", s.Title.text);
            StringAssert.Contains("Sort: ", s.Title.text);
            StringAssert.DoesNotContain("Catch:", s.Title.text, "no hands: Catch is skipped, not failed");
            var lines = File.ReadAllLines(s.LogPath);
            Assert.That(lines.Count(l => l.Contains("\"type\":\"summary\"") && l.Contains("\"rung\":\"Spot\"")), Is.EqualTo(1));
            Assert.That(lines.Count(l => l.Contains("\"type\":\"summary\"") && l.Contains("\"rung\":\"Sort\"")), Is.EqualTo(1));
            Assert.That(lines.Count(l => l.Contains("\"rung\":\"Catch\"")), Is.EqualTo(0), "nothing logged for a skipped Catch");
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
