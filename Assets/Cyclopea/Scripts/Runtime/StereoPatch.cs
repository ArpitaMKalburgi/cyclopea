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
        static readonly int CommonShiftId = Shader.PropertyToID("_CommonShift");

        // Global switches, changed by DevControls and the one-eye control.
        public static float OffsetSign = 1f;
        public static bool DebugEyeTint;
        public static float ContrastL = 1f, ContrastR = 1f;

        // Palette of the static (sRGB). DeskCamouflage swaps these for the player's desk colours in round 2.
        public static Color BackgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
        public static Color DotColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        // Motion mode assist: adds a gentle simulated side-to-side sway (metres, Hz) for players who
        // can't move their head. Off by default; the default motion cue is the player's own head.
        public static bool AutoSway;
        public static float SwayAmplitudeM = 0.03f, SwayHz = 0.5f;

        /// <summary>
        /// Distance between the eyes (metres) for the held-height geometry of the Catch rung. SpotSession
        /// keeps it at the rig's eye-anchor separation when the runtime reports one.
        /// </summary>
        public static double Ipd = DisparityMath.DefaultIpd;

        // Dot geometry in metres on the surface: ~3 mm dots on a ~6 mm grid read as fine static at 60 cm.
        public static float CellSizeM = 0.006f;
        public static float DotRadiusM = 0.0016f;

        [Tooltip("Disparity presented inside the creature mask, arcseconds. 0 = plain static.")]
        public double Arcsec;
        [Tooltip("Motion mode: the creature sits at the depth this many arcseconds of disparity would give, " +
                 "shown only through head-motion parallax (identical in both eyes). 0 = off.")]
        public double MotionArcsec;
        [Tooltip("Catch rung: hold the creature at this physical height above the surface (metres) however " +
                 "the head moves, like a real object, so a real fingertip can touch it. 0 = present Arcsec.")]
        public double HoldHeightM;
        public bool Decoy;
        public float Seed;
        public Vector2 MaskCenter = new Vector2(0.5f, 0.5f);
        public float MaskRadius = 0.27f;
        public float MaskWobble = 0.15f;
        public float MaskLobes = 5f;
        public int Index;

        public Transform Head;
        public double LastViewingDistance { get; private set; } = 0.6;
        public float LastCommonShiftUV { get; private set; }
        /// <summary>Height of the eyes above this square's plane (metres), from the last frame.</summary>
        public double LastEyeHeight { get; private set; } = 0.45;
        /// <summary>Surface shift between the eyes (metres) presented on the last frame.</summary>
        public double LastShiftM { get; private set; }

        /// <summary>The shift that shows the creature: held height if set, otherwise Arcsec at the current distance.</summary>
        public double ShiftNow()
        {
            if (HoldHeightM > 0 && Head != null) return Reach.ShiftForHeight(HoldHeightM, LastEyeHeight, Ipd);
            return DisparityMath.ShiftMeters(Arcsec, LastViewingDistance);
        }

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

        /// <summary>
        /// Head-motion parallax for a creature floating MotionArcsec-worth of depth above the patch:
        /// the head's sideways offset in the patch's own frame, projected through that depth.
        /// Shader convention: content is displayed at -shift, so the UV term is the negated slide.
        /// </summary>
        float MotionShiftUV(float width)
        {
            if (MotionArcsec <= 0 || Head == null) return 0f;
            Vector3 d = Head.position - transform.position;
            double zPerp = Mathf.Max(0.15f, Vector3.Dot(d, -transform.forward));
            double lateral = Vector3.Dot(d, transform.right);
            if (AutoSway) lateral += SwayAmplitudeM * Mathf.Sin(2f * Mathf.PI * SwayHz * Time.time);
            double depth = DisparityMath.PopOutMeters(MotionArcsec, zPerp);
            return (float)(-DisparityMath.MotionShiftMeters(lateral, depth, zPerp) / width);
        }

        void LateUpdate()
        {
            float width = Mathf.Max(0.01f, transform.lossyScale.x);
            if (Head != null)
            {
                LastViewingDistance = Mathf.Max(0.15f, Vector3.Distance(Head.position, transform.position));
                LastEyeHeight = Mathf.Max(0.1f, Vector3.Dot(Head.position - transform.position, -transform.forward));
            }
            LastShiftM = ShiftNow();

            meshRenderer.GetPropertyBlock(block);
            block.SetFloat(EyeOffsetId, (float)(LastShiftM / width));
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
            block.SetFloat(CommonShiftId, LastCommonShiftUV = MotionShiftUV(width));
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
