Shader "PathTracing/StandardGlass"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _ExtinctionCoefficient("Extinction Coefficient", Range(0.0, 20.0)) = 1.0

        _Roughness("Roughness", Range(0.0, 1.0)) = 0.0

        [Toggle] _FlatShading("Flat Shading", float) = 0

        _IOR("Index of Refraction", Range(1.0, 2.8)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" }
        LOD 100

         Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;

            };

            float4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color * saturate(saturate(dot(float3(-0.4, -1, -0.5), i.normal)) + saturate(dot(float3(0.4, 1, 0.5), i.normal)));
                return col;
            }
            ENDCG
        }
    }

    SubShader
    {
        Pass
        {
            Name "PathTracing"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #include "UnityRaytracingMeshUtils.cginc"
            #include "RayPayload.hlsl"
            #include "Utils.hlsl"
            #include "BRDF.hlsl"
            #include "GlobalResources.hlsl"

            #pragma raytracing main_hit_group

            #pragma shader_feature _FLAT_SHADING

            float4 _Color;
            float _IOR;
            float _Roughness;
            float _ExtinctionCoefficient;
            float _FlatShading;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            struct SurfaceHit
            {
                float3 worldPosition;
                float3 worldNormal;
                bool   isFrontFace;
            };

            // The macro-surface normal is oriented so that it points against the
            // incoming ray (i.e. dot(N, V) > 0), which is what the microfacet
            // sampler expects regardless of which side of the glass we hit.
            SurfaceHit LoadSurfaceHit(AttributeData attribs)
            {
                uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                Vertex v0 = FetchVertex(tri.x);
                Vertex v1 = FetchVertex(tri.y);
                Vertex v2 = FetchVertex(tri.z);

                float3 bary = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, bary);

                SurfaceHit s;
                s.isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

#if _FLAT_SHADING
                float3 e0 = v1.position - v0.position;
                float3 e1 = v2.position - v0.position;
                float3 localNormal = normalize(cross(e0, e1));
#else
                float3 localNormal = v.normal;
#endif
                localNormal *= s.isFrontFace ? 1.0 : -1.0;
                s.worldNormal = normalize(mul(localNormal, (float3x3)WorldToObject()));

                s.worldPosition = mul(ObjectToWorld(), float4(v.position, 1)).xyz;
                return s;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                if (payload.bounceIndexTransparent == g_BounceCountTransparent)
                {
                    payload.bounceIndexTransparent = -1;
                    return;
                }

                SurfaceHit hit = LoadSurfaceHit(attribs);

                float etaI  = hit.isFrontFace ? 1.0 : _IOR;
                float etaT  = hit.isFrontFace ? _IOR : 1.0;
                float alpha = max(_Roughness * _Roughness, 1e-4);

                float3 L;
                float3 weight;
                bool   isReflected;
                if (!SampleGlassGGX(WorldRayDirection(), hit.worldNormal, etaI, etaT, alpha, payload.rngState, L, weight, isReflected))
                {
                    payload.albedo                 = float3(0, 0, 0);
                    payload.emission               = float3(0, 0, 0);
                    payload.bounceIndexTransparent = -1;
                    return;
                }

                // Beer-Lambert absorption applies on rays that travelled through
                // the medium, i.e. when the current hit is the back face exit.
                float3 absorption = !hit.isFrontFace ? exp(-(1.0 - _Color.xyz) * RayTCurrent() * _ExtinctionCoefficient) : float3(1, 1, 1);

                float pushSign = isReflected ? 1.0 : -1.0;

                payload.albedo                 = weight * absorption;
                payload.emission               = float3(0, 0, 0);
                payload.bounceIndexTransparent = payload.bounceIndexTransparent + 1;
                payload.bounceRayOrigin        = hit.worldPosition + pushSign * K_RAY_ORIGIN_PUSH_OFF * hit.worldNormal;
                payload.bounceRayDirection     = L;
            }

            ENDHLSL
        }

    }

    CustomEditor "PathTracingSimpleGlassShaderGUI"
}