struct RayPayload
{
    float k;                // Energy conservation constraint
    float3 albedo;
    float3 emission;
    uint bounceIndexOpaque;
    uint bounceIndexTransparent;
    float3 bounceRayOrigin;
    float3 bounceRayDirection;
    uint rngState;          // Random number generator state.
};