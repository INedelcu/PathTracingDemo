#ifndef UTILS_HLSL
#define UTILS_HLSL

#define K_PI                    3.1415926535f
#define K_HALF_PI               1.5707963267f
#define K_QUARTER_PI            0.7853981633f
#define K_TWO_PI                6.283185307f
#define K_T_MAX                 10000
#define K_RAY_ORIGIN_PUSH_OFF   0.002

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

float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Returns true if the path should terminate. On survival, scales throughput by 1 / survivalProbability.
bool RussianRouletteTerminate(inout float3 throughput, inout uint rngState)
{
    // Don't let the survival probability be too low, otherwise we get fireflies (1 / p blowup).
    float survivalProbability = clamp(max(throughput.r, max(throughput.g, throughput.b)), 0.05, 0.95);

    // Dark colors have higher chance to terminate the path early.
    if (survivalProbability < RandomFloat01(rngState))
        return true;

    throughput *= 1 / survivalProbability;
    return false;
}

#endif // UTILS_HLSL