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
            Assert.True(s.AwaitingChoice, "one pinch accepts and starts the game");
            Assert.That(PlayerPrefs.GetInt(SpotSession.NoticeKey, 0), Is.EqualTo(1), "remembered for next launch");
            yield return null;
        }
    }
}
