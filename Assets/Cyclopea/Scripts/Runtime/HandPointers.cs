using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// What the hands look like while the Interaction SDK is the input. A pointing line from each tracked hand that
    /// is always there (to the square it points at, or a short fading line when it points at nothing), and Meta's
    /// own hand model as a soft outline over the real hand. Before, the line only showed over some places and the
    /// hands were bare passthrough, so a player could not tell where a pinch would land.
    /// Added at run time by <see cref="IsdkSelector"/>; it reuses the lines and hands already wired to
    /// <see cref="HandRaySelector"/>. Looks only: it never chooses a square.
    /// </summary>
    public sealed class HandPointers : MonoBehaviour
    {
        public float FreeLength = 0.6f, MaxDistance = 2.5f, Width = 0.003f;
        public Color Near = new Color(1f, 1f, 1f, 0.95f), Far = new Color(1f, 1f, 1f, 0.25f);
        public Color HandTint = new Color(0.62f, 0.86f, 1f, 1f);

        /// <summary>How many of the SDK's own ray pictures were switched off (ours replaces them).</summary>
        public int IsdkRayRenderersHidden { get; private set; }
        /// <summary>How many hand meshes got the outline.</summary>
        public int HandsOutlined { get; private set; }
        public bool LeftShown => left != null && left.enabled;
        public bool RightShown => right != null && right.enabled;

        HandRaySelector raw;
        LineRenderer left, right;
        Material lineMat, glow;

        void Start()
        {
            raw = GetComponent<HandRaySelector>();
            if (raw != null) { left = raw.LeftRay; right = raw.RightRay; }
            foreach (var line in new[] { left, right })
            {
                if (line == null) continue;
                if (lineMat == null && line.sharedMaterial != null)
                {
                    // Its own copy, drawn after everything else, so the static can never paint over the line.
                    lineMat = new Material(line.sharedMaterial) { renderQueue = 4000 };
                }
                if (lineMat != null) line.sharedMaterial = lineMat;
                line.widthMultiplier = Width;
                line.startColor = Near;
                line.endColor = Far;
                line.numCapVertices = 4;
            }
            HideSdkRays();
            OutlineHands();
            Debug.Log("[Cyclopea] hand pointers: lines " + (left != null && right != null ? "2" : "missing") +
                      ", sdk ray pictures hidden " + IsdkRayRenderersHidden + ", hands outlined " + HandsOutlined);
        }

        void OnDestroy()
        {
            if (lineMat != null) Destroy(lineMat);
            if (glow != null) Destroy(glow);
        }

        /// <summary>The SDK's hand rays keep working as input; only their pictures go, so there is one line, not two.</summary>
        void HideSdkRays()
        {
            foreach (var ri in FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (UnderController(ri.transform)) continue;
                foreach (var r in ri.GetComponentsInChildren<Renderer>(true))
                {
                    r.forceRenderingOff = true;
                    IsdkRayRenderersHidden++;
                }
            }
        }

        static bool UnderController(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.Contains("Controller")) return true;
            return false;
        }

        /// <summary>
        /// The SDK's hand meshes are depth masks (the real hand cuts the static). They keep that and get a second
        /// material: a soft outline, so the tracked hand is visible as Meta's hand model.
        /// </summary>
        void OutlineHands()
        {
            var coach = GetComponent<SlapCoach>();
            if (coach == null) coach = FindFirstObjectByType<SlapCoach>(FindObjectsInactive.Include);
            if (coach == null || coach.Material == null) return;
            glow = new Material(coach.Material) { renderQueue = 3010 };
            glow.SetColor("_Tint", HandTint);
            glow.SetFloat("_Fill", 0.10f);
            glow.SetFloat("_RimPower", 2.0f);
            glow.SetFloat("_Alpha", 0.85f);
            foreach (var smr in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var m = smr.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "Cyclopea/HandOccluder") continue;
                var mats = new List<Material>(smr.sharedMaterials);
                if (mats.Contains(glow)) continue;
                mats.Add(glow);
                smr.sharedMaterials = mats.ToArray();
                HandsOutlined++;
            }
        }

        void LateUpdate()
        {
            if (raw == null) return;
            if (raw.enabled) return;   // the backup input draws these lines itself
            Draw(raw.LeftHand, left);
            Draw(raw.RightHand, right);
        }

        void Draw(OVRHand hand, LineRenderer line)
        {
            if (line == null) return;
            bool valid = hand != null && hand.IsTracked && hand.IsPointerPoseValid;
            line.enabled = valid;
            if (!valid) return;
            var pose = hand.PointerPose;
            Vector3 from = pose.position, dir = pose.forward;
            Vector3 to = from + dir * FreeLength;
            if (Physics.Raycast(new Ray(from, dir), out var h, MaxDistance, ~0, QueryTriggerInteraction.Collide) &&
                h.collider.GetComponentInParent<StereoPatch>() != null)
            {
                to = h.point;
            }
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }
    }
}
