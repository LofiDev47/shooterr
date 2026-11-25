#ifndef SHOOTER_FOUNDRY_SURFACE_FORWARD_INCLUDED
#define SHOOTER_FOUNDRY_SURFACE_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct SurfaceAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SurfaceVaryings
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

SurfaceVaryings SurfaceVertex(SurfaceAttributes input)
{
    SurfaceVaryings output = (SurfaceVaryings)0;
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

void GetTread(float2 positionXZ, out half mask, out half relief)
{
    // Alternate the direction of each raised lug across the grid.
    float2 grid = positionXZ * 3.6;
    float2 cell = floor(grid);
    float2 local = frac(grid) - 0.5;
    float direction = 4.0 * frac((cell.x + cell.y) * 0.5) - 1.0;
    float2 lug = float2(local.x + local.y * direction, local.y - local.x * direction) * 0.7071;
    float2 edge = abs(lug) - float2(0.23, 0.045);
    float distanceToLug = length(max(edge, 0.0)) + min(max(edge.x, edge.y), 0.0);
    float footprint = max(fwidth(grid.x), fwidth(grid.y));
    float antialias = max(0.008, footprint * 0.65);
    float tread = 1.0 - smoothstep(0.012 - antialias, 0.012 + antialias, distanceToLug);
    float bevel = smoothstep(-0.025 - antialias, -0.025 + antialias, distanceToLug);
    float lightSide = smoothstep(-0.04, 0.04, lug.y);
    float visibility = 1.0 - smoothstep(0.08, 0.34, footprint);
    mask = tread * visibility;
    relief = mask * (0.07 + bevel * (lightSide - 0.6) * 0.8);
}

float WearCell(float2 cell)
{
    float3 hash = frac(cell.xyx * float3(0.1031, 0.1137, 0.1379));
    hash += dot(hash, hash.yzx + 33.33);
    return frac((hash.x + hash.y) * hash.z);
}

float SampleWear(float3 positionWS)
{
    float2 samplePosition = float2(
        dot(positionWS, float3(3.1, 3.9, 1.3)),
        dot(positionWS, float3(-1.8, 2.2, 3.4)));
    float2 cell = floor(samplePosition);
    float2 blend = smoothstep(0.0, 1.0, frac(samplePosition));
    float lower = lerp(WearCell(cell), WearCell(cell + float2(1, 0)), blend.x);
    float upper = lerp(WearCell(cell + float2(0, 1)), WearCell(cell + float2(1, 1)), blend.x);
    float2 footprint = fwidth(samplePosition);
    float fade = 1.0 - smoothstep(0.4, 1.4, max(footprint.x, footprint.y));
    return (lerp(lower, upper, blend.y) * 2.0 - 1.0) * fade;
}

SurfaceData FoundrySurface(float3 positionWS, half3 normalWS)
{
    float broadWear = SampleWear(positionWS);
    float grainPhase = dot(positionWS, float3(27.0, 108.0, 43.0)) + broadWear * 4.0;
    float detailFade = 1.0 - smoothstep(0.45, 2.6, fwidth(grainPhase));
    float grain = sin(grainPhase) * detailFade;

    half treadRelief = 0.0h;
    half treadMask = 0.0h;
    if (_TreadStrength > 0.0h)
    {
        GetTread(positionWS.xz, treadMask, treadRelief);
        half upwardTread = smoothstep(0.55h, 0.86h, normalWS.y) * _TreadStrength;
        treadMask *= upwardTread;
        treadRelief *= upwardTread;
    }

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = _BaseColor.rgb * (1.0h + (broadWear * 0.36h + grain * 0.09h) * _WearStrength + treadRelief);
    surface.metallic = _Metallic;
    surface.smoothness = saturate(_Smoothness + (broadWear * 0.24h + grain * 0.1h) * _WearStrength
        + treadMask * 0.16h);
    surface.normalTS = half3(0, 0, 1);
    surface.occlusion = 1.0h;
    surface.alpha = 1.0h;
    return surface;
}

void SurfaceFragment(SurfaceVaryings input, out half4 color : SV_Target0
    #ifdef _WRITE_RENDERING_LAYERS
        , out uint renderingLayers : SV_Target1
    #endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    InputData lighting = (InputData)0;
    lighting.positionWS = input.positionWS;
    lighting.positionCS = input.positionCS;
    lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
    lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    lighting.bakedGI = SampleSH(lighting.normalWS);
    lighting.shadowMask = unity_ProbesOcclusion;
    lighting.vertexLighting = input.fogAndVertexLight.yzw;
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        lighting.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    #endif

    SurfaceData surface = FoundrySurface(GetAbsolutePositionWS(input.positionWS), lighting.normalWS);
    color = UniversalFragmentPBR(lighting, surface);
    color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
    #ifdef _WRITE_RENDERING_LAYERS
        renderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
