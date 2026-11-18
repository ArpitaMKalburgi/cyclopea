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
        [Tooltip("A short tick in the controller when its trigger chooses a square: a hand feels the desk, a controller should answer too.")]
        public float BuzzSeconds = 0.05f;
        [Range(0f, 1f)] public float BuzzStrength = 0.45f;

        float buzzL = -1f, buzzR = -1f;

        /// <summary>True while a tick is playing in either controller.</summary>
        public bool Buzzing => buzzL >= 0f || buzzR >= 0f;

        /// <summary>A short tick in one controller. It ends by itself, and at once if this component is switched off.</summary>
        public void Tick(bool left)
        {
            if (!isActiveAndEnabled) return;
            try { OVRInput.SetControllerVibration(0.5f, BuzzStrength, left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch); }
            catch (System.Exception) { return; }
            if (left) buzzL = Time.unscaledTime + BuzzSeconds; else buzzR = Time.unscaledTime + BuzzSeconds;
        }

        void StopBuzz(ref float until, OVRInput.Controller c, bool now = false)
        {
            if (until < 0f || (!now && Time.unscaledTime < until)) return;
            until = -1f;
            try { OVRInput.SetControllerVibration(0f, 0f, c); } catch (System.Exception) { }
        }

        /// <summary>True while a Touch controller is connected and tracked (the words then say "pull the trigger").</summary>
        public static bool InUse { get; private set; }

        void Update()
        {
            bool l = Tracked(OVRInput.Controller.LTouch), r = Tracked(OVRInput.Controller.RTouch);
            InUse = l || r;
            var hl = Step(l, LeftAnchor, LeftRay, OVRInput.Controller.LTouch, "trigger-left");
            var hr = Step(r, RightAnchor, RightRay, OVRInput.Controller.RTouch, "trigger-right");
            Hovered = hr != null ? hr : hl;
            StopBuzz(ref buzzL, OVRInput.Controller.LTouch);
            StopBuzz(ref buzzR, OVRInput.Controller.RTouch);
        }

        void OnDisable()
        {
            InUse = false;
            StopBuzz(ref buzzL, OVRInput.Controller.LTouch, true);
            StopBuzz(ref buzzR, OVRInput.Controller.RTouch, true);
        }

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
            {
                Raise(patch, method);
                if (patch != null) Tick(c == OVRInput.Controller.LTouch);
            }
            return patch;
        }
    }
}
