#ifndef RAY_PAYLOAD_H
#define RAY_PAYLOAD_H

// Packed bounce counters. Layout:
//   bits  0.. 7 (0x000000ff): opaque bounce index      (max 254)
//   bits  8..15 (0x0000ff00): transparent bounce index (max 254)
//   bits 16..31 (0xffff0000): total bounce index       (max 65534)
// Set to 0xffffffff to mark the path as terminated. Keep g_MaxBounceCount* < 255
// so the per-byte increment cannot carry into the neighbouring field.

struct RayPayload
{
    float3 albedo;
    float3 emission;
    float3 bounceRayOrigin;
    float3 bounceRayDirection;
    uint bounceIndices;
    uint rngState;                  // Random number generator state.

    void Init(uint initialRngState)
    {
        albedo = float3(1, 1, 1);
        emission = float3(0, 0, 0);
        rngState = initialRngState;
        bounceIndices = 0;
        bounceRayOrigin = float3(0, 0, 0);
        bounceRayDirection = float3(0, 0, 0);
    }

    uint GetBounceIndexOpaque()      { return bounceIndices & 0xff; }
    uint GetBounceIndexTransparent() { return (bounceIndices >> 8) & 0xff; }
    uint GetBounceIndexTotal()       { return bounceIndices >> 16; }

    void IncrementBounceIndexOpaque()      { bounceIndices += 0x00000001; }
    void IncrementBounceIndexTransparent() { bounceIndices += 0x00000100; }
    void IncrementBounceIndexTotal()       { bounceIndices += 0x00010000; }

    void Terminate()    { bounceIndices = 0xffffffff; }
    bool IsTerminated() { return bounceIndices == 0xffffffff; }
};

#endif // RAY_PAYLOAD_H
