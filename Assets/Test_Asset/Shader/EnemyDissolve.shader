Shader "Custom/EnemyDissolve"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _DissolveAmount("Dissolve", Range(0, 1)) = 0
        _EdgeWidth("Edge Width", Range(0.01, 0.2)) = 0.06
        _EdgeColor("Edge Color", Color) = (1, 0.35, 0.05, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _DissolveAmount;
                half _EdgeWidth;
                half4 _EdgeColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Noise đơn giản theo UV (không cần texture phụ)
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float noise = Hash21(input.uv * 48.0);
                float dissolve = _DissolveAmount;

                // Cắt dần mesh theo noise
                clip(noise - dissolve);

                // Viền cháy gần mép dissolve
                float edge = dissolve + _EdgeWidth;
                if (noise < edge)
                {
                    float edgeFactor = saturate((edge - noise) / max(_EdgeWidth, 0.0001));
                    color.rgb = lerp(color.rgb, _EdgeColor.rgb, edgeFactor);
                }

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
