// Cyclopea — the creatures, once found. Soft toon shading that reads on top of passthrough, plus a
// dissolve made of ~4 mm cells so a creature condenses out of (and sinks back into) the static
// as dots. Rare creatures get a rim glow; elusive ones shimmer. Single-Pass Instanced (multiview) safe.
Shader "Cyclopea/Creature"
{
    Properties
    {
        _Color ("Colour", Color) = (0.45, 0.85, 0.8, 1)
        _Dissolve ("Dissolve (0 solid, 1 gone)", Range(0, 1)) = 0
        _Shimmer ("Shimmer (elusive)", Range(0, 1)) = 0
        _Rim ("Rim glow", Range(0, 1)) = 0.2
        _Cell ("Dissolve cell (m)", Float) = 0.0045
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Dissolve;
            half _Shimmer;
            half _Rim;
            float _Cell;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 local : TEXCOORD1;      // world-space offset from the object's origin (cells stay put as it moves)
                float3 viewWS : TEXCOORD2;     // per eye: computed in the vertex stage where the eye index is set
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.local = positionWS - TransformObjectToWorld(float3(0, 0, 0));
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceViewDir(positionWS);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 cellPos = IN.local / _Cell;
                float h = hash3(floor(cellPos) + 7.0);
                // Condensing out of the static: each cell starts as a small round white dot (like the
                // static's dots), grows, takes the creature's colour, and merges with its neighbours.
                float th = _Dissolve * 1.5;                    // 0 = all solid, 1 = nothing at all
                float g = saturate((h - th + 0.5) / 0.5);      // this cell's growth, 0..1
                // Dots stay round until the very end, then merge (0.9 > half the cell diagonal: solid).
                float r = lerp(0.18, lerp(0.9, 0.48, saturate(_Dissolve * 2.0)), g);
                clip((g > 0.0 && length(frac(cellPos) - 0.5) < r) ? 1.0 : -1.0);
                if (g < 0.45) return half4(0.93, 0.93, 0.93, 1.0);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);
                half wrap = saturate(n.y * 0.5 + 0.5);
                half3 c = _Color.rgb * (0.5 + 0.5 * wrap);
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                c += rim * _Rim;
                half3 rainbow = 0.5 + 0.5 * cos(6.28318 * (dot(n, v) * 1.5 + _Time.y * 0.25 + half3(0.0, 0.33, 0.67)));
                c = lerp(c, rainbow, 0.45 * _Shimmer);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
