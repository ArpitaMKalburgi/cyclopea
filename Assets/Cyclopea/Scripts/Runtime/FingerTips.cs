using Oculus.Interaction.Input;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Index fingertips from the Interaction SDK hands (IHand.GetJointPose, HandIndexTip), for creatures
    /// to climb onto. A simulated tip (tests, editor) stands in for a real hand when set.
    /// </summary>
    public sealed class FingerTips : MonoBehaviour
    {
        public Hand Left, Right;
        [Tooltip("Tests and editor: an object that stands in for a tracked right index fingertip.")]
        public Transform SimulatedTip;

        void Start()
        {
            if (Left == null || Right == null) Find();
        }

        /// <summary>Picks up the rig's data hands (not the synthetic, pose-locked ones).</summary>
        public void Find()
        {
            foreach (var h in FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (h.GetType().Name.Contains("Synthetic")) continue;
                if (h.Handedness == Handedness.Left && Left == null) Left = h;
                else if (h.Handedness == Handedness.Right && Right == null) Right = h;
            }
        }

        /// <summary>The tracked index fingertip nearest to <paramref name="near"/>, within <paramref name="maxDistance"/>.</summary>
        public bool TryNearest(Vector3 near, float maxDistance, out Pose tip)
        {
            tip = default;
            float best = maxDistance;
            bool found = false;
            if (SimulatedTip != null && SimulatedTip.gameObject.activeInHierarchy)
                Consider(new Pose(SimulatedTip.position, SimulatedTip.rotation), near, ref best, ref tip, ref found);
            if (TryTip(Left, out var l)) Consider(l, near, ref best, ref tip, ref found);
            if (TryTip(Right, out var r)) Consider(r, near, ref best, ref tip, ref found);
            return found;
        }

        /// <summary>
        /// The points of both hands that can slap a tile, in a fixed order (simulated tip; left index, middle,
        /// palm; right index, middle, palm). Untracked points are false in <paramref name="valid"/>.
        /// </summary>
        public void SlapPoints(Vector3[] points, bool[] valid)
        {
            for (int i = 0; i < valid.Length; i++) valid[i] = false;
            if (points.Length < 7 || valid.Length < 7) return;
            if (SimulatedTip != null && SimulatedTip.gameObject.activeInHierarchy) { points[0] = SimulatedTip.position; valid[0] = true; }
            Fill(Left, 1, points, valid);
            Fill(Right, 4, points, valid);
        }

        static void Fill(Hand h, int at, Vector3[] points, bool[] valid)
        {
            if (h == null || !h.IsTrackedDataValid) return;
            if (h.GetJointPose(HandJointId.HandIndexTip, out var a)) { points[at] = a.position; valid[at] = true; }
            if (h.GetJointPose(HandJointId.HandMiddleTip, out var b)) { points[at + 1] = b.position; valid[at + 1] = true; }
            if (h.GetJointPose(HandJointId.HandPalm, out var c)) { points[at + 2] = c.position; valid[at + 2] = true; }
        }

        /// <summary>Same tip as last time if it is still tracked (a riding creature stays on its finger).</summary>
        public bool TryFollow(Vector3 last, float maxJump, out Pose tip) => TryNearest(last, maxJump, out tip);

        static void Consider(Pose p, Vector3 near, ref float best, ref Pose tip, ref bool found)
        {
            float d = Vector3.Distance(p.position, near);
            if (d <= best) { best = d; tip = p; found = true; }
        }

        static bool TryTip(Hand h, out Pose p)   // Hand, not IHand: Unity's null check covers unassigned fields
        {
            p = default;
            return h != null && h.IsTrackedDataValid && h.GetJointPose(HandJointId.HandIndexTip, out p);
        }
    }
}
