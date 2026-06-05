#ifndef UTILS_HLSL
#define UTILS_HLSL

#include "RayPayload.hlsl"

#define K_PI                    3.1415926535f
#define K_HALF_PI               1.5707963267f
#define K_QUARTER_PI            0.7853981633f
#define K_TWO_PI                6.283185307f
#define K_T_MAX                 10000
#define K_RAY_ORIGIN_PUSH_OFF   0.002

#define ENABLE_RUSSIAN_ROULETTE 1

uint WangHash(inout uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

float RandomFloat01(inout uint seed)
{
    return float(WangHash(seed)) / float(0xFFFFFFFF);
}

float3 RandomUnitVector(inout uint state)
{
    float z = RandomFloat01(state) * 2.0f - 1.0f;
    float a = RandomFloat01(state) * K_TWO_PI;
    float r = sqrt(1.0f - z * z);
    float x = r * cos(a);
    float y = r * sin(a);
    return float3(x, y, z);
}

float FresnelReflectAmountOpaque(float n1, float n2, float3 incident, float3 normal)
{
    // Schlick's aproximation
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;
    float cosX = -dot(normal, incident);
    float x = 1.0 - cosX;
    float xx = x*x;
    return r0 + (1.0 - r0)*xx*xx*x;
}

float FresnelReflectAmountTransparent(float n1, float n2, float3 incident, float3 normal)
{
    // Schlick's aproximation
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;
    float cosX = -dot(normal, incident);

    if (n1 > n2)
    {
        float n = n1 / n2;
        float sinT2 = n * n*(1.0 - cosX * cosX);
        // Total internal reflection
        if (sinT2 >= 1.0)
            return 1;
        cosX = sqrt(1.0 - sinT2);
    }

    float x = 1.0 - cosX;
    float xx = x*x;
    return r0 + (1.0 - r0)*xx*xx*x;
}

// Branchless orthonormal basis around a unit normal (Duff et al. 2017, "Building an Orthonormal Basis, Revisited").
void BuildOrthonormalBasis(float3 n, out float3 b1, out float3 b2)
{
    float sgn = n.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (sgn + n.z);
    float b = n.x * n.y * a;
    b1 = float3(1.0 + sgn * n.x * n.x * a, sgn * b, -sgn * n.x);
    b2 = float3(b, sgn + n.y * n.y * a, -n.y);
}

// Reconstructs the hit position from the triangle vertices in object space, keeping
// the barycentric interpolation in a precise edge based mad chain (van Antwerpen
// 2023, "Solving Self-Intersection Artifacts in DirectX Raytracing"). Building the
// result from the two edges and adding the base vertex last avoids the cancellation
// of the plain weighted sum of the three vertices, which perturbs the hit point and
// feeds self intersection. Feed the result through TransformObjectToWorldPositionPrecise.
float3 InterpolatePositionPrecise(float3 p0, float3 p1, float3 p2, float3 barycentrics)
{
    precise float3 edge0 = p1 - p0;
    precise float3 edge1 = p2 - p0;
    precise float3 position = p0 + mad(barycentrics.y, edge0, mul(barycentrics.z, edge1));
    return position;
}

// Transforms an object space position to world space while preserving precision in
// the result (van Antwerpen 2023, "Solving Self-Intersection Artifacts in DirectX
// Raytracing"). The translation column is added last as a separate term so it does
// not swamp the low order bits of the rotated and scaled position, and expanding the
// matrix multiply by hand keeps the rest of the work in a precise mad chain instead
// of the default mul. It complements the precise barycentric reconstruction of the
// object space position, keeping world space hit points stable far from the origin.
float3 TransformObjectToWorldPositionPrecise(float3 objectPosition)
{
    const float3x4 o2w = ObjectToWorld();

    precise float3 worldPosition;
    worldPosition.x = o2w._m03 + mad(o2w._m00, objectPosition.x, mad(o2w._m01, objectPosition.y, mul(o2w._m02, objectPosition.z)));
    worldPosition.y = o2w._m13 + mad(o2w._m10, objectPosition.x, mad(o2w._m11, objectPosition.y, mul(o2w._m12, objectPosition.z)));
    worldPosition.z = o2w._m23 + mad(o2w._m20, objectPosition.x, mad(o2w._m21, objectPosition.y, mul(o2w._m22, objectPosition.z)));
    return worldPosition;
}

float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Returns true if the path should terminate. On survival, scales throughput by 1 / survivalProbability.
bool RussianRouletteTerminate(inout float3 throughput, inout RayPayload rayPayload)
{
#if ENABLE_RUSSIAN_ROULETTE
    // Skip RR for the first few bounces so the early path, which carries most of the light, is never terminated and keeps its variance low.
    if (rayPayload.GetBounceIndexTotal() < 2)
        return false;

    // Don't let the survival probability be too low, otherwise we get fireflies (1 / p blowup).
    float survivalProbability = clamp(max(throughput.r, max(throughput.g, throughput.b)), 0.05, 0.95);

    // Dark colors have higher chance to terminate the path early.
    if (survivalProbability < RandomFloat01(rayPayload.rngState))
        return true;

    throughput *= 1 / survivalProbability;
#endif
    return false;
}

#endif // UTILS_HLSL