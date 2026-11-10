using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Point and pull the trigger with a Touch controller. Hands are the main input, but a player who
    /// picks the game up with controllers in hand must never be stuck: every screen answers the
    /// trigger (or A / X) on whatever square the controller points at, and the words say so.
    /// </summary>
    public sealed class ControllerSelector : PatchSelector
    {
        public Transform LeftAnchor, RightAnchor;
        public LineRenderer LeftRay, RightRay;
        public float MaxDistance = 2.5f;

        /// <summary>True while a Touch controller is connected and tracked (the words then say "pull the trigger").</summary>
        public static bool InUse { get; private set; }

        void Update()
        {
            bool l = Tracked(OVRInput.Controller.LTouch), r = Tracked(OVRInput.Controller.RTouch);
            InUse = l || r;
            var hl = Step(l, LeftAnchor, LeftRay, OVRInput.Controller.LTouch, "trigger-left");
            var hr = Step(r, RightAnchor, RightRay, OVRInput.Controller.RTouch, "trigger-right");
            Hovered = hr != null ? hr : hl;
        }

        void OnDisable() => InUse = false;

        static bool Tracked(OVRInput.Controller c) =>
            (OVRInput.GetConnectedControllers() & c) == c && OVRInput.GetControllerPositionTracked(c);

        StereoPatch Step(bool on, Transform anchor, LineRenderer line, OVRInput.Controller c, string method)
        {
            bool show = on && anchor != null;
            if (line != null) line.enabled = show;
            if (!show) return null;
            var ray = new Ray(anchor.position, anchor.forward);
            var patch = RaycastPatch(ray, MaxDistance, out var hit);
            if (line != null)
            {
                line.SetPosition(0, ray.origin);
                line.SetPosition(1, patch != null ? hit : ray.origin + ray.direction * 0.35f);
            }
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, c) || OVRInput.GetDown(OVRInput.Button.One, c))
                Raise(patch, method);
            return patch;
        }
    }
}
