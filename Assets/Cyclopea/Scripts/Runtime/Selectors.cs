using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
    /// <summary>Anything that can choose a patch: hand ray + pinch, poke, head-gaze dwell, keyboard.</summary>
    public abstract class PatchSelector : MonoBehaviour
    {
        public event Action<StereoPatch, string> Selected;   // patch, input method
        public StereoPatch Hovered { get; protected set; }
        protected void Raise(StereoPatch p, string method) { if (p != null) Selected?.Invoke(p, method); }

        protected static StereoPatch RaycastPatch(Ray ray, float maxDistance, out Vector3 hit)
        {
            hit = ray.origin + ray.direction * maxDistance;
            if (Physics.Raycast(ray, out var h, maxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                hit = h.point;
                return h.collider.GetComponentInParent<StereoPatch>();
            }
            return null;
        }
    }

    /// <summary>
    /// Pinch-to-select with the system pointer ray from either hand. Hysteresis on pinch strength so
    /// a half-pinch never fires twice; a short ray line shows where the hand points.
    /// </summary>
    public sealed class HandRaySelector : PatchSelector
    {
        public OVRHand LeftHand, RightHand;
        public LineRenderer LeftRay, RightRay;
        public float PressStrength = 0.85f, ReleaseStrength = 0.45f;
        public float MaxDistance = 2.5f;

        bool leftDown, rightDown;

        void Update()
        {
            StereoPatch hoverL = Step(LeftHand, LeftRay, ref leftDown, "pinch-left");
            StereoPatch hoverR = Step(RightHand, RightRay, ref rightDown, "pinch-right");
            Hovered = hoverR != null ? hoverR : hoverL;
        }

        StereoPatch Step(OVRHand hand, LineRenderer line, ref bool down, string method)
        {
            bool valid = hand != null && hand.IsTracked && hand.IsPointerPoseValid;
            if (line != null) line.enabled = valid;
            if (!valid) { down = false; return null; }

            var pose = hand.PointerPose;
            var ray = new Ray(pose.position, pose.forward);
            var patch = RaycastPatch(ray, MaxDistance, out var hit);
            if (line != null)
            {
                line.SetPosition(0, ray.origin);
                line.SetPosition(1, patch != null ? hit : ray.origin + ray.direction * 0.35f);
            }

            float s = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            if (!down && s >= PressStrength)
            {
                down = true;
                Raise(patch, method);
            }
            else if (down && s <= ReleaseStrength)
            {
                down = false;
            }
            return patch;
        }
    }

    /// <summary>
    /// Eyes-only selection for players who cannot pinch: look at a patch and hold the gaze. The
    /// dwell restarts whenever the head leaves the patch, so glancing never selects.
    /// </summary>
    public sealed class HeadDwellSelector : PatchSelector
    {
        public Transform Head;
        public float DwellSeconds = 1.4f;
        public float MaxDistance = 2.5f;
        public float Progress { get; private set; }

        StereoPatch current;
        float since;

        void OnDisable() { Progress = 0; current = null; Hovered = null; }

        void Update()
        {
            if (Head == null) return;
            var p = RaycastPatch(new Ray(Head.position, Head.forward), MaxDistance, out _);
            if (p != current) { current = p; since = Time.time; }
            Hovered = current;
            Progress = current == null ? 0 : Mathf.Clamp01((Time.time - since) / DwellSeconds);
            if (current != null && Progress >= 1f)
            {
                Raise(current, "head-dwell");
                since = Time.time + 0.8f;   // refractory period before the same patch can fire again
            }
        }
    }

    /// <summary>Desk testing in the editor / XR Simulator: keys 1-4 pick a patch, mouse click picks the one under the cursor.</summary>
    public sealed class KeyboardSelector : PatchSelector
    {
        public StereoPatch[] Patches;
        public Camera Cam;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && Patches != null)
            {
                if (kb.digit1Key.wasPressedThisFrame && Patches.Length > 0) Raise(Patches[0], "key");
                if (kb.digit2Key.wasPressedThisFrame && Patches.Length > 1) Raise(Patches[1], "key");
                if (kb.digit3Key.wasPressedThisFrame && Patches.Length > 2) Raise(Patches[2], "key");
                if (kb.digit4Key.wasPressedThisFrame && Patches.Length > 3) Raise(Patches[3], "key");
            }
            var mouse = Mouse.current;
            if (mouse != null && Cam != null && mouse.leftButton.wasPressedThisFrame)
                Raise(RaycastPatch(Cam.ScreenPointToRay(mouse.position.ReadValue()), 10f, out _), "mouse");
        }
    }
}
