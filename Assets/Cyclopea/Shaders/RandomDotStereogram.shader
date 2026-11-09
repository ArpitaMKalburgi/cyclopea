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
        _DotsPerCell ("Mean dots per cell (Poisson)", Float) = 1.0
        _Seed       ("Seed", Float) = 0
        // Lateral shift of the masked region, in UV units (full disparity, split across eyes).
        _EyeOffset  ("Eye offset (UV)", Float) = 0
        // +1 = crossed (pops toward viewer), -1 = uncrossed. Flip if the device says so.
        _OffsetSign ("Offset sign", Float) = 1
        // Motion-parallax mode: the creature's dots slide by this much in BOTH eyes as the head moves
        // (StereoPatch computes it from head position). Zero in stereo mode, so one eye plus head
        // movement can never find a stereo creature.
        _CommonShift ("Common shift, both eyes (UV)", Float) = 0
        _MaskCenter ("Mask centre (UV)", Vector) = (0.5, 0.5, 0, 0)
        _MaskRadius ("Mask radius (UV)", Float) = 0.2
        _MaskWobble ("Mask wobble", Float) = 0.15
        _MaskLobes  ("Mask lobes", Float) = 5
        // 0 = hidden creature (disparity only), 1 = decoy (no disparity, faint monocular outline)
        _Decoy      ("Decoy", Float) = 0
        // Per-eye dot contrast; used by the balance check to rebalance a suppressed eye.
        _ContrastL  ("Contrast left eye", Range(0, 1)) = 1
        _ContrastR  ("Contrast right eye", Range(0, 1)) = 1
        // Debug: -1 = real stereo eye index; 0/1 = render as if left/right eye (offline per-eye captures).
        _ForceEye   ("Force eye (-1 off)", Float) = -1
        // Debug: 1 = tint left eye red, right eye blue (device check that each eye gets its own image).
        _DebugEyeTint ("Debug eye tint", Float) = 0
        // Look: rounded corners (UV units; 0 = square) and a soft rim of light just inside the edge.
        // Both are in display UV, identical for both eyes and all four squares, so they add no cue.
        _CornerRadius ("Corner radius (UV)", Float) = 0
        _RimColor ("Rim colour (alpha = strength)", Color) = (0.55, 0.75, 1, 0)
        _RimWidth ("Rim width (UV)", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Cull Back
            ZWrite On
            // Smooth rounded edges with MSAA (the URP asset runs 4x); without MSAA it is a plain cut.
            AlphaToMask On

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
                float  _DotsPerCell;
                float  _Seed;
                float  _EyeOffset;
                float  _OffsetSign;
                float  _CommonShift;
                float4 _MaskCenter;
                float  _MaskRadius;
                float  _MaskWobble;
                float  _MaskLobes;
                float  _Decoy;
                float  _ContrastL;
                float  _ContrastR;
                float  _ForceEye;
                float  _DebugEyeTint;
                float  _CornerRadius;
                float4 _RimColor;
                float  _RimWidth;
            CBUFFER_END

            // Signed distance to the square's rounded outline in UV (negative inside).
            float patchSDF(float2 uv)
            {
                float r = _CornerRadius;
                float2 q = abs(uv - 0.5) - (0.5 - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

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

            // The dot field is a Poisson process: every cell holds a Poisson-distributed number of
            // dots (mean _DotsPerCell) at independent uniform positions. Poisson is the one field whose
            // pieces in disjoint regions are independent, so the shifted interior joined to the
            // unshifted surround is statistically identical to plain static for either eye alone:
            // no clumps or gaps along the creature's outline at any disparity.
            float poissonCount(float u)
            {
                // Inverse CDF for small means, capped at 4 dots per cell.
                float lambda = _DotsPerCell;
                float p = exp(-lambda);
                float c = p;
                float k = 0;
                [unroll] for (int i = 1; i <= 4; i++)
                {
                    if (u > c) { k = i; p *= lambda / i; c += p; }
                }
                return k;
            }

            // Coverage of fragment `uv` by dots whose displayed centre satisfies
            // `wantInside == (maskSDF(displayed) < 0)`. The field is sampled at uv + shift, so a dot
            // at field position c is displayed at c - shift.
            float coverage(float2 uv, float shift, bool wantInside)
            {
                float2 sampleUV = uv + float2(shift, 0);
                float2 baseCell = floor(sampleUV / _CellSize);
                float cov = 0;
                float aa = fwidth(uv.x);
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 cell = baseCell + float2(x, y);
                    float k = poissonCount(hash21(cell + _Seed + 3.17));
                    [unroll] for (int j = 0; j < 4; j++)
                    {
                        if (j >= k) break;
                        float2 jit = float2(hash21(cell + _Seed + 11.3 * j + 0.5), hash21(cell + _Seed + 7.9 * j + 17.0));
                        float2 c = (cell + jit) * _CellSize;
                        bool inside = maskSDF(c - float2(shift, 0)) < 0;
                        if (inside != wantInside) continue;
                        float d = length(sampleUV - c);
                        cov = max(cov, 1.0 - smoothstep(_DotRadius - aa, _DotRadius + aa, d));
                    }
                }
                return cov;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Eye 0 = left, 1 = right. Left samples at -s/2, right at +s/2 => crossed disparity.
                float eye = (_ForceEye >= 0) ? _ForceEye : (float)unity_StereoEyeIndex;
                float eyeSign = (eye < 0.5) ? -1.0 : 1.0;
                float shift = (_Decoy > 0.5) ? 0.0 : eyeSign * _OffsetSign * 0.5 * _EyeOffset + _CommonShift;

                float outsideCov = coverage(IN.uv, 0.0, false);
                float insideCov  = coverage(IN.uv, shift, true);
                float cov = max(outsideCov, insideCov);

                float contrast = (eye < 0.5) ? _ContrastL : _ContrastR;
                float3 dotCol = lerp(_Background.rgb, _DotColor.rgb, contrast);
                float3 col = lerp(_Background.rgb, dotCol, cov);

                // Decoy: a faint outline visible to either eye alone — the trap the player learns to ignore.
                if (_Decoy > 0.5)
                {
                    float sd = abs(maskSDF(IN.uv));
                    float edge = 1.0 - smoothstep(0.0, _CellSize * 0.35, sd);
                    col = lerp(col, _DotColor.rgb, edge * 0.25);
                }
                // Rounded outline and inner rim: display-space only, the same in both eyes.
                float edgeSD = patchSDF(IN.uv);
                float aaEdge = max(fwidth(edgeSD), 1e-5);
                float alpha = 1.0 - smoothstep(-aaEdge, aaEdge, edgeSD);
                float rim = (1.0 - smoothstep(0.0, _RimWidth, -edgeSD)) * _RimColor.a;
                col = lerp(col, max(col, _RimColor.rgb), rim);

                if (_DebugEyeTint > 0.5)
                    col *= (eye < 0.5) ? float3(1.0, 0.45, 0.45) : float3(0.45, 0.6, 1.0);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
