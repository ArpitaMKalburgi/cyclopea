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
        [Tooltip("Things on the desk (a laptop, a book) or a desk found a little too low stop the finger before it " +
                 "reaches the squares. A finger that comes down fast over a square and is stopped under this height (m) " +
                 "has touched it. Before, such a touch needed several tries.")]
        public float StoppedBelow = 0.20f;
        [Tooltip("...after falling at least this far (m)...")]
        public float StoppedAfterDrop = 0.025f;
        [Tooltip("...at this speed or faster (m/s) at some point of the fall.")]
        public float StoppedFromSpeed = 0.25f;
        [Tooltip("Hand tracking is slower than the display, so some frames repeat the last hand position. A fall " +
                 "has stopped only when a new position no longer falls, or none has fallen for this long (s).")]
        public float StopSeconds = 0.05f;
        /// <summary>Touches that counted because the finger was stopped above the squares.</summary>
        public int StoppedTouches { get; private set; }

        /// <summary>The session, for the squares that are buttons on cards and menus. Found at run time.</summary>
        public SpotSession Session;
        /// <summary>Touches that chose a card or menu square (not a round tile).</summary>
        public int ButtonTouches { get; private set; }
        readonly System.Collections.Generic.List<StereoPatch> buttons = new System.Collections.Generic.List<StereoPatch>();

        void Start()
        {
            if (Session == null) Session = GetComponent<SpotSession>();
            if (Session == null) Session = FindFirstObjectByType<SpotSession>(FindObjectsInactive.Include);
            if (Session == null) return;
            if (Session.Patches != null) foreach (var p in Session.Patches) if (p != null) buttons.Add(p);
            if (Session.MenuButton != null) buttons.Add(Session.MenuButton);
        }

        bool ButtonsNow => buttons.Count > 0 && Session != null && Session.SquaresAreButtons;

        readonly Vector3[] points = new Vector3[FingerTips.SlapPointCount];
        readonly bool[] valid = new bool[FingerTips.SlapPointCount];
        readonly bool[] armed = new bool[FingerTips.SlapPointCount];
        readonly float[] quietUntil = new float[3];
        readonly StereoPatch[] pending = new StereoPatch[3];
        readonly float[] pendingUntil = new float[3];
        readonly float[] lastH = new float[FingerTips.SlapPointCount];
        readonly float[] lastAt = new float[FingerTips.SlapPointCount];
        readonly float[] fallFrom = new float[FingerTips.SlapPointCount];
        readonly float[] fallSpeed = new float[FingerTips.SlapPointCount];
        readonly bool[] seen = new bool[FingerTips.SlapPointCount];
        readonly bool[] falling = new bool[FingerTips.SlapPointCount];

        void Update()
        {
            Hovered = null;
            if (Tips == null || Targets == null) return;
            Tips.SlapPoints(points, valid);
            StereoPatch plane = null;
            if (ButtonsNow) plane = buttons[0];
            else foreach (var t in Targets) if (t != null) { plane = t; break; }
            if (plane == null) return;
            float now = Time.time;
            for (int i = 0; i < points.Length; i++)
            {
                if (!valid[i]) { armed[i] = false; seen[i] = false; falling[i] = false; continue; }
                // Height above the grid's plane: a hand sliding in along the desk never slaps, only one coming down.
                float h = Height(points[i], plane);
                if (Stopped(i, h, now, plane)) continue;
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

        /// <summary>
        /// True when this point's fall has just been stopped above the squares by something real, over a square:
        /// that is a touch. A fall that goes on down to the squares is left to the usual rule.
        /// </summary>
        bool Stopped(int i, float h, float now, StereoPatch plane)
        {
            if (!seen[i]) { seen[i] = true; lastH[i] = h; lastAt[i] = now; falling[i] = false; return false; }
            // Frames without a new hand position repeat the last height. They are not a stop: the first on-headset
            // try (Nov 18) cut every fall into pieces that way, and one tap in four counted.
            float dh = h - lastH[i];
            if (Mathf.Abs(dh) > 0.0002f)
            {
                float v = dh / Mathf.Max(now - lastAt[i], 0.004f);
                float before = lastH[i];
                lastH[i] = h;
                lastAt[i] = now;
                if (v < -0.15f)
                {
                    if (!falling[i]) { falling[i] = true; fallFrom[i] = before; fallSpeed[i] = 0f; }
                    fallSpeed[i] = Mathf.Max(fallSpeed[i], -v);
                    return false;
                }
                if (!falling[i]) return false;
            }
            else if (!falling[i] || now - lastAt[i] < StopSeconds) return false;
            falling[i] = false;   // the fall ended on this frame
            // Rounds only. On cards and menus a hand that merely settles above the squares must not choose one:
            // there a touch has to reach the surface, or the player pinches.
            if (ButtonsNow) return false;
            if (h < HitHeight || h > StoppedBelow) return false;
            if (fallFrom[i] - h < StoppedAfterDrop || fallSpeed[i] < StoppedFromSpeed) return false;
            int hand = FingerTips.HandOf(i);
            if (now < quietUntil[hand] || pending[hand] != null) return false;
            // A finger held up by something on the desk is not over the square it covers: from the player's eyes it
            // covers a square further away. The first on-headset try (Nov 18) logged four touches in a round and all
            // four on the wrong square. The square is the one on the line from the eyes through the fingertip.
            var tile = Over(Seen(points[i], plane));
            var under = Over(points[i]);
            if (tile == null) tile = under;
            else if (Arcade != null && !Arcade.IsRealUp(tile) && under != null && Arcade.IsRealUp(under)) tile = under;
            if (tile == null) return false;
            for (int j = 0; j < points.Length; j++) if (FingerTips.HandOf(j) == hand) armed[j] = false;
            quietUntil[hand] = now + RefractorySeconds;
            StoppedTouches++;
            Hovered = tile;
            if (Arcade == null || Arcade.IsRealUp(tile) || LandingWindowSeconds <= 0) Raise(tile, "slap");
            else { pending[hand] = tile; pendingUntil[hand] = now + LandingWindowSeconds; }
            return true;
        }

        /// <summary>Where the player sees this point on the squares: along the line from the eyes through it.</summary>
        static Vector3 Seen(Vector3 p, StereoPatch plane)
        {
            var cam = Camera.main;
            if (cam == null || plane == null) return p;
            Vector3 n = -plane.transform.forward;
            Vector3 eye = cam.transform.position;
            Vector3 dir = p - eye;
            float towards = Vector3.Dot(dir, n);
            if (towards > -0.0001f) return p;   // not looking down onto the squares
            float t = Vector3.Dot(plane.transform.position - eye, n) / towards;
            if (t < 1f || t > 4f) return p;      // the squares are not behind the finger
            return eye + dir * t;
        }

        void Land(int hand, StereoPatch tile)
        {
            pending[hand] = null;
            if (buttons.Contains(tile)) ButtonTouches++;
            Raise(tile, "slap");
        }

        void OnDisable() { for (int h = 0; h < pending.Length; h++) pending[h] = null; }

        static float Height(Vector3 p, StereoPatch plane) => Vector3.Dot(p - plane.transform.position, -plane.transform.forward);

        /// <summary>The shown tile under this point.</summary>
        StereoPatch Over(Vector3 p)
        {
            float height = float.PositiveInfinity;
            StereoPatch best = null;
            bool menus = ButtonsNow;
            int n = Targets.Length + (menus ? buttons.Count : 0);
            for (int k = 0; k < n; k++)
            {
                var t = k < Targets.Length ? Targets[k] : buttons[k - Targets.Length];
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
