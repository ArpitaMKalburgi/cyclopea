using System;
using System.Collections.Generic;
using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Leaves today's Depth Score on the desk where it was earned, as a persistent spatial anchor, with a
    /// small graph of the last two weeks' best scores under it (higher is finer depth). Next launch the
    /// card is still there, in the same spot in the room: "Last time: 83 arcsec. Beat it." It also keeps
    /// the daily streak. If the anchor can't be found (another room, a cleared space), the card simply
    /// doesn't appear; nothing else depends on it.
    /// </summary>
    public sealed class ScoreAnchor : MonoBehaviour
    {
        const string KeyUuid = "cyc_anchor_uuid", KeyText = "cyc_anchor_text", KeyDay = "cyc_anchor_day";
        const string KeyStreakDay = "cyc_streak_day", KeyStreak = "cyc_streak", KeyBest = "cyc_streak_best";

        public SpotSession Session;
        public Transform Head;
        public float SideOffsetM = 0.24f;
        [Tooltip("Material for the graph lines (the frames' unlit material).")]
        public Material LineMaterial;
        public float GraphWidthM = 0.15f, GraphHeightM = 0.045f;
        public int GraphDays = 14;

        /// <summary>The card's graph of daily bests (null before the first measured session). Test hook.</summary>
        public LineRenderer Graph { get; private set; }
        public TextMesh CardText { get; private set; }

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
            if (r.Measured && !r.PersonalBest && Session != null)
            {
                string best = Goals.BestLine(Session.Progress, r.Cue);
                if (best.Length > 0) text += "\n" + best;
            }
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

        GameObject MakeCard(string text)
        {
            var go = new GameObject("ScoreCard");
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, GraphHeightM + 0.012f, 0f);
            var tm = textGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = 56;
            tm.characterSize = 0.001f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.75f, 1f, 0.85f, 0.9f);
            tm.fontStyle = Options.HighContrast ? FontStyle.Bold : FontStyle.Normal;
            tm.text = text;
            textGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            if (Head != null)
            {
                var legible = textGo.AddComponent<LegibleText>();
                legible.Head = Head;
                legible.TargetDmm = 16f;
                legible.MaxWidthDeg = 24f;
            }
            CardText = tm;
            BuildGraph(go.transform);
            return go;
        }

        /// <summary>
        /// The last GraphDays days' best scores, oldest on the left; up is finer depth, on a log scale so each
        /// halving of the score is the same step. A faint baseline sits under it.
        /// </summary>
        void BuildGraph(Transform card)
        {
            Graph = null;
            if (Session == null || Session.Progress == null || LineMaterial == null) return;
            var days = Session.Progress.Recent(Session.Progress.MainCue, GraphDays);
            if (days.Count == 0) return;
            float w = GraphWidthM, h = GraphHeightM;
            MakeLine(card, "GraphBase", new[] { new Vector3(-w / 2, 0f, 0f), new Vector3(w / 2, 0f, 0f) },
                     new Color(1f, 1f, 1f, 0.25f), 0.0008f);
            Vector3[] pts;
            if (days.Count == 1)
            {
                float y = h * (float)ProgressHistory.GraphHeight(days[0].BestArcsec);
                pts = new[] { new Vector3(-0.004f, y, 0f), new Vector3(0.004f, y, 0f) };   // one day: a short dash
            }
            else
            {
                pts = new Vector3[days.Count];
                for (int i = 0; i < days.Count; i++)
                    pts[i] = new Vector3(-w / 2 + w * i / (days.Count - 1), h * (float)ProgressHistory.GraphHeight(days[i].BestArcsec), 0f);
            }
            Graph = MakeLine(card, "Graph", pts, new Color(0.75f, 1f, 0.85f, 0.95f), Options.HighContrast ? 0.0035f : 0.0022f);
        }

        LineRenderer MakeLine(Transform parent, string name, Vector3[] pts, Color c, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = c;
            lr.sharedMaterial = LineMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.numCapVertices = 2;
            return lr;
        }
    }
}
