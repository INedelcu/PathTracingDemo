#define K_PI                    3.1415926535f
#define K_HALF_PI               1.5707963267f
#define K_QUARTER_PI            0.7853981633f
#define K_TWO_PI                6.283185307f
#define K_T_MAX                 10000
#define K_RAY_ORIGIN_PUSH_OFF   0.002

#include "BluenoiseSampling.hlsl"
#define USE_BLUENOISE_SAMPLING

#define NB_RAND_BOUNCE 4

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

// Map sample on square to disk (http://psgraphics.blogspot.com/2011/01/improved-code-for-concentric-map.html)
static float2 MapSquareToDisk(float2 uv)
{
    //Code flow makes sure that division by 0 and thus NaNs cannot happen.
    float phi;
    float r;

    float a = uv.x * 2.0f - 1.0f;
    float b = uv.y * 2.0f - 1.0f;

    if (a * a > b * b)
    {
        r = a;
        phi = (K_QUARTER_PI) * (b / a);
    }
    else
    {
        r = b;

        if (b == 0.0f)
        {
            phi = K_HALF_PI;
        }
        else
        {
            phi = (K_HALF_PI) - (K_QUARTER_PI) * (a / b);
        }
    }

    return float2(r * cos(phi), r * sin(phi));
}

float3 HemisphereCosineSample(float2 rand)
{
    float2 diskSample = MapSquareToDisk(rand);
    return float3(diskSample.x, diskSample.y, sqrt(1.0f - dot(diskSample, diskSample)));
}

void CreateOrthoNormalBasis(in float3 n, inout float3 tangent, inout float3 bitangent)
{
    const float sign = n.z >= 0.0f ? 1.0f : -1.0f;
    const float a    = -1.0f / (sign + n.z);
    const float b    = n.x * n.y * a;

    tangent   = float3(1.0f + sign * n.x * n.x * a, sign * b, -sign * n.x);
    bitangent = float3(b, sign + n.y * n.y * a, -n.y);
}

float3 RandomUnitVector(inout uint state, uint2 pixelCoord, uint sampleIndex, uint sampleDimension)
{
    float z = RandomFloat01(state) * 2.0f - 1.0f;
    float a = RandomFloat01(state) * K_TWO_PI;

    float r = sqrt(1.0f - z * z);
    float x = r * cos(a);
    float y = r * sin(a);
    return float3(x, y, z);
}

float3 SampleDiffuse(inout uint state, uint2 pixelCoord, uint sampleIndex, uint sampleDimension, in float3 normal)
{
#ifdef USE_BLUENOISE_SAMPLING
    float3 b1;
    float3 b2;
    CreateOrthoNormalBasis(normal, b1, b2);
    float x = GetBNDSequenceSample(pixelCoord, sampleIndex, sampleDimension);
    float y = GetBNDSequenceSample(pixelCoord, sampleIndex, sampleDimension + 1);
    float3 hamDir = HemisphereCosineSample(float2(x,y));
    float3 D = hamDir.x*b1 + hamDir.y*b2 + hamDir.z*normal;
    return D;
#else
    return normalize(normal + RandomUnitVector(state, pixelCoord, sampleIndex, sampleDimension));
#endif
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

float4 SampleColorFromHistory(RWTexture2D<float4> color, float2 coords, int2 texSize)
{
#if 0
    // point sampling
    return color.Load(coords);
#else
    // bilinear filter
    float2 tc1 = floor(coords + 0.5) - 0.5;
    float2 tc2 = tc1 + float2(1, 0);
    float2 tc3 = tc1 + float2(0, 1);
    float2 tc4 = tc1 + float2(1, 1);

    float2 w = coords - tc1;

    tc1 = min(tc1, texSize - 1);
    tc2 = min(tc2, texSize - 1);
    tc3 = min(tc3, texSize - 1);
    tc4 = min(tc4, texSize - 1);
    tc1 = max(tc1, 0);
    tc2 = max(tc2, 0);
    tc3 = max(tc3, 0);
    tc4 = max(tc4, 0);

    float4 s1 = color.Load(tc1);
    float4 s2 = color.Load(tc2);
    float4 s3 = color.Load(tc3);
    float4 s4 = color.Load(tc4);

    float4 c1 = lerp(s1, s2, w.x);
    float4 c2 = lerp(s3, s4, w.x);
    return lerp(c1, c2, w.y);

#endif
}
