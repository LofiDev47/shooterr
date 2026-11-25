#ifndef SHOOTER_FOUNDRY_ARMOR_FORWARD_INCLUDED
#define SHOOTER_FOUNDRY_ARMOR_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct ArmorAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ArmorVaryings
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

ArmorVaryings ArmorVertex(ArmorAttributes input)
{
    ArmorVaryings output = (ArmorVaryings)0;
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

// --- Procedural noise helpers ---

float Hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float ValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float n000 = Hash31(i);
    float n100 = Hash31(i + float3(1, 0, 0));
    float n010 = Hash31(i + float3(0, 1, 0));
    float n110 = Hash31(i + float3(1, 1, 0));
    float n001 = Hash31(i + float3(0, 0, 1));
    float n101 = Hash31(i + float3(1, 0, 1));
    float n011 = Hash31(i + float3(0, 1, 1));
    float n111 = Hash31(i + float3(1, 1, 1));

    float n00 = lerp(n000, n100, f.x);
    float n10 = lerp(n010, n110, f.x);
    float n01 = lerp(n001, n101, f.x);
    float n11 = lerp(n011, n111, f.x);
    float n0 = lerp(n00, n10, f.y);
    float n1 = lerp(n01, n11, f.y);
    return lerp(n0, n1, f.z);
}

// Multi-octave scratch pattern that varies with surface position
float ScratchPattern(float3 worldPos)
{
    // Stretch along one axis to create directional scratches
    float3 p1 = worldPos * float3(28.0, 4.0, 28.0);
    float3 p2 = worldPos * float3(4.0, 28.0, 4.0);
    float scratch1 = ValueNoise3D(p1);
    float scratch2 = ValueNoise3D(p2);

    // Sharp threshold to create line-like scratches
    float lines = max(
        smoothstep(0.72, 0.78, scratch1),
        smoothstep(0.74, 0.80, scratch2) * 0.7
    );

    // Broad wear underneath
    float wear = ValueNoise3D(worldPos * 3.6) * 2.0 - 1.0;

    return lines + wear * 0.3;
}

// Micro surface grain
float MicroGrain(float3 worldPos)
{
    float phase = dot(worldPos, float3(53.0, 97.0, 71.0));
    float detailFade = 1.0 - smoothstep(0.5, 3.0, fwidth(phase));
    return sin(phase) * detailFade;
}

void ArmorFragment(ArmorVaryings input, out half4 color : SV_Target0
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

    // --- Scratches ---
    float scratch = ScratchPattern(absWS) * _ScratchStrength;

    // --- Micro detail ---
    float grain = MicroGrain(absWS) * _MicroDetail;

    // --- Edge / rim highlight ---
    half fresnel = 1.0h - saturate(dot(normalWS, viewDir));
    half edgeRim = pow(fresnel, 3.0h) * _EdgeHighlight;

    // --- Compose albedo ---
    half3 albedo = lerp(_DarkColor.rgb, _BaseColor.rgb, 0.85h + grain * 0.15h);
    // Scratches lighten the surface (exposed lighter material underneath)
    albedo += scratch * 0.12h;
    // Edge highlight adds a subtle bright rim
    albedo += edgeRim * 0.08h;

    // --- Surface data ---
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = _Metallic + scratch * 0.15h;
    surface.smoothness = saturate(_Smoothness - scratch * 0.2h + edgeRim * 0.1h + grain * 0.05h);
    surface.normalTS = half3(0, 0, 1);
    surface.occlusion = 1.0h - scratch * 0.08h;
    surface.alpha = 1.0h;

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
    color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
    #ifdef _WRITE_RENDERING_LAYERS
        renderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
