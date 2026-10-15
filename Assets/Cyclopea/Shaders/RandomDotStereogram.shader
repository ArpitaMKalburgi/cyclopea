// Cyclopea — procedural random-dot stereogram, per-eye, sub-pixel disparity.
//
// Each eye samples the same procedural dot field; inside the creature mask the
// sample position is shifted laterally by ±_EyeOffset/2 (sign per eye), which
// produces binocular disparity with no monocular cue. Dots are whole (never cut
// at the mask edge): membership is decided per dot centre, not per fragment,
// which is what makes the shape invisible to one eye alone.
//
// Eye index comes from unity_StereoEyeIndex — valid under Multiview and
// Single-Pass Instanced provided the stereo macros below are present.
Shader "Cyclopea/RandomDotStereogram"
{
    Properties
    {
        _Background ("Background", Color) = (0.5, 0.5, 0.5, 1)
        _DotColor   ("Dot colour", Color) = (1, 1, 1, 1)
        _CellSize   ("Cell size (UV units)", Float) = 0.02
        _DotRadius  ("Dot radius (UV units)", Float) = 0.006
        _Seed       ("Seed", Float) = 0
        // Lateral shift of the masked region, in UV units (full disparity, split across eyes).
        _EyeOffset  ("Eye offset (UV)", Float) = 0
        // +1 = crossed (pops toward viewer), -1 = uncrossed. Flip if the device says so.
        _OffsetSign ("Offset sign", Float) = 1
        _MaskCenter ("Mask centre (UV)", Vector) = (0.5, 0.5, 0, 0)
        _MaskRadius ("Mask radius (UV)", Float) = 0.2
        _MaskWobble ("Mask wobble", Float) = 0.15
        _MaskLobes  ("Mask lobes", Float) = 5
        // 0 = hidden creature (disparity only), 1 = decoy (no disparity, faint monocular outline)
        _Decoy      ("Decoy", Float) = 0
        // Per-eye dot contrast; used by the balance check to rebalance a suppressed eye.
        _ContrastL  ("Contrast left eye", Range(0, 1)) = 1
        _ContrastR  ("Contrast right eye", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON UNITY_SINGLE_PASS_STEREO
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Background;
                float4 _DotColor;
                float  _CellSize;
                float  _DotRadius;
                float  _Seed;
                float  _EyeOffset;
                float  _OffsetSign;
                float4 _MaskCenter;
                float  _MaskRadius;
                float  _MaskWobble;
                float  _MaskLobes;
                float  _Decoy;
                float  _ContrastL;
                float  _ContrastR;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Signed distance to the wobbly blob mask (negative inside).
            float maskSDF(float2 uv)
            {
                float2 d = uv - _MaskCenter.xy;
                float ang = atan2(d.y, d.x);
                float r = _MaskRadius * (1.0 + _MaskWobble * sin(_MaskLobes * ang + _Seed));
                return length(d) - r;
            }

            // Dot centre for a cell of the field with the given seed.
            float2 dotCenter(float2 cell, float seed)
            {
                float2 j = float2(hash21(cell + seed), hash21(cell + seed + 17.0));
                // Keep the whole disc inside its cell so discs never straddle cells.
                return (cell + 0.2 + 0.6 * j) * _CellSize;
            }

            // Coverage of fragment `uv` by discs of the field whose centres satisfy
            // `inside == (maskSDF(centreInSurfaceSpace) < 0)`. `shift` moves the field.
            float coverage(float2 uv, float shift, bool wantInside)
            {
                float2 sampleUV = uv + float2(shift, 0);
                float2 baseCell = floor(sampleUV / _CellSize);
                float cov = 0;
                float aa = fwidth(uv.x) * 1.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 cell = baseCell + float2(x, y);
                    float2 c = dotCenter(cell, _Seed);
                    // Where this dot sits on the surface (undo the shift) decides membership.
                    bool inside = maskSDF(c - float2(shift, 0)) < 0;
                    if (inside != wantInside) continue;
                    float d = length(sampleUV - c);
                    cov = max(cov, 1.0 - smoothstep(_DotRadius - aa, _DotRadius + aa, d));
                }
                return cov;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Eye 0 = left, 1 = right. Left samples at -s/2, right at +s/2 => crossed disparity.
                float eyeSign = (unity_StereoEyeIndex == 0) ? -1.0 : 1.0;
                float shift = (_Decoy > 0.5) ? 0.0 : eyeSign * _OffsetSign * 0.5 * _EyeOffset;

                float outsideCov = coverage(IN.uv, 0.0, false);
                float insideCov  = coverage(IN.uv, shift, true);
                float cov = max(outsideCov, insideCov);

                float contrast = (unity_StereoEyeIndex == 0) ? _ContrastL : _ContrastR;
                float3 dotCol = lerp(_Background.rgb, _DotColor.rgb, contrast);
                float3 col = lerp(_Background.rgb, dotCol, cov);

                // Decoy: a faint outline visible to either eye alone — the trap the player learns to ignore.
                if (_Decoy > 0.5)
                {
                    float sd = abs(maskSDF(IN.uv));
                    float edge = 1.0 - smoothstep(0.0, _CellSize * 0.35, sd);
                    col = lerp(col, _DotColor.rgb, edge * 0.25);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
