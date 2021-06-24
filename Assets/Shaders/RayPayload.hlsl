struct RayPayload
{
    float3  albedo;
    float3  emission;
    float3  velocity;
    float3  bounceRayOrigin;
    float3  bounceRayDirection;
    float3  lastWorldNormal;
    float3  lastWorldPosition;      // Used for camera motion vectors
    float   intersectionT;
    uint    rngState;               // Random number generator state.
    int     bounceIndexOpaque;
    int     bounceIndexTransparent;
    bool    isShadowRay;
};