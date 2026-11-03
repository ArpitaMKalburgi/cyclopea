using Cyclopea.Core;
using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// One square of random-dot static. Holds the disparity to present (arcseconds) and keeps the
    /// shader's lateral shift matched to the current head-to-patch distance every frame, so the
    /// disparity the eyes receive stays constant while the player leans in or out.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class StereoPatch : MonoBehaviour
    {
        static readonly int EyeOffsetId = Shader.PropertyToID("_EyeOffset");
        static readonly int OffsetSignId = Shader.PropertyToID("_OffsetSign");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        static readonly int DotRadiusId = Shader.PropertyToID("_DotRadius");
        static readonly int MaskCenterId = Shader.PropertyToID("_MaskCenter");
        static readonly int MaskRadiusId = Shader.PropertyToID("_MaskRadius");
        static readonly int MaskWobbleId = Shader.PropertyToID("_MaskWobble");
        static readonly int MaskLobesId = Shader.PropertyToID("_MaskLobes");
        static readonly int DecoyId = Shader.PropertyToID("_Decoy");
        static readonly int ContrastLId = Shader.PropertyToID("_ContrastL");
        static readonly int ContrastRId = Shader.PropertyToID("_ContrastR");
        static readonly int DebugEyeTintId = Shader.PropertyToID("_DebugEyeTint");
        static readonly int BackgroundId = Shader.PropertyToID("_Background");
        static readonly int DotColorId = Shader.PropertyToID("_DotColor");

        // Global switches, changed by DevControls and the one-eye control.
        public static float OffsetSign = 1f;
        public static bool DebugEyeTint;
        public static float ContrastL = 1f, ContrastR = 1f;

        // Palette of the static (sRGB). DeskCamouflage swaps these for the player's desk colours in round 2.
        public static Color BackgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
        public static Color DotColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        // Dot geometry in metres on the surface: ~3 mm dots on a ~6 mm grid read as fine static at 60 cm.
        public static float CellSizeM = 0.006f;
        public static float DotRadiusM = 0.0016f;

        [Tooltip("Disparity presented inside the creature mask, arcseconds. 0 = plain static.")]
        public double Arcsec;
        public bool Decoy;
        public float Seed;
        public Vector2 MaskCenter = new Vector2(0.5f, 0.5f);
        public float MaskRadius = 0.27f;
        public float MaskWobble = 0.15f;
        public float MaskLobes = 5f;
        public int Index;

        public Transform Head;
        public double LastViewingDistance { get; private set; } = 0.6;

        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;

        void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
        }

        /// <summary>New random field and creature outline for the next trial.</summary>
        public void Reshuffle(System.Random rng)
        {
            Seed = (float)(rng.NextDouble() * 1000.0);
            MaskCenter = new Vector2(0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f,
                                     0.5f + (float)(rng.NextDouble() - 0.5) * 0.12f);
            MaskWobble = 0.08f + (float)rng.NextDouble() * 0.14f;
            MaskLobes = 3 + rng.Next(0, 4);
        }

        void LateUpdate()
        {
            float width = Mathf.Max(0.01f, transform.lossyScale.x);
            if (Head != null)
                LastViewingDistance = Mathf.Max(0.15f, Vector3.Distance(Head.position, transform.position));

            meshRenderer.GetPropertyBlock(block);
            block.SetFloat(EyeOffsetId, DisparityMath.EyeOffsetUV(Arcsec, LastViewingDistance, width));
            block.SetFloat(OffsetSignId, OffsetSign);
            block.SetFloat(SeedId, Seed);
            block.SetFloat(CellSizeId, CellSizeM / width);
            block.SetFloat(DotRadiusId, DotRadiusM / width);
            block.SetVector(MaskCenterId, MaskCenter);
            block.SetFloat(MaskRadiusId, MaskRadius);
            block.SetFloat(MaskWobbleId, MaskWobble);
            block.SetFloat(MaskLobesId, MaskLobes);
            block.SetFloat(DecoyId, Decoy ? 1f : 0f);
            block.SetFloat(ContrastLId, ContrastL);
            block.SetFloat(ContrastRId, ContrastR);
            block.SetFloat(DebugEyeTintId, DebugEyeTint ? 1f : 0f);
            block.SetColor(BackgroundId, BackgroundColor);
            block.SetColor(DotColorId, DotColor);
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
