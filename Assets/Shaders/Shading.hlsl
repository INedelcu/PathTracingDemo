#ifndef SHADING_HLSL
#define SHADING_HLSL

#include "GlobalResources.hlsl"
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

// Traces one shadow ray for single sample next event estimation and writes the
// continuation ray plus the new throughput (payload.weight). The direct
// lighting contribution is folded into payload.emission so the ray gen
// integrator picks it up with the standard radiance += emission * throughput
// accumulation.
void ShadeOpaqueSurface(inout RayPayload payload, in SurfaceHit hit, in MaterialSample mat, float3 V)
{
    // Branch probability based on per-lobe luminance. The estimator stays unbiased for any positive probability.
    // Clamping avoids losing a lobe entirely when the other dominates.
    float specLum = Luminance(mat.F0);
    float diffLum = Luminance(mat.diffuseAlbedo);
    float specularChance = clamp(specLum / max(specLum + diffLum, 1e-7), 0.1, 0.9);

    float3 diffuseTint = mat.diffuseAlbedo * (1.0 - mat.F0);

    // Shared origin for the shadow ray (NEE) and the next bounce ray, pushed off
    // along the face normal to avoid self intersection against this surface.
    float3 hitRayOrigin = hit.worldPosition + K_RAY_ORIGIN_PUSH_OFF * hit.worldFaceNormal;

    // Single sample next event estimation: pick one light uniformly, evaluate
    // BRDF * cos in its direction, and shoot the shadow ray right here. The
    // estimator stays unbiased because we scale by the light count (1 / pickPdf).
    float3 directLight = float3(0, 0, 0);
    if (g_LightCount > 0)
    {
        uint li = min((uint)(RandomFloat01(payload.rngState) * g_LightCount), g_LightCount - 1);
        float3 wi;
        float dist;
        float3 Le;
        // Both hemisphere checks must hold: the shading normal test keeps wi in
        // the BRDF valid domain, and the face normal test keeps the shadow ray
        // on the correct side of the geometry (shading and face normals can
        // disagree on smoothed meshes).
        if (SampleLight(g_Lights[li], hit.worldPosition, payload.rngState, wi, dist, Le)
            && dot(hit.worldNormal,     wi) > 0
            && dot(hit.worldFaceNormal, wi) > 0)
        {
            float  pickPdf = 1.0 / (float)g_LightCount;
            float3 fSpec   = EvaluateSpecularGGX(V, wi, hit.worldNormal, mat.F0, mat.alpha);
            float3 fDiff   = EvaluateDiffuseLambert(diffuseTint, hit.worldNormal, wi);
            float  visible = TraceShadowRay(hitRayOrigin, wi, max(dist - K_RAY_ORIGIN_PUSH_OFF, 0.0));

            directLight = (fSpec + fDiff) * Le * visible / pickPdf;
        }
    }

    bool doSpecular = RandomFloat01(payload.rngState) < specularChance;

    float3 L;
    float3 weight;
    if (doSpecular)
    {
        if (!SampleSpecularGGX(V, hit.worldNormal, mat.F0, mat.alpha, payload.rngState, L, weight))
        {
            payload.weight = float3(0, 0, 0);
            payload.emission = mat.emission + directLight;
            payload.Terminate();
            return;
        }
        weight /= specularChance;
    }
    else
    {
        SampleDiffuseLambert(hit.worldNormal, diffuseTint, payload.rngState, L, weight);
        weight /= (1.0 - specularChance);
    }

    payload.weight = weight;
    payload.emission = mat.emission + directLight;
    payload.bounceRayOrigin = hitRayOrigin;
    payload.bounceRayDirection = L;
    payload.IncrementBounceIndexOpaque();
}

#endif // SHADING_HLSL
