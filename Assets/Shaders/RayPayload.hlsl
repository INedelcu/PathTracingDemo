#ifndef RAY_PAYLOAD_H
#define RAY_PAYLOAD_H

struct RayPayload
{
    float3 albedo;
    float3 emission;
    uint bounceIndexOpaque;
    uint bounceIndexTransparent;
    float3 bounceRayOrigin;
    float3 bounceRayDirection;
    uint rngState;          // Random number generator state.
};

#endif // RAY_PAYLOAD_H