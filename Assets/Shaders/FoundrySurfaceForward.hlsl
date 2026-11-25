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

// Distance LOD: screen-space derivatives explode at the grazing angles a
// first-person camera always has on a floor, which used to erase the tread a few
// metres out. Distance keeps the plate readable without turning the horizon to noise.
float TreadVisibility(float2 positionXZ)
{
    float cameraDistance = distance(GetCameraPositionWS(), float3(positionXZ.x, 0.0, positionXZ.y));
    return 1.0 - smoothstep(26.0, 58.0, cameraDistance);
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
    float antialias = clamp(footprint * 0.65, 0.008, 0.09);
    float tread = 1.0 - smoothstep(0.012 - antialias, 0.012 + antialias, distanceToLug);
    float bevel = smoothstep(-0.025 - antialias, -0.025 + antialias, distanceToLug);
    float lightSide = smoothstep(-0.04, 0.04, lug.y);
    float visibility = TreadVisibility(positionXZ);
    mask = tread * visibility;
    relief = mask * (0.10 + bevel * (lightSide - 0.6) * 0.8);
}

// Smooth lug silhouette (0 outside, 1 on top of a lug). Sampled at neighbouring
// positions to rebuild a normal, so the plate actually catches the light instead
// of only tinting the albedo.
float TreadHeight(float2 positionXZ, float antialias)
{
    float2 grid = positionXZ * 3.6;
    float2 cell = floor(grid);
    float2 local = frac(grid) - 0.5;
    float direction = 4.0 * frac((cell.x + cell.y) * 0.5) - 1.0;
    float2 lug = float2(local.x + local.y * direction, local.y - local.x * direction) * 0.7071;
    float2 edge = abs(lug) - float2(0.23, 0.045);
    float distanceToLug = length(max(edge, 0.0)) + min(max(edge.x, edge.y), 0.0);
    return 1.0 - smoothstep(-0.012 - antialias, -0.012 + antialias, distanceToLug);
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
        dot(positionWS, float3(6.2, 7.8, 2.6)),
        dot(positionWS, float3(-3.6, 4.4, 6.8)));
    float2 cell = floor(samplePosition);
    float2 blend = smoothstep(0.0, 1.0, frac(samplePosition));
    float lower = lerp(WearCell(cell), WearCell(cell + float2(1, 0)), blend.x);
    float upper = lerp(WearCell(cell + float2(0, 1)), WearCell(cell + float2(1, 1)), blend.x);
    float2 footprint = fwidth(samplePosition);
    float fade = 1.0 - smoothstep(0.4, 1.4, max(footprint.x, footprint.y));
    return (lerp(lower, upper, blend.y) * 2.0 - 1.0) * fade;
}

// Panel seams on flat structure: a 2.4 m grid projected onto the dominant face
// axis, so walls and crates read as assembled plate rather than solid colour.
float PanelSeam(float3 positionWS, half3 normalWS, out float seamHighlight)
{
    float3 an = abs(normalWS);
    float2 planeUV = an.y > max(an.x, an.z) ? positionWS.xz
        : an.x > an.z ? positionWS.zy : positionWS.xy;

    float2 grid = planeUV / 2.4;
    float2 f = abs(frac(grid) - 0.5);
    float2 seamDistance = (0.5 - f) * 2.4;
    float distanceToSeam = min(seamDistance.x, seamDistance.y);

    float footprint = length(fwidth(planeUV));
    float antialias = clamp(footprint * 0.6, 0.004, 0.05);
    float seam = 1.0 - smoothstep(0.012 - antialias, 0.012 + antialias, distanceToSeam);
    seamHighlight = seam * smoothstep(0.012, 0.05, distanceToSeam);
    return seam;
}

SurfaceData FoundrySurface(float3 positionWS, half3 normalWS, out half3 perturbedNormalWS)
{
    perturbedNormalWS = normalWS;
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

        // Rebuild the lug edge as a real slope so the plate reads in the lighting,
        // not just as a brighter albedo patch.
        float footprint = max(fwidth(positionWS.x * 3.6), fwidth(positionWS.z * 3.6));
        float antialias = clamp(footprint * 0.65, 0.008, 0.09);
        float visibility = TreadVisibility(positionWS.xz);
        float eps = 0.012;
        float h0 = TreadHeight(positionWS.xz, antialias);
        float hx = TreadHeight(positionWS.xz + float2(eps, 0.0), antialias);
        float hz = TreadHeight(positionWS.xz + float2(0.0, eps), antialias);
        float2 slope = float2(hx - h0, hz - h0) / eps;
        float2 tilt = slope / (length(slope) + 24.0);
        half bump = 0.5h * upwardTread * visibility;
        perturbedNormalWS = normalize(normalWS + half3(-tilt.x, 0.0h, -tilt.y) * bump);
    }

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = _BaseColor.rgb * (1.0h + (broadWear * 0.22h + grain * 0.07h) * _WearStrength + treadRelief);

    // Structural plate: seams on vertical faces, where no tread plate is visible.
    half verticalness = 1.0h - smoothstep(0.4h, 0.85h, normalWS.y);

    if (verticalness > 0.001h)
    {
        float seamHighlight;
        float seam = PanelSeam(positionWS, normalWS, seamHighlight);
        surface.albedo *= lerp(1.0h, 1.0h - seam * 0.42h + seamHighlight * 0.08h, verticalness);
    }

    surface.metallic = _Metallic;
    surface.smoothness = saturate(_Smoothness + (broadWear * 0.18h + grain * 0.08h) * _WearStrength
        + treadMask * 0.2h);
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

    half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
    half3 shadedNormalWS = normalWS;

    InputData lighting = (InputData)0;
    lighting.positionWS = input.positionWS;
    lighting.positionCS = input.positionCS;
    lighting.normalWS = normalWS;
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

    SurfaceData surface = FoundrySurface(GetAbsolutePositionWS(input.positionWS), lighting.normalWS, shadedNormalWS);
    lighting.normalWS = shadedNormalWS;
    color = UniversalFragmentPBR(lighting, surface);
    color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
    #ifdef _WRITE_RENDERING_LAYERS
        renderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
