using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Shows instead of telling. A see-through hand (GhostHand) acts out the move right where it's needed:
    ///   first screen: it pinches above a square ("pinch a square to start");
    ///   Critter Whack's countdown, for a newer player: it slaps the middle tile flat with an open palm.
    /// The countdown has no critters up yet, so the hand never hints where one is: only two eyes find them.
    /// It stops after <see cref="MaxLoops"/> rounds of the gesture so it never nags, fades when the player's real
    /// hand comes near, and stays away for controller players (their labels say what the trigger does) and for
    /// look-and-hold players.
    /// </summary>
    public sealed class SlapCoach : MonoBehaviour
    {
        public SpotSession Session;
        public WhackRound Arcade;
        public FingerTips Tips;
        public Transform Head;
        public Material Material;
        [Tooltip("The countdown slap is shown until the player has caught this many critters in all.")]
        public int CoachUntilCaught = 40;
        public int MaxLoops = 3;
        public float SlapPeriod = 1.5f, PinchPeriod = 1.6f;

        GhostHand hand;
        string mode = "";
        float since;
        bool done;

        public bool Showing => hand != null && hand.gameObject.activeSelf;
        /// <summary>"pinch", "slap" or "" (tests and the heartbeat).</summary>
        public string Mode => Showing ? mode : "";

        void Start()
        {
            if (Material != null) hand = GhostHand.Make("Coach hand (see-through)", transform, Material, false);
        }

        string Wanted()
        {
            if (Session == null || Session.Paused || ControllerSelector.InUse || Options.LookAndHold) return "";
            if (Session.ShowingNotice) return "pinch";
            if (Arcade != null && Arcade.Counting && WhackRound.TotalCaught < CoachUntilCaught) return "slap";
            return "";
        }

        void Update()
        {
            if (hand == null || Head == null) return;
            string want = Wanted();
            if (want != mode)
            {
                mode = want; since = Time.time; done = false;
                if (want.Length > 0) Milestones.Mark("coach_" + want);
            }
            if (mode.Length == 0 || done) { Hide(); return; }
            float period = mode == "slap" ? SlapPeriod : PinchPeriod;
            float t = Time.time - since;
            if (t >= period * MaxLoops) { done = true; Hide(); return; }
            float p = (t % period) / period;
            if (!Place(mode, p)) { Hide(); return; }
            float fade = Mathf.Clamp01(p / 0.12f) * Mathf.Clamp01((1f - p) / 0.12f);
            // The player's real hand is there: they've got it, step aside.
            if (Tips != null && Tips.TryNearest(hand.PalmCenter, 0.12f, out _)) fade *= 0.15f;
            hand.Alpha = fade;
            if (!hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
        }

        void Hide() { if (hand != null && hand.gameObject.activeSelf) hand.gameObject.SetActive(false); }

        bool Place(string what, float p)
        {
            Transform target = what == "slap"
                ? (Arcade != null && Arcade.Tiles != null && Arcade.Tiles.Length > 4 ? Arcade.Tiles[4].transform : null)
                : (Session.Patches != null && Session.Patches.Length > 1 && Session.Patches[1] != null ? Session.Patches[1].transform : null);
            if (target == null) return false;
            Vector3 c = target.position;
            Vector3 n = target.forward;
            if (Vector3.Dot(n, Head.position - c) < 0f) n = -n;            // the face the player sees
            Vector3 away = Vector3.ProjectOnPlane(c - Head.position, n);
            if (away.sqrMagnitude < 1e-6f) away = Vector3.ProjectOnPlane(Head.forward, n);
            away.Normalize();
            if (what == "slap")
            {
                // Raise (open hand, palm down), fall flat onto the tile, rest, lift.
                float h = p < 0.35f ? 0.15f
                        : p < 0.48f ? Mathf.Lerp(0.15f, 0.005f, Mathf.SmoothStep(0f, 1f, (p - 0.35f) / 0.13f))
                        : p < 0.72f ? 0.005f
                        : Mathf.Lerp(0.005f, 0.15f, (p - 0.72f) / 0.28f);
                hand.Curl = 0.04f; hand.Pinch = 0f; hand.Point = 0f;
                hand.PlaceBy(c + n * h - away * 0.02f, Quaternion.LookRotation(away, n), GhostHand.Anchor.Palm);
            }
            else
            {
                // Hover in front of the square, fingers towards it, and pinch.
                float k = p < 0.3f ? 0f : p < 0.5f ? (p - 0.3f) / 0.2f : p < 0.75f ? 1f : Mathf.Max(0f, 1f - (p - 0.75f) / 0.2f);
                hand.Curl = 0.1f; hand.Point = 0f; hand.Pinch = Mathf.SmoothStep(0f, 1f, k);
                Vector3 toward = (c - Head.position).normalized;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, toward);
                if (up.sqrMagnitude < 1e-6f) up = n;
                hand.PlaceBy(c + n * 0.09f - toward * 0.03f, Quaternion.LookRotation(toward, up.normalized), GhostHand.Anchor.PinchPoint);
            }
            return true;
        }
    }
}
