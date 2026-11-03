using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
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
}
