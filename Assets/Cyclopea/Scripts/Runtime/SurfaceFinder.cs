using System;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Finds where the static lives, in this order:
    ///   1. Scene model (MRUK): a scanned table, else a wall in front, checked against the room:
    ///      the whole grid must fit on the surface, nothing scanned may sit on it, and nothing may
    ///      block the line of sight from the head.
    ///   2. Depth (MRUK EnvironmentRaycastManager, no Space Setup needed): a flat, clear spot on
    ///      whatever is in front, found with PlaceBox.
    ///   3. A virtual easel in front of the player, pulled closer if a real wall is in the way.
    /// The returned frame's right axis is horizontal for the viewer, so the shader's lateral shift
    /// is a pure horizontal disparity.
    /// </summary>
    public sealed class SurfaceFinder : MonoBehaviour
    {
        public enum Kind { Easel, Table, Wall }

        public struct Surface
        {
            public Kind Kind;
            public Vector3 Center;     // world point where the set of patches is centred
            public Vector3 Normal;     // towards the viewer's side of the surface
            public Vector3 Right;      // horizontal for the viewer
            public Vector3 Away;       // in-plane: away from the viewer (table) or up (wall, easel)
        }

        /// <summary>Nearest comfortable distance: closer than this the eyes strain to focus, and the grid outgrows a narrow field of view.</summary>
        public const float MinDistance = 0.45f;

        public Transform Head;
        public float Reach = 0.62f;
        public float GridHalfExtent = 0.14f;   // half the 2x2 grid's width, metres
        public bool UseSceneModel = true;
        public bool UseDepth = true;
        public EnvironmentRaycastManager Depth;

        public event Action<Surface> SurfaceChanged;
        public Surface Current { get; private set; }
        public string Source { get; private set; } = "easel";
        public bool SceneLoaded { get; private set; }
        /// <summary>True when the player faces something closer than MinDistance in every direction tried; the game asks them to turn.</summary>
        public bool FacingObstacle { get; private set; }

        // Valid before any other Start() reads it; refined once the head is tracked.
        void Awake() => Current = Easel(HeadPos, HeadFwd, null, 0.14f, out _);

        void Start()
        {
            if (UseSceneModel && MRUK.Instance != null)
                MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            // The head pose is not tracked on the first frame, and depth needs a few frames: place
            // again once, then retry the depth path for a few seconds if it wasn't ready.
            Invoke(nameof(Refresh), 1.0f);
            InvokeRepeating(nameof(RetryDepth), 2.0f, 1.0f);
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
        }

        void OnDestroy()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        // Depth raycasting costs GPU time: it runs only until the static has a home, and again after a recenter.
        void OnRecentered()
        {
            if (Depth != null) Depth.enabled = UseDepth;
            retries = 0;
            CancelInvoke(nameof(RetryDepth));
            InvokeRepeating(nameof(RetryDepth), 1.0f, 1.0f);
            Refresh();
        }

        void OnSceneLoaded()
        {
            SceneLoaded = true;
            Refresh();
        }

        [Tooltip("Depth tries per search, counted only while the headset is worn and the app has focus.")]
        public int DepthTries = 10;

        int retries;
        void RetryDepth()
        {
            if (Source != "easel" || retries >= DepthTries)
            {
                CancelInvoke(nameof(RetryDepth));
                if (Depth != null) Depth.enabled = false;
                return;
            }
            // At launch the system's own screens can hold focus for a while, and depth sees nothing then:
            // the first playtest (Nov 10) used up every try before the player could see the app.
            if (!Application.isEditor && (!OVRManager.hasInputFocus || !OVRManager.isHmdPresent)) return;
            retries++;
            Refresh();
        }

        /// <summary>
        /// No room scan and no desk found yet: look again with depth. The session calls this from the home
        /// card and the score cards, where moving the squares to the desk interrupts nothing.
        /// </summary>
        public void SearchAgain()
        {
            if (Source != "easel" || !UseDepth || Depth == null || IsInvoking(nameof(RetryDepth))) return;
            Depth.enabled = true;
            retries = 0;
            InvokeRepeating(nameof(RetryDepth), 0.5f, 1.0f);
        }

        Vector3 HeadPos => Head != null ? Head.position : new Vector3(0, 1.2f, 0);
        Vector3 HeadFwd => Head != null ? Head.forward : Vector3.forward;

        public void Refresh()
        {
            Surface s;
            string src;
            var room = SceneLoaded && MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room != null && ChooseInRoom(room, HeadPos, HeadFwd, Reach, GridHalfExtent, out s, out _)) src = "scene";
            else if (UseDepth && TryDepth(out s)) src = "depth";
            else if (Source == "depth") return;   // keep the last depth placement rather than fall back
            else { s = Easel(HeadPos, HeadFwd, room, GridHalfExtent, out bool clear); src = "easel"; FacingObstacle = !clear; }
            if (src != "easel") FacingObstacle = false;
            Current = s;
            Source = src;
            if (src != "easel" && Depth != null) Depth.enabled = false;
            SurfaceChanged?.Invoke(s);
        }

        // ---------- 1. Scene model ----------

        /// <summary>
        /// Best table or wall in a scanned room for a seated player at headPos. Pure function of the
        /// room and head pose, so the 58-room sweep test calls it directly.
        /// </summary>
        public static bool ChooseInRoom(MRUKRoom room, Vector3 headPos, Vector3 headFwd, float reach, float gridHalf,
                                        out Surface best, out string reason)
            => ChooseInRoom(room, headPos, headFwd, reach, gridHalf, out best, out reason, out _);

        public static bool ChooseInRoom(MRUKRoom room, Vector3 headPos, Vector3 headFwd, float reach, float gridHalf,
                                        out Surface best, out string reason, out MRUKAnchor chosen)
        {
            best = default;
            chosen = null;
            reason = "no table or wall in reach";
            if (room == null) { reason = "no room"; return false; }
            float bestScore = float.MaxValue;
            bool found = false;
            Vector3 flatFwd = Flat(headFwd);
            float[] lateral = { 0f, 0.2f, -0.2f, 0.4f, -0.4f, 0.6f, -0.6f };
            float[] back = { 0f, 0.15f, 0.3f, 0.45f };   // step away from the player if the near edge is cluttered

            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue) continue;
                bool table = a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE);
                bool wall = a.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE);
                if (!table && !wall) continue;
                Rect r = a.PlaneRect.Value;
                float margin = gridHalf + 0.02f;
                if (r.width < 2 * margin || r.height < 2 * margin) continue;

                Vector3 local = a.transform.InverseTransformPoint(headPos);
                float awayY = Mathf.Sign(r.center.y - local.y);
                foreach (float dy in back)
                foreach (float dx in lateral)
                {
                    if (wall && dy > 0) continue;   // on a wall "back" means up; the lateral sweep is enough
                    float x = Mathf.Clamp(local.x + dx, r.xMin + margin, r.xMax - margin);
                    float y = Mathf.Clamp(local.y + dy * awayY, r.yMin + margin, r.yMax - margin);
                    Vector3 c = a.transform.TransformPoint(new Vector3(x, y, 0));
                    Vector3 n = a.transform.forward;
                    if (Vector3.Dot(headPos - c, n) < 0) n = -n;

                    float d = Vector3.Distance(headPos, c);
                    if (d < MinDistance || d > reach + 0.25f) continue;
                    // Yaw only: a table well below eye level is still "in front".
                    float yaw = Vector3.Angle(flatFwd, Flat(c - headPos));
                    if (yaw > 50f) continue;

                    var s = Make(table ? Kind.Table : Kind.Wall, c + n * 0.003f, n, headPos);
                    if (!FitsOnPlane(a, r, s, gridHalf)) continue;
                    if (Cluttered(room, a, s, gridHalf)) { reason = "surface cluttered"; continue; }
                    if (Blocked(room, a, s, headPos, gridHalf)) { reason = "line of sight blocked"; continue; }

                    // Tables win clearly: seated play, and the later rungs reach toward the surface.
                    float score = d + (table ? 0f : 0.5f) + yaw / 180f + Mathf.Abs(dx) * 0.3f + dy * 0.3f;
                    if (score < bestScore) { bestScore = score; best = s; chosen = a; found = true; }
                }
            }
            if (found) reason = "ok";
            return found;
        }

        public static Vector3[] GridCorners(Surface s, float half)
        {
            return new[]
            {
                s.Center + s.Right * half + s.Away * half, s.Center - s.Right * half + s.Away * half,
                s.Center + s.Right * half - s.Away * half, s.Center - s.Right * half - s.Away * half
            };
        }

        static bool FitsOnPlane(MRUKAnchor a, Rect r, Surface s, float half)
        {
            foreach (var p in GridCorners(s, half))
            {
                Vector3 l = a.transform.InverseTransformPoint(p);
                if (l.x < r.xMin + 0.01f || l.x > r.xMax - 0.01f || l.y < r.yMin + 0.01f || l.y > r.yMax - 0.01f) return false;
            }
            return true;
        }

        static bool Cluttered(MRUKRoom room, MRUKAnchor surface, Surface s, float half)
        {
            var pts = GridCorners(s, half);
            for (int i = -1; i < pts.Length; i++)
            {
                Vector3 p = (i < 0 ? s.Center : pts[i]) + s.Normal * 0.05f;
                if (room.IsPositionInSceneVolume(p, out MRUKAnchor obj, true, 0f) && obj != null && obj != surface) return true;
            }
            return false;
        }

        public static bool Blocked(MRUKRoom room, MRUKAnchor surface, Surface s, Vector3 head, float half)
        {
            var pts = GridCorners(s, half);
            for (int i = -1; i < pts.Length; i++)
            {
                Vector3 p = (i < 0 ? s.Center : pts[i]) + s.Normal * 0.01f;
                Vector3 v = p - head;
                float dist = v.magnitude;
                if (dist < 0.05f) continue;
                if (room.Raycast(new Ray(head, v / dist), dist - 0.02f, out RaycastHit _, out MRUKAnchor hit) && hit != surface)
                    return true;
            }
            return false;
        }

        // ---------- 2. Depth (no Space Setup) ----------

        bool TryDepth(out Surface s)
        {
            s = default;
            if (Depth == null || !Depth.isActiveAndEnabled || !EnvironmentRaycastManager.IsSupported || Head == null) return false;
            Vector3 head = HeadPos, fwd = Flat(HeadFwd);
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3 box = new Vector3(2 * GridHalfExtent + 0.03f, 2 * GridHalfExtent + 0.03f, 0f);
            // Desk first: rays pitched down in front of the player, then a wall straight ahead.
            float[] pitches = { 40f, 30f, 50f, 20f };
            float[] yaws = { 0f, 12f, -12f };
            foreach (float pitch in pitches)
            foreach (float yaw in yaws)
            {
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, side) * fwd;
                if (Depth.PlaceBox(new Ray(head, dir), box, fwd, out var hit) && Accept(hit, head, out s)) return true;
            }
            if (Depth.PlaceBox(new Ray(head, fwd), box, Vector3.up, out var wallHit) && Accept(wallHit, head, out s)) return true;
            return false;
        }

        bool Accept(EnvironmentRaycastHit hit, Vector3 head, out Surface s)
        {
            s = default;
            Vector3 n = hit.normal;
            if (Vector3.Dot(head - hit.point, n) < 0) n = -n;
            float d = Vector3.Distance(head, hit.point);
            if (d < MinDistance || d > Reach + 0.25f) return false;
            bool horizontal = n.y > 0.8f;
            bool vertical = Mathf.Abs(n.y) < 0.3f;
            if (!horizontal && !vertical) return false;
            s = Make(horizontal ? Kind.Table : Kind.Wall, hit.point + n * 0.003f, n, head);
            return true;
        }

        // ---------- 3. Easel ----------

        /// <summary>
        /// Virtual easel 55 cm ahead and 22 cm below the eyes. In a scanned room it must have a clear
        /// view and stand in free space; if straight ahead is blocked it turns up to 40 degrees to find
        /// open space, then tries closer (never nearer than MinDistance). clear = false means every
        /// option was blocked: the easel is placed straight ahead and the game asks the player to turn.
        /// </summary>
        public static Surface Easel(Vector3 head, Vector3 headFwd, MRUKRoom room, float half, out bool clear)
        {
            Vector3 fwd = Flat(headFwd);
            clear = true;
            Surface straight = EaselAt(head, fwd, 0.592f);
            if (room == null) return straight;
            float[] yaws = { 0f, 20f, -20f, 40f, -40f };
            float[] dists = { 0.592f, 0.52f, MinDistance };
            foreach (float dist in dists)
            foreach (float yaw in yaws)
            {
                var s = EaselAt(head, Quaternion.AngleAxis(yaw, Vector3.up) * fwd, dist);
                if (!room.IsPositionInSceneVolume(s.Center, 0.02f) && !Blocked(room, null, s, head, half)) return s;
            }
            clear = false;
            return EaselAt(head, fwd, MinDistance);
        }

        static Surface EaselAt(Vector3 head, Vector3 fwd, float dist)
        {
            Vector3 dir = (fwd * 0.55f + Vector3.down * 0.22f).normalized;
            Vector3 c = head + dir * dist;
            return Make(Kind.Easel, c, (head - c).normalized, head);
        }

        static Vector3 Flat(Vector3 v)
        {
            v = Vector3.ProjectOnPlane(v, Vector3.up);
            return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
        }

        public static Surface Make(Kind kind, Vector3 c, Vector3 n, Vector3 head)
        {
            Vector3 away = Vector3.ProjectOnPlane(c - head, n);
            if (Mathf.Abs(n.y) < 0.6f || away.magnitude < 0.25f * Vector3.Distance(head, c))
                away = Vector3.ProjectOnPlane(Vector3.up, n);
            away.Normalize();
            Vector3 right = Vector3.Cross(n, away).normalized;
            return new Surface { Kind = kind, Center = c, Normal = n, Right = right, Away = away };
        }

        /// <summary>Rotation for a Unity quad lying in the surface: visible face toward the viewer, +X = viewer's right.</summary>
        public static Quaternion QuadRotation(Surface s) => Quaternion.LookRotation(-s.Normal, s.Away);
    }
}
