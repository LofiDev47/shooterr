#ifndef SHOOTER_FOUNDRY_ALLOY_FORWARD_INCLUDED
#define SHOOTER_FOUNDRY_ALLOY_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct AlloyAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct AlloyVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 fogAndVertexLight : TEXCOORD2;
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord : TEXCOORD3;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

AlloyVaryings AlloyVertex(AlloyAttributes input)
{
    AlloyVaryings output = (AlloyVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z),
        VertexLighting(position.positionWS, output.normalWS));
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = GetShadowCoord(position);
    #endif
    return output;
}

// Concentric machining lines (lathe / CNC marks)
float MachiningMarks(float3 worldPos, float scale)
{
    float phase = dot(worldPos, float3(scale, scale * 0.37, scale * 0.71));
    float fadeDist = fwidth(phase);
    float fade = 1.0 - smoothstep(0.4, 2.0, fadeDist);
    return sin(phase) * fade;
}

// Anisotropic-like highlight boost along a procedural grain direction
half AnisotropicFactor(half3 normalWS, half3 viewDir, half3 lightDir)
{
    // Use a world-space tangent approximation (cross with up or forward)
    half3 tangent = normalize(cross(normalWS, half3(0, 1, 0)));
    half TdotH = dot(tangent, normalize(lightDir + viewDir));
    return 1.0h - abs(TdotH);
}

void AlloyFragment(AlloyVaryings input, out half4 color : SV_Target0
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

    // --- Machining micro-detail ---
    float machining = MachiningMarks(absWS, _MachiningScale) * _MachiningStrength;

    // --- Fresnel rim ---
    half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), _FresnelPower);

    // --- Compose surface ---
    half3 albedo = _BaseColor.rgb + machining * 0.1h;

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = _Metallic;
    surface.smoothness = saturate(_Smoothness + machining * 0.5h);
    surface.normalTS = half3(0, 0, 1);
    surface.occlusion = 1.0h;
    surface.alpha = 1.0h;
    // Fresnel adds a subtle colored reflection at grazing angles
    surface.emission = _FresnelTint.rgb * fresnel * 0.15h;

    // --- Lighting ---
    InputData lighting = (InputData)0;
    lighting.positionWS = input.positionWS;
    lighting.positionCS = input.positionCS;
    lighting.normalWS = normalWS;
    lighting.viewDirectionWS = viewDir;
    lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    lighting.bakedGI = SampleSH(normalWS);
    lighting.shadowMask = unity_ProbesOcclusion;
    lighting.vertexLighting = input.fogAndVertexLight.yzw;
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        lighting.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    #endif

    color = UniversalFragmentPBR(lighting, surface);

    // Add anisotropic highlight boost from main light
    Light mainLight = GetMainLight(lighting.shadowCoord);
    half anisoFactor = AnisotropicFactor(normalWS, viewDir, mainLight.direction);
    half anisoBoost = pow(anisoFactor, 4.0h) * _AnisotropyStrength * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
    color.rgb += anisoBoost * mainLight.color * 0.12h;

    color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
    #ifdef _WRITE_RENDERING_LAYERS
        renderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
