#ifndef LIGHTS_HLSL
#define LIGHTS_HLSL

#include "Utils.hlsl"

#define LIGHT_TYPE_DIRECTIONAL  0
#define LIGHT_TYPE_POINT        1

// Distance below which the inverse square falloff is clamped to avoid the 1/d²
// singularity when shading right next to a point light. 1 cm is the convention
// used by most real time engines.
#define K_PUNCTUAL_LIGHT_THRESHOLD 0.01

// Full angular diameter of every directional light, in radians. 0.5 degrees
// matches the sun seen from Earth, producing a subtle penumbra. Set to 0 for a
// pure delta directional light (sharp shadows).
#define K_DIRECTIONAL_ANGULAR_DIAMETER (0.5 * K_PI / 180.0)

// Type tagged light record. Layout matches the C# LightData struct in
// PathTracingDemo.cs (48 bytes).
struct Light
{
    float3 color;        // light.color.linear * light.intensity.
    uint   type;         // LIGHT_TYPE_*
    float3 direction;    // Directional: forward direction the light travels along.
    float  range;        // Point only.
    float3 position;     // Point only.
    float  _pad0;
};

struct ShadowRayPayload
{
    uint visible;
};

// Trace a shadow ray and return 1 if the target light is unoccluded, 0 otherwise.
// RAY_FLAG_SKIP_CLOSEST_HIT_SHADER treats every geometry hit as a fully opaque
// occluder, so the shadow ray cannot recurse back into a closest hit shader and
// counts as one extra level of recursion against max_recursion_depth.
float TraceShadowRay(float3 origin, float3 direction, float distance)
{
    RayDesc shadowRay;
    shadowRay.Origin    = origin;
    shadowRay.Direction = direction;
    shadowRay.TMin      = 0;
    shadowRay.TMax      = distance;

    ShadowRayPayload shadowPayload;
    shadowPayload.visible = 0;

    const uint shadowRayMissShaderIndex = 1;

    TraceRay(g_AccelStruct,
             RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
             0xFF, 0, 1, shadowRayMissShaderIndex, shadowRay, shadowPayload);

    return (float)shadowPayload.visible;
}

// Sample a single light from a shading point. Returns false when the light
// cannot contribute (out of range, behind the horizon, ...).
//   wi   — unit vector from the surface toward the light.
//   dist — distance to test for occlusion (TMax for the shadow ray).
//   Le   — incident radiance to multiply by BRDF * cos(theta_l).
// Every light type either is a delta light or has its sampling pdf cancel
// against Le (see the directional cone case below, where the solid angle Ω
// cancels). The caller multiplies Le * BRDF * cos directly without dividing
// by a pdf.
bool SampleLight(Light light, float3 worldPos, inout uint rngState, out float3 wi, out float dist, out float3 Le)
{
    wi   = float3(0, 1, 0);
    dist = 0;
    Le   = float3(0, 0, 0);

    switch (light.type)
    {
        case LIGHT_TYPE_DIRECTIONAL:
        {
            // Uniform cone sample around the direction toward the light. With
            // K_DIRECTIONAL_ANGULAR_DIAMETER == 0 this collapses to a delta
            // (cosThetaMax = 1, sinTheta = 0, sample = axis exactly). Le stays
            // light.color because the cone solid angle Ω cancels:
            //   L_disc = E / Ω, pdf = 1 / Ω
            //   estimator = f * cos(θ) * L_disc / pdf = f * cos(θ) * E
            float3 axis        = -normalize(light.direction);
            float  cosThetaMax = cos(K_DIRECTIONAL_ANGULAR_DIAMETER * 0.5);
            float  u1          = RandomFloat01(rngState);
            float  u2          = RandomFloat01(rngState);
            float  cosTheta    = lerp(cosThetaMax, 1.0, u1);
            float  sinTheta    = sqrt(saturate(1.0 - cosTheta * cosTheta));
            float  phi         = K_TWO_PI * u2;

            float3 T, B;
            BuildOrthonormalBasis(axis, T, B);
            wi   = (cos(phi) * T + sin(phi) * B) * sinTheta + axis * cosTheta;
            dist = K_T_MAX;
            Le   = light.color;
            return true;
        }
        case LIGHT_TYPE_POINT:
        {
            // Delta point light. Smooth windowed inverse square from Lagarde 2014
            // ("Moving Frostbite to PBR"):
            //   window      = saturate(1 − (d²/r²)²)
            //   attenuation = (min(1/d, 1/threshold) · window)²
            // The min caps the 1/d² singularity at K_PUNCTUAL_LIGHT_THRESHOLD;
            // squaring at the end smooths the transition near range.
            float3 toLight = light.position - worldPos;
            float  d2      = dot(toLight, toLight);
            float  d       = sqrt(d2);
            if (d >= light.range)
                return false;

            float invD        = min(rcp(d), rcp(K_PUNCTUAL_LIGHT_THRESHOLD));
            float r2          = light.range * light.range;
            float window      = saturate(1.0 - (d2 / r2) * (d2 / r2));
            float attenuation = invD * window;
            attenuation *= attenuation;

            wi   = toLight / d;
            dist = d;
            Le   = light.color * attenuation;
            return true;
        }
    }

    return false;
}

#endif // LIGHTS_HLSL
