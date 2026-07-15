#ifndef SHADING_HLSL
#define SHADING_HLSL

#include "GlobalResources.hlsl"
#include "BRDF.hlsl"
#include "Utils.hlsl"
#include "RayPayload.hlsl"

// GGX single-scatter directional albedo E_ss(NdotV, perceptualRoughness) with Fresnel=1
Texture2D<float>  g_EnergyCompLUT;
SamplerState sampler_g_EnergyCompLUT;

// Geometry for one opaque hit, in world space. Filled by LoadSurfaceHit.
struct SurfaceHit
{
    float3 worldPosition;
    float3 worldNormal;       // Shading normal: interpolated vertex normal, perturbed by the normal map when one is assigned.
    float3 worldVertexNormal; // Interpolated vertex normal before any normal map perturbation.
    float3 worldFaceNormal;   // Triangle face normal, flipped to the ray side on back face hits.
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

// Multiple-scattering energy compensation for the specular lobe (Kulla & Conty 2017,
// Turquin 2019). Single-scatter GGX (weight F * G2 / G1) drops the light that scatters
// more than once across the microsurface, losing up to about half the energy at high
// roughness - the white furnace test makes this visible. E_ss is the single-scatter
// directional albedo with F = 1 (from the baked LUT), so 1 - E_ss is the lost fraction;
// scaling the lobe by this factor puts it back, tinted by the average Fresnel of the
// extra bounces. A metal has no diffuse lobe to carry the (1 - F0) tint, so without this
// it comes out too dark, the more so the rougher and more saturated it is.
float3 SpecularEnergyCompensation(float3 F0, float NdotV, float alpha)
{
    float  Ess  = g_EnergyCompLUT.SampleLevel(sampler_g_EnergyCompLUT, float2(NdotV, sqrt(alpha)), 0);
    float3 Favg = F0 + (1.0 - F0) * (1.0 / 21.0); // cosine-weighted Schlick average, (20 F0 + 1) / 21
    return 1.0 + Favg * (1.0 - Ess) / max(Ess, 1e-3);
}

void ShadeOpaqueSurface(inout RayPayload payload, in SurfaceHit hit, in MaterialSample mat, float3 V)
{
    // Branch probability based on per-lobe luminance.
    // Clamping avoids losing a lobe entirely when the other dominates.
    float specLum = Luminance(mat.F0);
    float diffLum = Luminance(mat.diffuseAlbedo);
    float specularChance = clamp(specLum / max(specLum + diffLum, 1e-7), 0.1, 0.9);

    float3 diffuseTint = mat.diffuseAlbedo * (1.0 - mat.F0);

#if NORMAL_MAP_ON
    // Specular lobe uses a shading normal bent so the mirror reflection of V stays above the smooth surface.
    // With the raw normal, strong normal map perturbations reflect view rays into the surface and the rejected
    // samples render as black patches. Identity when the reflection is valid.
    // Like the terminator factor, the reference is the interpolated vertex
    // normal: using the geometry normal will result in a discontinuity at triangle edges and visible
    // seams in reflections on coarse meshes.
    float3 specularNormal = ComputeConsistentShadingNormal(V, hit.worldVertexNormal, hit.worldNormal);
#else
    // Without a normal map the shading normal is the vertex normal, so the bent
    // normal would equal it: skip the work and use the shading normal directly.
    float3 specularNormal = hit.worldNormal;
#endif

    // Specular energy-compensation multiplier. It depends only on the view angle,
    // roughness and F0 (not the sampled direction), so evaluate it once and apply the
    // same factor to the next event estimate and the sampled bounce.
#if ENERGY_COMPENSATION_ON
    float  NdotV = saturate(dot(specularNormal, V));
    float3 specularEnergyComp = SpecularEnergyCompensation(mat.F0, NdotV, mat.alpha);
#else
    float3 specularEnergyComp = 1.0;
#endif

    float3 hitRayOrigin = OffsetRayOrigin(hit.worldPosition, hit.worldFaceNormal);

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
            float3 fSpec   = EvaluateSpecularGGX(V, wi, specularNormal, mat.F0, mat.alpha) * specularEnergyComp;
            float3 fDiff   = EvaluateDiffuseLambert(diffuseTint, hit.worldNormal, wi);
            float3 shadowRayOrigin = OffsetRayOrigin(hit.worldPosition, hit.worldFaceNormal, K_SHADOW_RAY_OFFSET_SCALE);
            float  visible = TraceShadowRay(shadowRayOrigin, wi, dist * (1.0 - K_SHADOW_RAY_T_EPSILON));

            directLight = (fSpec + fDiff) * Le * visible / pickPdf;

#if NORMAL_MAP_ON
            // Terminator factor (Chiang et al. 2019) - ramps the contribution to zero where the light drops
            // below the smooth surface horizon so the normal map perturbation does not clip to black in a
            // harsh band. The interpolated vertex normal is the reference, so only the normal map deviation is corrected.
            directLight *= ShadowTerminatorTerm(hit.worldVertexNormal, hit.worldNormal, wi);
#endif
        }
    }

    bool doSpecular = RandomFloat01(payload.rngState) < specularChance;

    float3 L;
    float3 weight;
    if (doSpecular)
    {
        if (!SampleSpecularGGX(V, specularNormal, mat.F0, mat.alpha, payload.rngState, L, weight))
        {
            payload.weight = float3(0, 0, 0);
            payload.emission = mat.emission + directLight;
            payload.Terminate();
            return;
        }
        weight *= specularEnergyComp;
        weight /= specularChance;
    }
    else
    {
        SampleDiffuseLambert(hit.worldNormal, diffuseTint, payload.rngState, L, weight);
        weight /= (1.0 - specularChance);
    }

#if NORMAL_MAP_ON
    // The terminator discontinuity exists for the sampled bounce direction
    // too, so the same factor keeps indirect lighting consistent with the
    // corrected direct lighting. A zero factor means the sample fell below
    // the smooth surface horizon and would contribute nothing: end the path
    // instead of tracing a ray with zero throughput.
    float bounceTerminator = ShadowTerminatorTerm(hit.worldVertexNormal, hit.worldNormal, L);
    if (bounceTerminator <= 0.0)
    {
        payload.weight = float3(0, 0, 0);
        payload.emission = mat.emission + directLight;
        payload.Terminate();
        return;
    }
    weight *= bounceTerminator;
#endif

    payload.weight = weight;
    payload.emission = mat.emission + directLight;
    payload.bounceRayOrigin = hitRayOrigin;
    payload.bounceRayDirection = L;
    payload.IncrementBounceIndexOpaque();
}

#endif // SHADING_HLSL
