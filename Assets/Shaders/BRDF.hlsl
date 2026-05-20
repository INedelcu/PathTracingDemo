#ifndef BRDF_HLSL
#define BRDF_HLSL

#include "Utils.hlsl"

// Perceptual smoothness in [0,1] -> GGX roughness alpha. The squaring is the
// Disney/Burley convention also used by URP/HDRP.
float SmoothnessToAlpha(float smoothness)
{
    float roughness = 1.0 - smoothness;
    return max(roughness * roughness, 1e-4);
}

// Schlick's Fresnel approximation with colored F0 (metals + tinted dielectrics).
float3 FresnelSchlick(float3 F0, float VdotH)
{
    float x = saturate(1.0 - VdotH);
    float x2 = x * x;
    return F0 + (1.0 - F0) * (x2 * x2 * x);
}

// Smith G1 for GGX (isotropic). Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs".
float SmithG1_GGX(float NdotV, float alpha)
{
    float a2 = alpha * alpha;
    float denom = NdotV + sqrt(a2 + (1.0 - a2) * NdotV * NdotV);
    return 2.0 * NdotV / max(denom, 1e-7);
}

// Height-correlated Smith G2 for GGX (isotropic). Lower variance than the separable form.
float SmithG2_GGX_HeightCorrelated(float NdotL, float NdotV, float alpha)
{
    float a2 = alpha * alpha;
    float lambdaV = NdotL * sqrt(a2 + (1.0 - a2) * NdotV * NdotV);
    float lambdaL = NdotV * sqrt(a2 + (1.0 - a2) * NdotL * NdotL);
    return 2.0 * NdotL * NdotV / max(lambdaV + lambdaL, 1e-7);
}

// Sample the GGX visible-normal distribution (VNDF) in tangent space.
// Ve is the view direction in tangent space, +Z aligned with the shading normal.
// Heitz 2018, "Sampling the GGX Distribution of Visible Normals".
float3 SampleGGXVNDF(float3 Ve, float alpha, float u1, float u2)
{
    // Stretch the view direction so the distribution becomes hemispherical.
    float3 Vh = normalize(float3(alpha * Ve.x, alpha * Ve.y, Ve.z));

    // Orthonormal basis aligned with Vh (degenerate case when Vh ~ +Z).
    float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
    float3 T1 = lensq > 0 ? float3(-Vh.y, Vh.x, 0) * rsqrt(lensq) : float3(1, 0, 0);
    float3 T2 = cross(Vh, T1);

    // Sample a point on the projected disk and warp onto the upper hemisphere.
    float r = sqrt(u1);
    float phi = K_TWO_PI * u2;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = 0.5 * (1.0 + Vh.z);
    t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;

    float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;

    // Unstretch back to the original space.
    return normalize(float3(alpha * Nh.x, alpha * Nh.y, max(0.0, Nh.z)));
}

// GGX normal distribution. Trowbridge & Reitz 1975, Walter 2007.
float D_GGX(float NdotH, float alpha)
{
    float a2 = alpha * alpha;
    float t = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / max(K_PI * t * t, 1e-7);
}

// GGX-Smith specular BRDF evaluated at a fixed L direction, multiplied by
// cos(theta_l). Used by next event estimation where L is the direction to a
// light rather than a sampled direction.
//   f_r         = D * G2 * F / (4 * NdotV * NdotL)
//   f_r * NdotL = D * G2 * F / (4 * NdotV)
float3 EvaluateSpecularGGX(float3 V, float3 L, float3 N, float3 F0, float alpha)
{
    float NdotL = dot(N, L);
    float NdotV = dot(N, V);
    if (NdotL <= 0.0 || NdotV <= 0.0)
        return float3(0, 0, 0);

    float3 H     = normalize(V + L);
    float  NdotH = saturate(dot(N, H));
    float  VdotH = saturate(dot(V, H));

    float  D  = D_GGX(NdotH, alpha);
    float  G2 = SmithG2_GGX_HeightCorrelated(NdotL, NdotV, alpha);
    float3 F  = FresnelSchlick(F0, VdotH);
    return F * (D * G2 / max(4.0 * NdotV, 1e-7));
}

// Lambert BRDF * cos(theta_l). The albedo is the diffuse albedo (already
// tinted by (1 - F0) when the caller does energy compensation).
float3 EvaluateDiffuseLambert(float3 albedo, float3 N, float3 L)
{
    float NdotL = saturate(dot(N, L));
    return albedo * (NdotL / K_PI);
}

