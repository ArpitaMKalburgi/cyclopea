using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Slap a tile with a real hand. A fingertip or palm that comes down onto a tile (from more than
    /// ArmHeight above it to under HitHeight, inside its footprint) chooses it; that hand must lift again
    /// before it can slap once more. On a real desk the desk itself is the feedback.
    /// One hand coming down is one slap, however many of its points land: the first playtest (Nov 10)
    /// logged 41 empty taps in a round because the palm landed on the tile next to the fingertips and
    /// counted as a second, empty pick (which also broke the combo). Now the hand's other points are
    /// quiet for a moment, and if one of them is over a tile with a critter up, that is the tile it hit.
    /// </summary>
    public sealed class SlapSelector : PatchSelector
    {
        public FingerTips Tips;
        public StereoPatch[] Targets;
        [Tooltip("Prefers the tile with a critter in it when one hand covers two tiles.")]
        public WhackRound Arcade;
        [Tooltip("A point this far above a tile (m) is ready to slap.")]
        public float ArmHeight = 0.045f;
        [Tooltip("...and slaps when it comes under this (m).")]
        public float HitHeight = 0.02f;
        [Tooltip("How far outside a tile's edge still counts, as a fraction of its width.")]
        public float EdgeMargin = 0.08f;
        [Tooltip("After a slap, the same hand's other points can't slap for this long (s).")]
        public float RefractorySeconds = 0.35f;
        [Tooltip("A hand that lands on an empty tile waits this long (s) for the rest of it: if its fingertips land on " +
                 "a critter next door, that is the tile it hit. A landing on a critter counts at once.")]
        public float LandingWindowSeconds = 0.08f;

        readonly Vector3[] points = new Vector3[FingerTips.SlapPointCount];
        readonly bool[] valid = new bool[FingerTips.SlapPointCount];
        readonly bool[] armed = new bool[FingerTips.SlapPointCount];
        readonly float[] quietUntil = new float[3];
        readonly StereoPatch[] pending = new StereoPatch[3];
        readonly float[] pendingUntil = new float[3];

        void Update()
        {
            Hovered = null;
            if (Tips == null || Targets == null) return;
            Tips.SlapPoints(points, valid);
            StereoPatch plane = null;
            foreach (var t in Targets) if (t != null) { plane = t; break; }
            if (plane == null) return;
            float now = Time.time;
            for (int i = 0; i < points.Length; i++)
            {
                if (!valid[i]) { armed[i] = false; continue; }
                // Height above the grid's plane: a hand sliding in along the desk never slaps, only one coming down.
                float h = Height(points[i], plane);
                if (h > ArmHeight) { armed[i] = true; continue; }
                var tile = Over(points[i]);
                if (tile == null) continue;
                Hovered = tile;
                if (h >= HitHeight) continue;
                int hand = FingerTips.HandOf(i);
                // The rest of a hand that already landed on an empty tile: a critter under it wins.
                if (pending[hand] != null)
                {
                    if (Arcade != null && Arcade.IsRealUp(tile)) Land(hand, tile);
                    continue;
                }
                if (!armed[i]) continue;
                if (now < quietUntil[hand]) { armed[i] = false; continue; }
                for (int j = 0; j < points.Length; j++) if (FingerTips.HandOf(j) == hand) armed[j] = false;
                quietUntil[hand] = now + RefractorySeconds;
                if (Arcade == null || Arcade.IsRealUp(tile) || LandingWindowSeconds <= 0) Raise(tile, "slap");
                else { pending[hand] = tile; pendingUntil[hand] = now + LandingWindowSeconds; }
            }
            for (int hand = 0; hand < pending.Length; hand++)
                if (pending[hand] != null && now >= pendingUntil[hand]) Land(hand, pending[hand]);
        }

        void Land(int hand, StereoPatch tile)
        {
            pending[hand] = null;
            Raise(tile, "slap");
        }

        void OnDisable() { for (int h = 0; h < pending.Length; h++) pending[h] = null; }

        static float Height(Vector3 p, StereoPatch plane) => Vector3.Dot(p - plane.transform.position, -plane.transform.forward);

        /// <summary>The shown tile under this point.</summary>
        StereoPatch Over(Vector3 p)
        {
            float height = float.PositiveInfinity;
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
