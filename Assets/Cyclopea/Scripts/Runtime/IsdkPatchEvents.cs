using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Bridges ISDK pointer events on one square (from the hand RayInteractable and the
    /// GazeInteractable) to the game's selector interface.
    /// </summary>
    public sealed class IsdkPatchEvents : MonoBehaviour
    {
        public StereoPatch Patch;
        public RayInteractable Ray;
        public GazeInteractable Gaze;

        public event Action<StereoPatch, string> Selected;
        public bool RayHovered { get; private set; }
        public bool GazeHovered { get; private set; }

        void OnEnable()
        {
            if (Ray != null) Ray.WhenPointerEventRaised += OnRay;
            if (Gaze != null) Gaze.WhenPointerEventRaised += OnGaze;
        }

        void OnDisable()
        {
            if (Ray != null) Ray.WhenPointerEventRaised -= OnRay;
            if (Gaze != null) Gaze.WhenPointerEventRaised -= OnGaze;
            RayHovered = GazeHovered = false;
        }

        void OnRay(PointerEvent e) => Handle(e, ref rayCount, "isdk-ray", v => RayHovered = v);
        void OnGaze(PointerEvent e) => Handle(e, ref gazeCount, "isdk-gaze", v => GazeHovered = v);

        int rayCount, gazeCount;

        void Handle(PointerEvent e, ref int hovering, string method, Action<bool> setHover)
        {
            switch (e.Type)
            {
                case PointerEventType.Hover: hovering++; break;
                case PointerEventType.Unhover:
                case PointerEventType.Cancel: hovering = Mathf.Max(0, hovering - 1); break;
                case PointerEventType.Select: Selected?.Invoke(Patch, method); break;
            }
            setHover(hovering > 0);
        }
    }
}
