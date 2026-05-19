#ifndef SHADING_HLSL
#define SHADING_HLSL

#include "BRDF.hlsl"
#include "Utils.hlsl"
#include "RayPayload.hlsl"

// Geometry for one opaque hit, in world space. Filled by LoadSurfaceHit.
struct SurfaceHit
{
    float3 worldPosition;
    float3 worldNormal;
    float3 worldFaceNormal;
    float2 uv;
    bool isFrontFace;
};

// Decoded BSDF parameters for one opaque hit. Filled by EvaluateMaterial.
struct MaterialSample
{
    float3 diffuseAlbedo;
    float3 F0;
    float alpha;
    float3 emission;
};

// Writes the deferred NEE params, the continuation ray, throughput (albedo).
void ShadeOpaqueSurface(inout RayPayload payload, in SurfaceHit hit, in MaterialSample mat, float3 V)
{
    // Branch probability based on per-lobe luminance. The estimator stays unbiased for any positive probability.
    // Clamping avoids losing a lobe entirely when the other dominates.
    float specLum = Luminance(mat.F0);
    float diffLum = Luminance(mat.diffuseAlbedo);
    float specularChance = clamp(specLum / max(specLum + diffLum, 1e-7), 0.1, 0.9);

    bool doSpecular = RandomFloat01(payload.rngState) < specularChance;

    float3 L;
    float3 weight;
    if (doSpecular)
    {
        if (!SampleSpecularGGX(V, hit.worldNormal, mat.F0, mat.alpha, payload.rngState, L, weight))
        {
            payload.albedo = float3(0, 0, 0);
            payload.emission = mat.emission;
            payload.Terminate();
            return;
        }
        weight /= specularChance;
    }
    else
    {
        float3 diffuseTint = mat.diffuseAlbedo * (1.0 - mat.F0);
        SampleDiffuseLambert(hit.worldNormal, diffuseTint, payload.rngState, L, weight);
        weight /= (1.0 - specularChance);
    }

    payload.albedo = weight;
    payload.emission = mat.emission;
    payload.IncrementBounceIndexOpaque();
    payload.bounceRayOrigin = hit.worldPosition + K_RAY_ORIGIN_PUSH_OFF * hit.worldFaceNormal;
    payload.bounceRayDirection = L;
}

#endif // SHADING_HLSL