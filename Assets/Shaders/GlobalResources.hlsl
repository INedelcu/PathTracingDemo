#ifndef GLOBAL_RESOURCES_INCLUDED
#define GLOBAL_RESOURCES_INCLUDED

RaytracingAccelerationStructure g_AccelStruct           : register(t0, space1);
Texture2D<float>                _ScramblingTileXSPP     : register(t1, space1);
Texture2D<float>                _RankingTileXSPP        : register(t2, space1);
Texture2D<float2>               _ScramblingTexture      : register(t3, space1);
Texture2D<float2>               _OwenScrambledTexture   : register(t4, space1);

cbuffer GlobalParams : register(b0, space1)
{
    uint g_BounceCountOpaque;
    uint g_BounceCountTransparent;
};

#endif