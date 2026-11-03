using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Finds where the static lives: a scanned table, else a scanned wall in front, else a virtual
    /// easel in front of the player. The returned frame's right axis is horizontal for the viewer,
    /// so the shader's lateral shift is a pure horizontal disparity.
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

        public Transform Head;
        public float Reach = 0.62f;
        public bool UseSceneModel = true;

        public event Action<Surface> SurfaceChanged;
        public Surface Current { get; private set; }
        public bool SceneLoaded { get; private set; }

        void Start()
        {
            Current = Easel();
            if (UseSceneModel && MRUK.Instance != null)
                MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
            // The head pose is not tracked on the first frame; place again once it is.
            Invoke(nameof(Refresh), 1.0f);
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += Refresh;
        }

        void OnDestroy()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= Refresh;
        }

        void OnSceneLoaded()
        {
            SceneLoaded = true;
            Refresh();
        }

        public void Refresh()
        {
            Surface s;
            if (!(SceneLoaded && TryScene(out s))) s = Easel();
            Current = s;
            SurfaceChanged?.Invoke(s);
        }

        Surface Easel()
        {
            Vector3 head = Head != null ? Head.position : new Vector3(0, 1.2f, 0);
            Vector3 fwd = Head != null ? Vector3.ProjectOnPlane(Head.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 c = head + fwd * 0.55f + Vector3.down * 0.22f;
            Vector3 n = (head - c).normalized;
            return Make(Kind.Easel, c, n, head);
        }

        bool TryScene(out Surface best)
        {
            best = default;
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null || Head == null) return false;
            float bestScore = float.MaxValue;
            bool found = false;
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue) continue;
                bool table = a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE);
                bool wall = a.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE);
                if (!table && !wall) continue;

                Rect r = a.PlaneRect.Value;
                if (r.width < 0.3f || r.height < 0.3f) continue;
                // Nearest point of the plane rect to the head, pulled inside the edges.
                Vector3 local = a.transform.InverseTransformPoint(Head.position);
                float x = Mathf.Clamp(local.x, r.xMin + 0.15f, r.xMax - 0.15f);
                float y = Mathf.Clamp(local.y, r.yMin + 0.15f, r.yMax - 0.15f);
                Vector3 c = a.transform.TransformPoint(new Vector3(x, y, 0));
                Vector3 n = a.transform.forward;
                if (Vector3.Dot(Head.position - c, n) < 0) n = -n;
                float d = Vector3.Distance(Head.position, c);
                if (d > Reach + 0.25f) continue;
                float facing = Vector3.Dot(Head.forward, (c - Head.position).normalized);
                if (facing < 0.2f) continue;
                // Prefer tables (seated play), then the closest surface in front of the face.
                float score = d + (table ? 0f : 0.25f) - facing * 0.2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Make(table ? Kind.Table : Kind.Wall, c + n * 0.003f, n, Head.position);
                    found = true;
                }
            }
            return found;
        }

        static Surface Make(Kind kind, Vector3 c, Vector3 n, Vector3 head)
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
