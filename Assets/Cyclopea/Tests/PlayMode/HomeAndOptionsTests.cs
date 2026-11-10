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
    /// A returning player: the home card (streak, best, next goal) with the four squares as buttons, the
    /// options every input can reach, decoy trials, growth recorded across days, and text that stays
    /// inside the comfortable field of view.
    /// </summary>
    public class HomeAndOptionsTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";
        // Three earlier days, coarse to finer, and a field guide with Commons and one Uncommon.
        const string History = "2026-10-30,S,400.0,1;2026-11-01,S,240.0,2;2026-11-02,S,180.0,1";
        const string Guide = "3,1,0,0,2,0,0,0,0,0,0,0,1,0,0,0";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            PlayerPrefs.SetString(SpotSession.ProgressKey, History);
            PlayerPrefs.SetString(CreatureDirector.CollectionKey, Guide);
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

        [UnityTearDown]
        public IEnumerator Restore()
        {
            PlayerPrefs.DeleteKey(SpotSession.ProgressKey);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey); PlayerPrefs.DeleteKey(WhackRound.BestKey); PlayerPrefs.DeleteKey(WhackRound.LevelKey);
            PlayerPrefs.DeleteKey(SpotSession.CueKey);
            PlayerPrefs.DeleteKey(CreatureDirector.CollectionKey);
            Options.Reset();
            yield return null;
        }

        static SpotSession Session()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            Assert.NotNull(s);
            s.FeedbackSeconds = 0f;
            s.CatchFeedbackSeconds = 0f;
            s.RungIntroSeconds = 0f;
            Assert.NotNull(s.Home, "home card wired");
            s.Home.DebounceSeconds = 0f;
            return s;
        }

        static IEnumerator Play(SpotSession s, System.Func<SpotSession, int> choose, int guard = 3000)
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

        [UnityTest]
        public IEnumerator AReturningPlayerOpensOnTheHomeCardAndItsSquaresAreButtons()
        {
            var s = Session();
            Assert.True(s.ShowingHome, "a player with history starts at the home card");
            Assert.False(s.AwaitingChoice);
            StringAssert.StartsWith("Cyclopea", s.Title.text);
            StringAssert.Contains("Best 180″ (Nov 2)", s.Title.text);
            StringAssert.Contains("Next: a Rare creature. They hide below 120″.", s.Title.text);
            Assert.That(s.Patches.All(p => p.Arcsec == 0 && !p.Decoy), "nothing hides behind a menu");
            Assert.That(s.Patches.All(p => p.Brightness < 0.5f), "the static fades behind the labels");
            var labels = s.Home.Labels;
            Assert.That(labels.Select(l => l.text.Split('\n')[0]), Is.EqualTo(new[] { "Play", "Depth", "Field guide", "Options" }));
            Assert.That(labels.All(l => l.gameObject.activeSelf));
            StringAssert.Contains("4 of 16", labels[2].text);
            yield return null;

            // Every menu screen accepts look and hold, whatever the chosen input.
            Assert.True(s.WantsLookAndHold);
            var dev = Object.FindFirstObjectByType<DevControls>();
            Assert.True(dev.Dwell.enabled, "look and hold is live on the home card");

            s.Choose(2, "test");
            Assert.That(s.Home.Current, Is.EqualTo(HomeCard.Page.FieldGuide));
            StringAssert.Contains("Field guide: 4 of 16", s.Title.text);
            StringAssert.Contains("Trifoil   ● ● ○ ○", s.Title.text);
            s.Choose(0, "test");
            Assert.That(s.Home.Current, Is.EqualTo(HomeCard.Page.Home), "any square goes back");

            s.Choose(1, "test");
            Assert.That(s.Home.Current, Is.EqualTo(HomeCard.Page.Check), "Depth Check asks: two eyes or one");
            s.Choose(0, "test");
            Assert.True(s.AwaitingChoice, "two eyes starts the measured Depth Check");
            Assert.That(s.Cue, Is.EqualTo(SessionLog.Cue.Stereo));
            Assert.That(s.Patches.All(p => p.Brightness == 1f), "full static again");
            Assert.That(s.Home.Labels.All(l => !l.gameObject.activeSelf), "labels gone");
            yield return null;
            Assert.False(dev.Dwell.enabled, "and in play, look and hold is off again for a pinching player");
        }

        [UnityTest]
        public IEnumerator OptionsAreSquaresAndEveryChangeSticks()
        {
            var s = Session();
            var dev = Object.FindFirstObjectByType<DevControls>();
            s.Choose(3, "test");
            Assert.That(s.Home.Current, Is.EqualTo(HomeCard.Page.Options));
            Assert.That(s.Home.Labels.Select(l => l.text), Is.EqualTo(new[] { "Contrast\nStandard", "Choose by\nPinch", "Less\nmovement\nOff", "Back" }));

            s.Choose(0, "test");
            Assert.True(Options.HighContrast);
            Assert.That(StereoPatch.DotColor, Is.EqualTo(Color.white));
            Assert.That(StereoPatch.BackgroundColor, Is.EqualTo(Color.black));
            Assert.That(s.Home.Labels[0].text, Is.EqualTo("Contrast\nHigh"));
            float before = s.Frames[0].widthMultiplier;
            yield return null;
            Assert.That(s.Frames[0].widthMultiplier, Is.GreaterThan(before * 1.5f), "bolder frames");
            Assert.That(s.Title.fontStyle, Is.EqualTo(FontStyle.Bold), "bolder text");
            var mood = Object.FindFirstObjectByType<PassthroughMood>();
            Assert.That(mood.HighContrastBrightness, Is.LessThan(0f), "the room dims behind the static");

            s.Choose(1, "test");
            Assert.True(Options.LookAndHold);
            yield return null;
            Assert.That(DevControls.Mode, Is.EqualTo(DevControls.InputMode.Dwell));
            Assert.False(dev.Isdk.enabled, "one way to choose at a time in play");
            Assert.True(dev.Dwell.enabled);

            s.Choose(2, "test");
            Assert.True(Options.LessMovement);
            Assert.True(StereoPatch.SwayAssist, "motion mode sways for you");
            StringAssert.Contains("hc=1;look=1;less=1", PlayerPrefs.GetString(Options.Key), "kept for next launch");

            // A fresh load (next launch) reads them back.
            Options.HighContrast = Options.LookAndHold = Options.LessMovement = false;
            Options.Load();
            Assert.True(Options.HighContrast && Options.LookAndHold && Options.LessMovement);

            s.Choose(3, "test");
            Assert.That(s.Home.Current, Is.EqualTo(HomeCard.Page.Home));
            s.Choose(1, "test"); s.Choose(0, "test");   // Depth Check, two eyes
            Assert.True(s.AwaitingChoice);
            StringAssert.Contains("Look at it and hold", s.Title.text, "instructions follow the chosen input");
            Assert.That(s.Patches.Count(p => p.Arcsec > 0), Is.EqualTo(1));
            yield return null;
            Assert.True(dev.Dwell.enabled, "look and hold is the input in play");

            // Less movement: no reaching. A sharp player goes on to Sort, and Catch is skipped.
            yield return Play(s, x => x.CurrentTarget);
            Assert.True(s.IsDone);
            var lines = File.ReadAllLines(s.LogPath);
            Assert.That(lines.Count(l => l.Contains("\"rung\":\"Sort\"")), Is.GreaterThan(0), "Sort still runs");
            Assert.That(lines.Count(l => l.Contains("\"rung\":\"Catch\"")), Is.EqualTo(0), "no reaching game");
            Options.Reset();
            yield return null;
            Assert.That(DevControls.Mode, Is.EqualTo(DevControls.InputMode.Isdk), "back to pinch");
            Assert.That(StereoPatch.DotColor, Is.Not.EqualTo(Color.white));
        }

        [UnityTest]
        public IEnumerator CantPinchLookAndHoldJoinsInAfterAWhile()
        {
            var s = Session();
            var dev = Object.FindFirstObjectByType<DevControls>();
            s.IdleAssistSeconds = 0.3f;
            s.Choose(1, "test"); s.Choose(0, "test");   // Depth Check, two eyes
            Assert.True(s.AwaitingChoice);
            yield return null;
            Assert.False(dev.Dwell.enabled);
            yield return new WaitForSeconds(0.5f);
            Assert.True(s.DwellAssist, "nothing chosen for a while");
            StringAssert.Contains("Can't pinch?", s.Status.text);
            yield return null;
            Assert.True(dev.Dwell.enabled, "look and hold works now, alongside the pinch");
            Assert.True(dev.Isdk.enabled);
            s.IdleAssistSeconds = 25f;
        }

        [UnityTest]
        public IEnumerator DecoysAreFlatFakesThatNeverMoveTheStaircase()
        {
            var s = Session();
            s.DecoyChance = 1f;
            s.DecoyAfterReversals = 0;
            s.LadderRungs = false;
            try
            {
                s.Choose(1, "test"); s.Choose(0, "test");   // Depth Check, two eyes
                int guard = 0;
                while (s.CurrentDecoy < 0 && guard++ < 200)
                {
                    if (s.AwaitingChoice) s.Choose(s.CurrentTarget, "test");
                    yield return null;
                }
                Assert.That(s.CurrentDecoy, Is.GreaterThanOrEqualTo(0), "a decoy trial came up");
                int d = s.CurrentDecoy, t = s.CurrentTarget;
                Assert.That(d, Is.Not.EqualTo(t));
                Assert.True(s.Patches[d].Decoy);
                Assert.That(s.Patches[d].Arcsec, Is.EqualTo(0), "the fake is flat: no disparity at all");
                Assert.That(s.Patches[t].Arcsec, Is.GreaterThanOrEqualTo(960), "the real one floats clearly");
                Assert.That(s.Patches.Count(p => p.Decoy), Is.EqualTo(1));
                double level = s.CurrentArcsec;

                s.Choose(d, "test");
                StringAssert.Contains("A fake!", s.Status.text);
                Assert.That(s.CurrentArcsec, Is.EqualTo(level), "a decoy trial is a check, not a staircase step");
                var last = File.ReadAllLines(s.LogPath).Last();
                StringAssert.Contains("\"decoy\":true,\"decoy_picked\":true", last);
                StringAssert.Contains("\"phase\":\"Decoy\"", last);

                yield return Play(s, x => x.CurrentTarget);
                Assert.True(s.IsDone);
                StringAssert.Contains("Fakes ignored: ", s.Title.text);
                var lines = File.ReadAllLines(s.LogPath);
                int decoys = lines.Count(l => l.Contains("\"decoy\":true"));
                StringAssert.Contains("Fakes ignored: " + (decoys - 1) + " of " + decoys, s.Title.text);
            }
            finally
            {
                s.DecoyChance = 0.18f;
                s.DecoyAfterReversals = 2;
                s.LadderRungs = true;
            }
        }

        [UnityTest]
        public IEnumerator FinishingKeepsTheDayDrawsTheGraphAndSaysANewBest()
        {
            var s = Session();
            s.LadderRungs = false;
            s.DecoyChance = 0f;
            try
            {
                s.Choose(1, "test"); s.Choose(0, "test");   // Depth Check, two eyes
                yield return Play(s, x => x.CurrentTarget);   // sees everything: down to the display floor
                Assert.True(s.IsDone);
                StringAssert.Contains("New personal best!", s.Title.text);
                var saved = ProgressHistory.Parse(PlayerPrefs.GetString(SpotSession.ProgressKey));
                Assert.That(saved.Count, Is.EqualTo(4), "three earlier days and today");
                Assert.That(saved.Days.Last().Date, Is.EqualTo(System.DateTime.Now.ToString("yyyy-MM-dd")));

                var anchor = Object.FindFirstObjectByType<ScoreAnchor>();
                Assert.NotNull(anchor.Graph, "the card on the desk carries the graph");
                Assert.That(anchor.Graph.positionCount, Is.EqualTo(4));
                Assert.That(anchor.Graph.GetPosition(3).y, Is.GreaterThan(anchor.Graph.GetPosition(0).y), "finer depth draws higher");
                StringAssert.Contains("New personal best!", anchor.CardText.text);

                s.Choose(1, "test");
                Assert.True(s.ShowingHome, "after the score card, the home card");
                StringAssert.Contains("Best ", s.Title.text);
                StringAssert.DoesNotContain("Best 180", s.Title.text, "the new best replaced the old one");
            }
            finally
            {
                s.LadderRungs = true;
                s.DecoyChance = 0.18f;
            }
        }

        /// <summary>The largest angle from the gaze centre (head to grid centre) to a corner of a renderer's bounds.</summary>
        static float WidestDeg(SpotSession s, Renderer r)
        {
            Vector3 head = s.Head.position;
            Vector3 centre = s.Patches.Aggregate(Vector3.zero, (a, p) => a + p.transform.position) / s.Patches.Length;
            Vector3 gaze = (centre - head).normalized;
            var b = r.bounds;
            float worst = 0;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                worst = Mathf.Max(worst, Vector3.Angle(gaze, c - head));
            }
            return worst;
        }

        /// <summary>Type size as seen from the head, in mm per metre (Meta: 14 minimum, 18+ comfortable).</summary>
        static float TypeDmm(SpotSession s, TextMesh t) =>
            0.1f * t.fontSize * t.characterSize * t.transform.lossyScale.y * 1000f /
            Vector3.Distance(s.Head.position, t.transform.position);

        [UnityTest]
        public IEnumerator EveryTitleAndLabelStaysInsideTheComfortableView()
        {
            const float limit = 27f;   // half of the Meta VR Glasses design area's 54° height; Quest 3 is far wider
            const float minDmm = 14f;  // Meta typography: smallest legible type
            var s = Session();
            var r = s.Title.GetComponent<Renderer>();
            var texts = new System.Collections.Generic.List<(string name, System.Action show)>
            {
                ("notice", () => s.SetTitle(SpotSession.NoticeText)),
                ("home", () => s.Home.Show(HomeCard.Page.Home)),
                ("options", () => s.Home.Show(HomeCard.Page.Options)),
                ("field guide", () => s.Home.Show(HomeCard.Page.FieldGuide)),
                ("round 1", () => s.SetTitle("One square hides a creature.\nOnly two eyes can see it. Look at it and hold.")),
                ("long score card", () => s.SetTitle("Depth Score\nSpot: ≤ 15″ (display limit) — a 0.4 mm step at 60 cm\n" +
                                                     "New personal best!\nSort: 30″  ·  Catch: 7 of 8, within 6 mm  ·  Fakes ignored: 6 of 6\n" +
                                                     "You found 61 creatures. Field guide: 12 of 16.\nNew: Elusive Starfin and 2 more")),
            };
            var report = new System.Text.StringBuilder();
            foreach (var (name, show) in texts)
            {
                show();
                yield return null;
                yield return null;   // sized in LateUpdate, measured once the mesh follows
                float deg = WidestDeg(s, r);
                float dmm = TypeDmm(s, s.Title);
                report.Append(name).Append(' ').Append(deg.ToString("0.0")).Append("° ").Append(dmm.ToString("0")).Append(" dmm  ");
                Debug.Log("[Cyclopea test] " + name + ": " + deg.ToString("0.0") + "° from gaze centre, type " + dmm.ToString("0.0") +
                          " dmm, " + (r.bounds.size.x * 100).ToString("0.0") + " × " + (r.bounds.size.y * 100).ToString("0.0") + " cm at " +
                          Vector3.Distance(s.Head.position, s.Title.transform.position).ToString("0.00") + " m");
                Assert.That(r.bounds.size.magnitude, Is.GreaterThan(0.01f), name + ": the text was measured");
                Assert.That(deg, Is.LessThanOrEqualTo(limit), name + " reaches " + deg.ToString("0.0") + "° from the gaze centre");
                Assert.That(dmm, Is.GreaterThanOrEqualTo(minDmm - 0.1f), name + " type is " + dmm.ToString("0.0") + " dmm, below Meta's minimum");
                Assert.That(r.bounds.min.y, Is.GreaterThan(s.Patches[0].transform.position.y - 0.005f), name + " sinks into the desk");
            }
            // The status line under the grid, with the longest toast it carries.
            var sr = s.Status.GetComponent<Renderer>();
            s.Toast("Something is very close in front of you.\nTurn toward open space.", 5f);
            yield return null;
            yield return null;
            float sdeg = WidestDeg(s, sr);
            float sdmm = TypeDmm(s, s.Status);
            s.Toast("Can't pinch? Look at a square and hold your gaze on it.", 5f);
            yield return null;
            yield return null;
            sdeg = Mathf.Max(sdeg, WidestDeg(s, sr));
            sdmm = Mathf.Min(sdmm, TypeDmm(s, s.Status));
            report.Append("status ").Append(sdeg.ToString("0.0")).Append("° ").Append(sdmm.ToString("0")).Append(" dmm  ");
            Assert.That(sdeg, Is.LessThanOrEqualTo(limit), "status line");
            Assert.That(sdmm, Is.GreaterThanOrEqualTo(minDmm - 0.1f), "status type");

            s.Home.Show(HomeCard.Page.Options);
            yield return null;
            yield return null;
            for (int i = 0; i < 4; i++)
            {
                var lb = s.Home.Labels[i].GetComponent<Renderer>().bounds;
                var pb = s.Patches[i].GetComponent<Renderer>().bounds;
                float ldmm = TypeDmm(s, s.Home.Labels[i]);
                report.Append("label").Append(i).Append(' ').Append((lb.size.x * 100).ToString("0.0")).Append(" cm ")
                      .Append(ldmm.ToString("0")).Append(" dmm  ");
                Assert.That(lb.size.x, Is.LessThanOrEqualTo(pb.size.x * 1.02f), "label " + i + " fits its square");
                Assert.That(ldmm, Is.GreaterThanOrEqualTo(minDmm - 0.1f), "label " + i + " type");
            }
            Debug.Log("[Cyclopea test] widest text from gaze centre: " + report);
        }
    }
}