// Sample the GGX-Smith specular lobe with VNDF importance sampling.
// V:   outgoing direction in world space (toward the camera).
// N:   shading normal in world space.
// F0:  Fresnel reflectance at normal incidence (per channel).
// alpha: GGX roughness (already squared from perceptual smoothness).
// Outputs the bounced ray direction L and the Monte Carlo throughput weight.
// Returns false when the sampled direction is below the horizon — the caller
// should treat the path as terminated rather than propagating an invalid ray.
bool SampleSpecularGGX(float3 V, float3 N, float3 F0, float alpha, inout uint rngState, out float3 L, out float3 weight)
{
    float3 T, B;
    BuildOrthonormalBasis(N, T, B);

    float3 Ve = float3(dot(V, T), dot(V, B), dot(V, N));

    float u1 = RandomFloat01(rngState);
    float u2 = RandomFloat01(rngState);
    float3 Hts = SampleGGXVNDF(Ve, alpha, u1, u2);

    float3 H = Hts.x * T + Hts.y * B + Hts.z * N;
    L = reflect(-V, H);

    float NdotL = dot(N, L);
    float NdotV = dot(N, V);
    if (NdotL <= 0.0 || NdotV <= 0.0)
    {
        weight = float3(0, 0, 0);
        return false;
    }

    float VdotH = saturate(dot(V, H));
    float3 F = FresnelSchlick(F0, VdotH);

    // VNDF Monte Carlo weight collapses to F * G2 / G1 (Heitz 2018, eq. 19).
    float G1 = SmithG1_GGX(NdotV, alpha);
    float G2 = SmithG2_GGX_HeightCorrelated(NdotL, NdotV, alpha);
    weight = F * (G2 / max(G1, 1e-7));
    return true;
}

// Cosine-weighted Lambertian sample. The pdf cos(theta)/PI cancels the
// albedo/PI BRDF factor, so the throughput weight is the diffuse albedo.
// The normalize(N + random_unit_vector) trick is from Ertl 2010, "Numerical
// Methods for Topography Simulation", PhD thesis, TU Wien, §5.3.4, eq. (5.53).
void SampleDiffuseLambert(float3 N, float3 albedo, inout uint rngState, out float3 L, out float3 weight)
{
    // Fall back to L = N when the random vector is near antiparallel to N, so
    // normalize doesn't hit 0/0 and produce NaNs that the accumulator locks in.
    float3 s = N + RandomUnitVector(rngState);
    L = dot(s, s) < 1e-6 ? N : normalize(s);
    weight = albedo;
}

// Sample the GGX-Smith dielectric BSDF (rough glass) with VNDF importance sampling.
// rayDir: incoming ray direction in world space (points into the surface).
// N:      macro-surface normal in world space, oriented so dot(N, -rayDir) > 0.
// etaI:   IOR on the incident side. etaT: IOR on the transmitted side.
// alpha:  GGX roughness (already squared from the perceptual control).
// Outputs the bounced ray direction L, the throughput weight, and isReflected
// so the caller can pick the correct surface push-off direction. With VNDF the
// branch probability cancels the Fresnel factor, leaving weight = G2 / G1 in
// both the reflection and refraction branches (Walter 2007, Heitz 2018).
// Returns false when the sampled direction is degenerate (treat as terminated).
bool SampleGlassGGX(float3 rayDir, float3 N, float etaI, float etaT, float alpha, inout uint rngState, out float3 L, out float3 weight, out bool isReflected)
{
    float3 V = -rayDir;

    float3 T, B;
    BuildOrthonormalBasis(N, T, B);

    float3 Ve = float3(dot(V, T), dot(V, B), dot(V, N));

    float u1 = RandomFloat01(rngState);
    float u2 = RandomFloat01(rngState);
    float3 Hts = SampleGGXVNDF(Ve, alpha, u1, u2);
    float3 H = Hts.x * T + Hts.y * B + Hts.z * N;

    float F = FresnelReflectAmountTransparent(etaI, etaT, rayDir, H);

    if (RandomFloat01(rngState) < F)
    {
        L = reflect(rayDir, H);
        isReflected = true;
    }
    else
    {
        L = refract(rayDir, H, etaI / etaT);
        isReflected = false;
        // Total internal reflection safety net: refract returns zero on TIR.
        if (dot(L, L) < 1e-6)
        {
            L = reflect(rayDir, H);
            isReflected = true;
        }
    }

    float NdotV = dot(N, V);
    float NdotL = abs(dot(N, L));
    if (NdotV <= 0.0 || NdotL <= 0.0)
    {
        weight = float3(0, 0, 0);
        return false;
    }

    float G1 = SmithG1_GGX(NdotV, alpha);
    float G2 = SmithG2_GGX_HeightCorrelated(NdotL, NdotV, alpha);
    weight = (G2 / max(G1, 1e-7)).xxx;
    return true;
}

#endif // BRDF_HLSL
