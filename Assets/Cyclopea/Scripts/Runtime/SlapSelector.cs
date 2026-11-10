using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Slap a tile with a real hand. A fingertip or palm that comes down onto a tile (from more than
    /// ArmHeight above it to under HitHeight, inside its footprint) chooses it; that point must lift again
    /// before it can slap once more. On a real desk the desk itself is the feedback.
    /// </summary>
    public sealed class SlapSelector : PatchSelector
    {
        public FingerTips Tips;
        public StereoPatch[] Targets;
        [Tooltip("A point this far above a tile (m) is ready to slap.")]
        public float ArmHeight = 0.045f;
        [Tooltip("...and slaps when it comes under this (m).")]
        public float HitHeight = 0.02f;
        [Tooltip("How far outside a tile's edge still counts, as a fraction of its width.")]
        public float EdgeMargin = 0.08f;

        readonly Vector3[] points = new Vector3[7];
        readonly bool[] valid = new bool[7];
        readonly bool[] armed = new bool[7];

        void Update()
        {
            Hovered = null;
            if (Tips == null || Targets == null) return;
            Tips.SlapPoints(points, valid);
            StereoPatch plane = null;
            foreach (var t in Targets) if (t != null) { plane = t; break; }
            if (plane == null) return;
            for (int i = 0; i < points.Length; i++)
            {
                if (!valid[i]) { armed[i] = false; continue; }
                // Height above the grid's plane: a hand sliding in along the desk never slaps, only one coming down.
                float h = Vector3.Dot(points[i] - plane.transform.position, -plane.transform.forward);
                if (h > ArmHeight) { armed[i] = true; continue; }
                var tile = Over(points[i], out _);
                if (tile == null) continue;
                Hovered = tile;
                if (armed[i] && h < HitHeight)
                {
                    armed[i] = false;
                    Raise(tile, "slap");
                }
            }
        }

        /// <summary>The shown tile under this point, and the point's height above it (m).</summary>
        StereoPatch Over(Vector3 p, out float height)
        {
            height = float.PositiveInfinity;
            StereoPatch best = null;
            foreach (var t in Targets)
            {
                if (t == null) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Vector3 local = t.transform.InverseTransformPoint(p);
                float lim = 0.5f + EdgeMargin;
                if (Mathf.Abs(local.x) > lim || Mathf.Abs(local.y) > lim) continue;
                float h = Vector3.Dot(p - t.transform.position, -t.transform.forward);
                if (h < -0.04f) continue;   // well below the surface: not this tile
                if (h < height) { height = h; best = t; }
            }
            return best;
        }
    }
}
