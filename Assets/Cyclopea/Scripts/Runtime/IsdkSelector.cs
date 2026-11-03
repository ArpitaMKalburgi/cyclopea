using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// The default input, built on the Interaction SDK: point and pinch with either hand (ISDK hand
    /// rays), or look and pinch (ISDK GazeInteractor on head gaze). When a hand ray is on a square
    /// the ray wins, so one pinch can never pick two squares.
    /// </summary>
    public sealed class IsdkSelector : PatchSelector
    {
        public IsdkPatchEvents[] Patches;
        public bool AllowGaze = true;

        void OnEnable() { foreach (var p in Patches) if (p != null) p.Selected += OnSelected; }
        void OnDisable() { foreach (var p in Patches) if (p != null) p.Selected -= OnSelected; Hovered = null; }

        bool AnyRayHover()
        {
            foreach (var p in Patches) if (p != null && p.RayHovered) return true;
            return false;
        }

        void OnSelected(StereoPatch patch, string method)
        {
            if (method == "isdk-gaze" && (!AllowGaze || AnyRayHover())) return;
            Raise(patch, method);
        }

        void Update()
        {
            StereoPatch ray = null, gaze = null;
            foreach (var p in Patches)
            {
                if (p == null) continue;
                if (p.RayHovered) ray = p.Patch;
                if (p.GazeHovered) gaze = p.Patch;
            }
            Hovered = ray != null ? ray : (AllowGaze ? gaze : null);
        }
    }
}
