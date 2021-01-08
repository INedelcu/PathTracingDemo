struct RayPayload
{
    float3 radiance;
    float3 throughput;
    uint rngState;
    uint bounceCountOpaque;
    uint bounceCountTransparent;
};

struct Result
{
    float3 radiance;
};