Shader "Shooter/Foundry Glow"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.02, 0.05, 0.08, 1)
        [HDR] _EmissionColor("Emission Color", Color) = (1, 0.4, 0.1, 1)
        _PulseRate("Pulse Rate (Hz)", Range(0, 4)) = 0
        _PulseDepth("Pulse Depth", Range(0, 1)) = 0.3
        _HeatIntensity("Heat Distortion", Range(0, 1)) = 0
        _NoiseScale("Energy Noise Scale", Range(0, 40)) = 8
        _NoiseStrength("Energy Noise Strength", Range(0, 1)) = 0.25
        _FresnelBoost("Fresnel Boost", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            float _PulseRate;
            half _PulseDepth;
            half _HeatIntensity;
            float _NoiseScale;
            half _NoiseStrength;
            half _FresnelBoost;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fog
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GlowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Simple 3D value noise for energy variation
            float EnergyHash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float EnergyNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = EnergyHash(i);
                float n100 = EnergyHash(i + float3(1, 0, 0));
                float n010 = EnergyHash(i + float3(0, 1, 0));
                float n110 = EnergyHash(i + float3(1, 1, 0));
                float n001 = EnergyHash(i + float3(0, 0, 1));
                float n101 = EnergyHash(i + float3(1, 0, 1));
                float n011 = EnergyHash(i + float3(0, 1, 1));
                float n111 = EnergyHash(i + float3(1, 1, 1));

                float n00 = lerp(n000, n100, f.x);
                float n10 = lerp(n010, n110, f.x);
                float n01 = lerp(n001, n101, f.x);
                float n11 = lerp(n011, n111, f.x);
                return lerp(lerp(n00, n10, f.y), lerp(n01, n11, f.y), f.z);
            }

            void GlowFragment(Varyings input, out half4 color : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint renderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 absWS = GetAbsolutePositionWS(input.positionWS);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Spatial phase offsets neighboring glow surfaces
                float phase = dot(absWS, float3(0.13, 0.64, 0.09));

                // Pulse
                half pulse = 1.0h;
                if (_PulseRate > 0.001)
                {
                    pulse = 1.0h + sin(TWO_PI * (_Time.y * _PulseRate - phase)) * _PulseDepth;
                }

                // Animated energy noise — flows across the surface
                float3 noiseCoord = absWS * _NoiseScale + float3(0, _Time.y * 0.8, 0);
                float noise = EnergyNoise(noiseCoord) * 2.0 - 1.0;
                half energyVariation = 1.0h + noise * _NoiseStrength;

                // Fresnel boost — glow intensifies at grazing angles
                half facing = saturate(dot(normalWS, viewDir));
                half fresnelMask = pow(1.0h - facing, 2.5h) * _FresnelBoost;
                half lens = lerp(0.76h, 1.0h, facing);

                // Combine emission
                half3 emission = _EmissionColor.rgb * lens * pulse * energyVariation * (1.0h + fresnelMask);

                // Heat shimmer slight color shift (subtle warm edge)
                emission += _HeatIntensity * half3(0.15h, 0.03h, 0.0h) * fresnelMask * 2.0h;

                half3 light = _BaseColor.rgb * 0.2h + emission;
                color = half4(MixFog(light, input.fog), 1.0h);
                #ifdef _WRITE_RENDERING_LAYERS
                    renderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
