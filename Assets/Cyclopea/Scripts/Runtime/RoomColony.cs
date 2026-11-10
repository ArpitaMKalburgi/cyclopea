using System;
using System.Collections;
using System.Collections.Generic;
using Cyclopea.Core;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Every new kind of creature you find moves into your room. When the score card comes up, it hops
    /// off the nest to a real surface the room scan found (a shelf, the sofa, the other end of the desk)
    /// and it is still there tomorrow: the place is saved against that scene anchor. Without a scan they
    /// live on the desk beside the squares. Residents breathe, hop now and then, and chirp when you look
    /// at them. The room itself shows the collection growing between sessions.
    /// </summary>
    public sealed class RoomColony : MonoBehaviour
    {
        public const string ColonyKey = "cyc_colony_v1";

        public SpotSession Session;
        public CreatureDirector Creatures;
        public SurfaceFinder Surface;
        public Transform Head;
        public Material Material;

        [Header("Where they live")]
        public Rules Placement = Rules.Default;
        public float Scale = 0.55f;

        [Header("Timing")]
        public float MoveInDelaySeconds = 1.6f, MoveInStaggerSeconds = 0.7f, MoveInHopSeconds = 1.4f;
        [Tooltip("Launch: wait this long for the room scan before placing residents (the desk otherwise).")]
        public float WaitForSceneSeconds = 6f;
        [Tooltip("Look at a resident this long and it chirps (at most every ChirpEverySeconds).")]
        public float GazeChirpSeconds = 0.5f, ChirpEverySeconds = 8f;

        [Serializable]
        public struct Rules
        {
            public float MinDistanceM, MaxDistanceM, MaxYawDeg, MaxAboveEyeM, ClearOfGridM, SpacingM, EdgeInsetM;
            public static Rules Default => new Rules
            {
                MinDistanceM = 0.45f, MaxDistanceM = 3.2f, MaxYawDeg = 70f, MaxAboveEyeM = 0.4f,
                ClearOfGridM = 0.08f, SpacingM = 0.12f, EdgeInsetM = 0.06f
            };
        }

        public Colony Colony { get; private set; } = new Colony();
        public bool Spawned { get; private set; }
        public int Living => homes.Count;
        public int Pending => pending.Count;
        public int MovingIn { get; private set; }
        public string LastPlacement { get; private set; } = "";

        sealed class Home
        {
            public Colony.Resident R;
            public CreatureBody Body;
            public Vector3 Pos, Up;
            public float NextHop, Phase, GazeSince = -1f, LastChirp = -99f, HopStart = -1f;
            public bool Arriving;
        }

        readonly List<Home> homes = new List<Home>();
        readonly List<Colony.Resident> pending = new List<Colony.Resident>();
        readonly System.Random rng = new System.Random();
        bool surfaceReady;

        static readonly MRUKAnchor.SceneLabels Perches =
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.FLOOR;

        // ---------- Life cycle ----------

        void Awake()
        {
            try { Colony = Colony.Parse(PlayerPrefs.GetString(ColonyKey, "")); }
            catch (Exception) { Colony = new Colony(); }
        }

        void OnEnable()
        {
            if (Creatures != null) Creatures.Collected += OnCollected;
            if (Session != null) { Session.Finished += OnFinished; Session.ArcadeFinished += OnArcadeFinished; }
            if (Surface != null) Surface.SurfaceChanged += OnSurfaceChanged;
        }

        void OnDisable()
        {
            if (Creatures != null) Creatures.Collected -= OnCollected;
            if (Session != null) { Session.Finished -= OnFinished; Session.ArcadeFinished -= OnArcadeFinished; }
            if (Surface != null) Surface.SurfaceChanged -= OnSurfaceChanged;
        }

        IEnumerator Start()
        {
            float t0 = Time.time;
            // Residents appear once the squares have a place and the room scan is in (or clearly isn't coming).
            while (!Spawned && (!surfaceReady || (Surface != null && !Surface.SceneLoaded && Time.time - t0 < WaitForSceneSeconds)))
                yield return null;
            if (!Spawned) SpawnAll();
        }

        void OnSurfaceChanged(SurfaceFinder.Surface s)
        {
            surfaceReady = true;
            // Desk residents move with the squares.
            foreach (var h in homes)
                if (h.R.OnDesk && !h.Arriving) { DeskPose((int)Math.Round(h.R.X), out h.Pos, out h.Up); Seat(h); }
        }

        /// <summary>Places every saved resident now (tests; also what launch does once the scene is in).</summary>
        public void SpawnAll()
        {
            Spawned = true;
            foreach (var h in homes) if (h.Body != null) Destroy(h.Body.gameObject);
            homes.Clear();
            var room = CurrentRoom();
            bool moved = false;
            foreach (var r in Colony.Residents)
            {
                if (!Resolve(r, room, out var pos, out var up))
                {
                    // Its spot is gone (another room, a new scan): it finds a new one.
                    Rehome(r, room, out pos, out up);
                    moved = true;
                }
                var h = new Home { R = r, Body = MakeBody(r), Pos = pos, Up = up };
                h.Phase = (float)rng.NextDouble() * 6.28f;
                h.NextHop = Time.time + 4f + (float)rng.NextDouble() * 10f;
                Seat(h);
                homes.Add(h);
            }
            if (moved) Save();
        }

        // ---------- New kinds move in ----------

        void OnCollected(Core.Creatures.Species s, Core.Creatures.Rarity r, bool isNew)
        {
            if (!isNew || Colony.Has(s, r) || pending.Exists(p => p.Species == s && p.Rarity == r)) return;
            var body = Creatures != null ? Creatures.Latest : null;
            var res = new Colony.Resident { Species = s, Rarity = r, Day = DateTime.Now.ToString("yyyy-MM-dd") };
            if (body != null && body.Species == s && body.Rarity == r)
            {
                res.Radius = body.RadiusM; res.Wobble = body.Wobble; res.Lobes = body.Lobes; res.Seed = body.Seed;
            }
            else
            {
                res.Radius = 0.02; res.Wobble = 0.15; res.Lobes = Core.Creatures.Lobes(s); res.Seed = rng.Next(0, 1000);
            }
            pending.Add(res);
        }

        void OnFinished(SpotSession.Result result) => MoveInNow();
        void OnArcadeFinished(WhackRound.Result result) => MoveInNow();

        /// <summary>Every waiting new kind moves in now (what the score card does; tests call it directly).</summary>
        public void MoveInNow()
        {
            if (pending.Count > 0 && !movingInRunning) StartCoroutine(MoveIn());
        }

        bool movingInRunning;

        IEnumerator MoveIn()
        {
            movingInRunning = true;
            try
            {
                if (MoveInDelaySeconds > 0f) yield return new WaitForSeconds(MoveInDelaySeconds);
                yield return MoveInEach();
            }
            finally { movingInRunning = false; }
        }

        IEnumerator MoveInEach()
        {
            var room = CurrentRoom();
            while (pending.Count > 0)
            {
                var r = pending[0];
                pending.RemoveAt(0);
                if (!Colony.Add(r)) continue;
                Rehome(r, room, out var pos, out var up);
                Save();

                // It leaves the nest (or the middle of the squares) in one long hop.
                Vector3 from; Quaternion fromRot;
                if (Creatures == null || !Creatures.TakeFromNest(r.Species, r.Rarity, out from, out fromRot))
                {
                    from = GridCenter(out var n) + n * 0.01f;
                    fromRot = Quaternion.LookRotation(Flat(HeadPos() - from), Vector3.up);
                }
                var h = new Home { R = r, Body = MakeBody(r), Pos = pos, Up = up, Arriving = true };
                h.Body.transform.SetPositionAndRotation(from, fromRot);
                h.Body.transform.localScale = Vector3.one * Scale;
                h.Phase = (float)rng.NextDouble() * 6.28f;
                h.NextHop = Time.time + 6f + (float)rng.NextDouble() * 10f;
                homes.Add(h);
                MovingIn++;
                StartCoroutine(HopHome(h, from));
                yield return new WaitForSeconds(MoveInStaggerSeconds);
            }
        }

        IEnumerator HopHome(Home h, Vector3 from)
        {
            if (Creatures != null && Creatures.Sounds != null) Creatures.SayAt(Creatures.Sounds.CreatureHop, from);
            float dist = Vector3.Distance(from, h.Pos);
            float height = 0.12f + 0.2f * Mathf.Min(dist, 2.5f);
            float t0 = Time.time;
            while (h.Body != null)
            {
                float u = Mathf.Clamp01((Time.time - t0) / MoveInHopSeconds);
                float e = u * u * (3f - 2f * u);
                Vector3 p = Vector3.Lerp(from, h.Pos, e) + Vector3.up * (4f * u * (1f - u) * height);
                Vector3 dir = Flat(h.Pos - from);
                h.Body.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward, Vector3.up));
                h.Body.Squash = u < 0.1f ? -0.35f : u > 0.92f ? 0.3f : 0f;
                if (u >= 1f) break;
                yield return null;
            }
            h.Arriving = false;
            MovingIn--;
            if (h.Body == null) yield break;
            Seat(h);
            if (Creatures != null && Creatures.Sounds != null)
                Creatures.SayAt(Creatures.Sounds.Chirp((int)h.R.Species), h.Pos);
        }

        void Save()
        {
            try { PlayerPrefs.SetString(ColonyKey, Colony.Serialize()); PlayerPrefs.Save(); }
            catch (Exception e) { Debug.LogWarning("[Cyclopea] colony save failed: " + e.Message); }
        }

        // ---------- Everyday life ----------

        void Update()
        {
            Vector3 head = HeadPos();
            Vector3 gaze = Head != null ? Head.forward : Vector3.forward;
            foreach (var h in homes)
            {
                if (h.Body == null || h.Arriving) continue;
                float t = Time.time;
                // Breathing, and a little hop in place now and then.
                float hop = 0f;
                if (h.HopStart < 0f && t >= h.NextHop) { h.HopStart = t; }
                if (h.HopStart >= 0f)
                {
                    float u = (t - h.HopStart) / 0.38f;
                    if (u >= 1f) { h.HopStart = -1f; h.NextHop = t + 6f + (float)rng.NextDouble() * 14f; }
                    else hop = 4f * u * (1f - u) * 0.025f;
                }
                h.Body.transform.position = h.Pos + h.Up * hop;
                h.Body.Squash = hop > 0f ? -0.2f : 0.05f * Mathf.Sin(t * 2.2f + h.Phase);

                // Look at it and it says hi.
                Vector3 to = h.Pos - head;
                bool looked = to.sqrMagnitude > 1e-4f && Vector3.Angle(gaze, to) < 7f;
                if (!looked) h.GazeSince = -1f;
                else if (h.GazeSince < 0f) h.GazeSince = t;
                else if (t - h.GazeSince >= GazeChirpSeconds && t - h.LastChirp >= ChirpEverySeconds)
                {
                    h.LastChirp = t;
                    h.HopStart = t;
                    if (Creatures != null && Creatures.Sounds != null)
                        Creatures.SayAt(Creatures.Sounds.Chirp((int)h.R.Species), h.Pos);
                }
            }
        }

        // ---------- Placement ----------

        CreatureBody MakeBody(Colony.Resident r)
        {
            var b = CreatureBody.Create(Material, transform, (float)r.Radius, (float)r.Wobble, (float)r.Lobes, (float)r.Seed, r.Rarity);
            b.name = "Resident " + Core.Creatures.Name(r.Species, r.Rarity);
            b.Look = Head;
            b.Dissolve = 0f;
            b.transform.localScale = Vector3.one * Scale;
            return b;
        }

        void Seat(Home h)
        {
            if (h.Body == null) return;
            Vector3 toHead = Vector3.ProjectOnPlane(HeadPos() - h.Pos, h.Up);
            h.Body.transform.SetPositionAndRotation(h.Pos, Quaternion.LookRotation(toHead.sqrMagnitude > 1e-6f ? toHead : Vector3.forward, h.Up));
            h.Body.transform.localScale = Vector3.one * Scale;
        }

        MRUKRoom CurrentRoom() =>
            Surface != null && Surface.SceneLoaded && MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;

        Vector3 HeadPos() => Head != null ? Head.position : Vector3.zero;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        bool Resolve(Colony.Resident r, MRUKRoom room, out Vector3 pos, out Vector3 up)
        {
            if (r.OnDesk) { DeskPose((int)Math.Round(r.X), out pos, out up); return true; }
            pos = default; up = Vector3.up;
            var a = FindAnchor(room, r.Anchor);
            if (a == null) return false;
            pos = a.transform.TransformPoint(new Vector3((float)r.X, (float)r.Y, (float)r.Z));
            up = Vector3.up;
            return true;
        }

        void Rehome(Colony.Resident r, MRUKRoom room, out Vector3 pos, out Vector3 up)
        {
            up = Vector3.up;
            var taken = new List<Vector3>();
            foreach (var h in homes) if (h.R != r) taken.Add(h.Pos);
            if (room != null && Surface != null &&
                ChooseHome(room, HeadPos(), Surface.Current, Surface.GridHalfExtent, taken, rng, Placement, out pos, out var anchor, out string why))
            {
                var local = anchor.transform.InverseTransformPoint(pos);
                r.Anchor = AnchorKey(anchor); r.X = local.x; r.Y = local.y; r.Z = local.z;
                LastPlacement = "room: " + anchor.name;
                return;
            }
            r.Anchor = Colony.Desk;
            int slot = 0;
            while (Colony.Residents.Exists(x => x != r && x.OnDesk && (int)Math.Round(x.X) == slot)) slot++;
            r.X = slot; r.Y = 0; r.Z = 0;
            DeskPose(slot, out pos, out up);
            LastPlacement = "desk slot " + slot;
        }

        Vector3 GridCenter(out Vector3 normal)
        {
            var s = Surface != null ? Surface.Current : default;
            normal = s.Normal.sqrMagnitude > 0.5f ? s.Normal : Vector3.up;
            return s.Center;
        }

        /// <summary>
        /// No room scan: slot <paramref name="slot"/> in columns of five to the left of the squares (the nest
        /// and the score card are on the right, the title beyond the far edge), on the same surface. On a
        /// wall or the easel they stand in a row under the squares instead.
        /// </summary>
        void DeskPose(int slot, out Vector3 pos, out Vector3 up)
        {
            var s = Surface != null ? Surface.Current : default;
            float half = Surface != null ? Surface.GridHalfExtent : 0.14f;
            DeskSlot(s, half, slot, out pos, out up);
        }

        public static void DeskSlot(SurfaceFinder.Surface s, float half, int slot, out Vector3 pos, out Vector3 up)
        {
            bool flat = s.Normal.y > 0.7f;
            up = flat ? s.Normal : Vector3.up;
            const float step = 0.06f;
            int col = slot / 5, row = slot % 5;
            if (flat)
                pos = s.Center - s.Right * (half + 0.07f + step * col) + s.Away * (half - 0.03f - step * row) + s.Normal * 0.002f;
            else
                pos = s.Center - s.Away * (half + 0.06f + step * col) + s.Right * (-half + 0.02f + step * row) + s.Normal * 0.04f;
        }

        /// <summary>A scene anchor's id that stays the same across launches: its UUID, or its label and place.</summary>
        public static string AnchorKey(MRUKAnchor a)
        {
            if (a == null) return Colony.Desk;
            var uuid = a.Anchor.Uuid;
            if (uuid != Guid.Empty) return uuid.ToString("N");
            Vector3 p = a.transform.position;
            return a.Label + "@" + Mathf.RoundToInt(p.x * 100) + "_" + Mathf.RoundToInt(p.y * 100) + "_" + Mathf.RoundToInt(p.z * 100);
        }

        public static MRUKAnchor FindAnchor(MRUKRoom room, string key)
        {
            if (room == null || string.IsNullOrEmpty(key) || key == Colony.Desk) return null;
            foreach (var a in room.Anchors) if (a != null && AnchorKey(a) == key) return a;
            return null;
        }

        /// <summary>
        /// A spot on a real horizontal surface (furniture top or the floor) where a resident can live: in
        /// front of a seated player (yaw), within distance, not above eye height by much, clear of the
        /// squares, not inside any scanned object, in plain sight from the head, and spaced from the others.
        /// </summary>
        public static bool ChooseHome(MRUKRoom room, Vector3 head, SurfaceFinder.Surface play, float gridHalf, IList<Vector3> taken,
                                      System.Random rng, Rules rules, out Vector3 pos, out MRUKAnchor anchor, out string why)
        {
            pos = default; anchor = null; why = "no surfaces";
            if (room == null) return false;
            Vector3 fwd = Flat(play.Center - head);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var perches = new List<MRUKAnchor>();
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue || !a.HasAnyLabel(Perches)) continue;
                if (Mathf.Abs(a.transform.forward.y) < 0.9f) continue;   // horizontal tops only
                Rect r = a.PlaneRect.Value;
                if (r.width < 2f * rules.EdgeInsetM + 0.02f || r.height < 2f * rules.EdgeInsetM + 0.02f) continue;
                perches.Add(a);
            }
            if (perches.Count == 0) return false;

            float best = float.MaxValue;
            var counts = new Dictionary<string, int>();
            void Reject(string reason) => counts[reason] = (counts.TryGetValue(reason, out int n) ? n : 0) + 1;
            for (int tries = 0; tries < 80; tries++)
            {
                var a = perches[rng.Next(perches.Count)];
                Rect r = a.PlaneRect.Value;
                float x = Mathf.Lerp(r.xMin + rules.EdgeInsetM, r.xMax - rules.EdgeInsetM, (float)rng.NextDouble());
                float y = Mathf.Lerp(r.yMin + rules.EdgeInsetM, r.yMax - rules.EdgeInsetM, (float)rng.NextDouble());
                Vector3 p = a.transform.TransformPoint(new Vector3(x, y, 0f));
                bool floor = a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR);
                Vector3 v = p - head;
                float d = v.magnitude;
                if (d < rules.MinDistanceM || d > rules.MaxDistanceM) { Reject("distance"); continue; }
                float yaw = Vector3.Angle(fwd, Flat(v));
                if (yaw > rules.MaxYawDeg) { Reject("behind or beside"); continue; }
                if (p.y > head.y + rules.MaxAboveEyeM) { Reject("too high"); continue; }
                Vector3 g = p - play.Center;
                if (Mathf.Abs(Vector3.Dot(g, play.Normal)) < 0.1f &&
                    Mathf.Abs(Vector3.Dot(g, play.Right)) < gridHalf + rules.ClearOfGridM &&
                    Mathf.Abs(Vector3.Dot(g, play.Away)) < gridHalf + rules.ClearOfGridM) { Reject("on the squares"); continue; }
                bool crowded = false;
                foreach (var q in taken) if (Vector3.Distance(p, q) < rules.SpacingM) { crowded = true; break; }
                if (crowded) { Reject("crowded"); continue; }
                Vector3 body = p + Vector3.up * 0.03f;
                if (!room.IsPositionInRoom(body)) { Reject("outside the room"); continue; }   // multi-room scans: this room only
                if (room.IsPositionInSceneVolume(body, 0.01f)) { Reject("inside something"); continue; }
                Vector3 sight = body - head;
                if (room.Raycast(new Ray(head, sight.normalized), sight.magnitude - 0.02f, out RaycastHit hit, out MRUKAnchor blocker) &&
                    blocker != a) { Reject("out of sight"); continue; }
                float score = yaw / rules.MaxYawDeg + Mathf.Abs(d - 1.1f) * 0.35f + (floor ? 0.6f : 0f) + (float)rng.NextDouble() * 0.25f;
                if (score < best) { best = score; pos = p + Vector3.up * 0.002f; anchor = a; }
            }
            if (anchor != null) { why = "ok"; return true; }
            // The commonest reason a spot was turned down.
            why = "no surfaces";
            int most = 0;
            foreach (var kv in counts) if (kv.Value > most) { most = kv.Value; why = kv.Key; }
            return false;
        }
    }
}
