using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cyclopea
{
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
