using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
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
}
