using System;
using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Leaves today's Depth Score on the desk where it was earned, as a persistent spatial anchor.
    /// Next launch the card is still there, in the same spot in the room: "Last time: 83 arcsec.
    /// Beat it." It also keeps the daily streak. If the anchor can't be found (another room, a
    /// cleared space), the card simply doesn't appear; nothing else depends on it.
    /// </summary>
    public sealed class ScoreAnchor : MonoBehaviour
    {
        const string KeyUuid = "cyc_anchor_uuid", KeyText = "cyc_anchor_text", KeyDay = "cyc_anchor_day";
        const string KeyStreakDay = "cyc_streak_day", KeyStreak = "cyc_streak", KeyBest = "cyc_streak_best";

        public SpotSession Session;
        public Transform Head;
        public float SideOffsetM = 0.24f;

        public string Status { get; private set; } = "idle";
        public Streak.State StreakState { get; private set; }
        public event Action<Streak.State> StreakUpdated;

        GameObject card;

        void OnEnable() { if (Session != null) Session.Finished += OnFinished; }
        void OnDisable() { if (Session != null) Session.Finished -= OnFinished; }

        async void Start()
        {
            StreakState = new Streak.State
            {
                LastDay = PlayerPrefs.GetString(KeyStreakDay, ""),
                Count = PlayerPrefs.GetInt(KeyStreak, 0),
                Best = PlayerPrefs.GetInt(KeyBest, 0)
            };
            if (!Guid.TryParse(PlayerPrefs.GetString(KeyUuid, ""), out var uuid)) { Status = "no saved card"; return; }
            try
            {
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (!load.Success || unbound.Count == 0) { Status = "card not in this space"; return; }
                var u = unbound[0];
                if (!await u.LocalizeAsync()) { Status = "card not localized"; return; }
                string day = PlayerPrefs.GetString(KeyDay, "");
                card = MakeCard("Last time" + (day.Length > 0 ? " (" + day + ")" : "") + "\n" +
                                PlayerPrefs.GetString(KeyText, "") + "\nBeat it.");
                u.BindTo(card.AddComponent<OVRSpatialAnchor>());
                Status = "restored " + uuid;
            }
            catch (Exception e) { Status = "load failed: " + e.Message; }
        }

        async void OnFinished(SpotSession.Result r)
        {
            StreakState = Streak.Update(StreakState, DateTime.Now);
            PlayerPrefs.SetString(KeyStreakDay, StreakState.LastDay);
            PlayerPrefs.SetInt(KeyStreak, StreakState.Count);
            PlayerPrefs.SetInt(KeyBest, StreakState.Best);
            PlayerPrefs.Save();
            StreakUpdated?.Invoke(StreakState);

            if (Session == null || Session.Surface == null || Head == null) return;
            string text = r.Measured ? r.ScoreLine : "no stereo measured";
            string old = PlayerPrefs.GetString(KeyUuid, "");
            if (card != null) Destroy(card);

            var s = Session.Surface.Current;
            float step = (Session.PatchSize + Session.Gap) * 0.5f;
            Vector3 pos = s.Center + s.Right * (step * 2f + SideOffsetM * 0.5f) + s.Normal * 0.02f;
            card = MakeCard("Today\n" + text + "\nDay " + StreakState.Count + " in a row");
            card.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Head.position, Vector3.up));
#if UNITY_EDITOR
            Status = "editor: anchors off";
            await System.Threading.Tasks.Task.CompletedTask;
#else
            try
            {
                var anchor = card.AddComponent<OVRSpatialAnchor>();
                if (!await anchor.WhenLocalizedAsync()) { Status = "anchor not localized"; return; }
                var save = await anchor.SaveAnchorAsync();
                if (!save.Success) { Status = "save failed"; return; }
                PlayerPrefs.SetString(KeyUuid, anchor.Uuid.ToString());
                PlayerPrefs.SetString(KeyText, text);
                PlayerPrefs.SetString(KeyDay, DateTime.Now.ToString("MMM d"));
                PlayerPrefs.Save();
                Status = "saved " + anchor.Uuid;
                if (Guid.TryParse(old, out var oldId) && oldId != anchor.Uuid)
                    await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { oldId });
            }
            catch (Exception e) { Status = "save threw: " + e.Message; }
#endif
        }

        static GameObject MakeCard(string text)
        {
            var go = new GameObject("ScoreCard");
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = 56;
            tm.characterSize = 0.0007f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.75f, 1f, 0.85f, 0.9f);
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return go;
        }
    }
}
