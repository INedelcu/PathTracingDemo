struct RayPayload
{
    float3 albedo;
    float3 emission;
    uint bounceIndexOpaque;
    uint bounceIndexTransparent;
    float3 bounceRayOrigin;
    float3 bounceRayDirection;
    float3 lastWorldNormal;
    float3 lastWorldPosition;  // Used for camera motion vectors, will probably remove later
    uint rngState;          // Random number generator state.
    bool isShadowRay;
};