#ifndef GLOBAL_RESOURCES_H
#define GLOBAL_RESOURCES_H

RaytracingAccelerationStructure g_AccelStruct : register(t0, space1);

// Maximum number of ray bounces for opaque and transparent materials.
// To be capped at 254 from C# due to uint bitfield storage in RayPayload.bounceIndices.
uint g_MaxBounceCountOpaque;
uint g_MaxBounceCountTransparent;

#endif // GLOBAL_RESOURCES_H