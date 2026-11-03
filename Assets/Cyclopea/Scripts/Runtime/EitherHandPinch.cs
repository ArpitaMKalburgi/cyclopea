using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// ISDK selector that fires on an index pinch from either hand, with hysteresis. Drives the
    /// GazeInteractor: look at a square, pinch anywhere (hand can rest in the lap).
    /// </summary>
    public sealed class EitherHandPinch : MonoBehaviour, ISelector
    {
        public OVRHand LeftHand, RightHand;
        public float Press = 0.85f, Release = 0.45f;
        public event Action WhenSelected = delegate { };
        public event Action WhenUnselected = delegate { };
        public bool IsDown { get; private set; }

        void Update()
        {
            float s = Mathf.Max(Strength(LeftHand), Strength(RightHand));
            if (!IsDown && s >= Press) { IsDown = true; WhenSelected(); }
            else if (IsDown && s <= Release) { IsDown = false; WhenUnselected(); }
        }

        static float Strength(OVRHand h) =>
            h != null && h.IsTracked ? h.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
    }
}
