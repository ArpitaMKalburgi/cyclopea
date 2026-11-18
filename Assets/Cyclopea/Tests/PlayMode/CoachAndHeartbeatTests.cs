using System.Collections;
using System.Linq;
using Cyclopea.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cyclopea.PlayTests
{
    /// <summary>The see-through coach hand and the heartbeat (both ported from Worldeck).</summary>
    public class CoachAndHeartbeatTests
    {
        const string Scene = "Assets/Cyclopea/Scenes/Cyclopea.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
            PlayerPrefs.SetInt(SpotSession.NoticeKey, 1);
            PlayerPrefs.SetInt(SpotSession.TutorialKey, 1);
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            PlayerPrefs.DeleteKey(WhackRound.BestKey);
            PlayerPrefs.DeleteKey(WhackRound.LevelKey);
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
            var arcade = Object.FindFirstObjectByType<WhackRound>();
            if (arcade != null) arcade.Abort();
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            Options.Reset();
            Health.Reset();
        }

        [UnityTest]
        public IEnumerator TheCoachSlapsInTheCountdownAndLeavesWhenPlayStarts()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var coach = Object.FindFirstObjectByType<SlapCoach>();
            Assert.NotNull(coach, "coach wired");
            s.Arcade.CountdownSeconds = 1.2f;
            s.Arcade.RoundSeconds = 5f;
            s.StartArcade();
            yield return null; yield return null;
            Assert.True(s.Arcade.Counting);
            Assert.AreEqual("slap", coach.Mode, "a newer player sees the slap before any critter is up");
            float until = Time.realtimeSinceStartup + 4f;
            while (!s.Arcade.Playing && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            Assert.True(s.Arcade.Playing);
            Assert.False(coach.Showing, "gone once critters can appear: it never hints where one is");
        }

        [UnityTest]
        public IEnumerator TheCoachStaysAwayForLookAndHoldAndForExperiencedPlayers()
        {
            var s = Object.FindFirstObjectByType<SpotSession>();
            var coach = Object.FindFirstObjectByType<SlapCoach>();
            s.Arcade.CountdownSeconds = 1.2f;
            PlayerPrefs.SetInt(WhackRound.TotalKey, 500);
            s.StartArcade();
            yield return null; yield return null;
            Assert.True(s.Arcade.Counting);
            Assert.False(coach.Showing, "500 caught: no lesson");
            s.Arcade.Abort();
            PlayerPrefs.DeleteKey(WhackRound.TotalKey);
            Options.LookAndHold = true; Options.Save();
            s.StartArcade();
            yield return null; yield return null;
            Assert.False(coach.Showing, "a look-and-hold player isn't shown a slap");
        }

        [UnityTest]
        public IEnumerator TheHeartbeatWritesWhatTheAppIsDoing()
        {
            var beat = Object.FindFirstObjectByType<Heartbeat>();
            var log = Object.FindFirstObjectByType<PlayLog>();
            Assert.NotNull(beat, "heartbeat wired");
            log.WriteFiles = false;
            beat.Seconds = 0.2f;
            yield return new WaitForSeconds(0.7f);
            var beats = log.Written.Where(l => l.StartsWith("{\"type\":\"beat\"")).ToList();
            Assert.GreaterOrEqual(beats.Count, 1, string.Join("\n", log.Written));
            StringAssert.Contains("\"fps\":", beats[0]);
            StringAssert.Contains("\"state\":\"", beats[0]);
            StringAssert.Contains("\"mem_mb\":", beats[0]);
            StringAssert.Contains("\"errors\":", beats[0]);
        }
    }
}
