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
}
