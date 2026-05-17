#ifndef GLOBAL_RESOURCES_H
#define GLOBAL_RESOURCES_H

RaytracingAccelerationStructure g_AccelStruct : register(t0, space1);

uint g_BounceCountOpaque;
uint g_BounceCountTransparent;

#endif // GLOBAL_RESOURCES_H