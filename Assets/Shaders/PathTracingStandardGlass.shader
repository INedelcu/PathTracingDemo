Shader "PathTracing/StandardGlass"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _ExtinctionCoefficient("Extinction Coefficient", Range(0.0, 20.0)) = 1.0
        
        _Roughness("Roughness", Range(0.0, 0.5)) = 0.0
        
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
            #include "GlobalResources.hlsl"

            #pragma raytracing test
            
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

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                if (payload.bounceIndexTransparent == g_BounceCountTransparent)
                {
                    payload.bounceIndexTransparent = -1;
                    return;
                }

                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                bool isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

                float3 roughness = _Roughness * RandomUnitVector(payload.rngState);

#if _FLAT_SHADING
                float3 e0 = v1.position - v0.position;
                float3 e1 = v2.position - v0.position;

                float3 localNormal = normalize(cross(e0, e1));
#else
                float3 localNormal = v.normal;
#endif      

                float normalSign = isFrontFace ? 1 : -1;

                localNormal *= normalSign;

                float3 worldNormal = normalize(mul(localNormal, (float3x3)WorldToObject()) + roughness);

                float3 reflectionRayDir = reflect(WorldRayDirection(), worldNormal);
                
                float indexOfRefraction = isFrontFace ? 1 / _IOR : _IOR;

                float3 refractionRayDir = refract(WorldRayDirection(), worldNormal, indexOfRefraction);
                
                float fresnelFactor = FresnelReflectAmountTransparent(isFrontFace ? 1 : _IOR, isFrontFace ? _IOR : 1, WorldRayDirection(), worldNormal);

                float doRefraction = (RandomFloat01(payload.rngState) > fresnelFactor) ? 1 : 0;

                float3 bounceRayDir = lerp(reflectionRayDir, refractionRayDir, doRefraction);

                float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1)).xyz;

                float pushOff = doRefraction ? -K_RAY_ORIGIN_PUSH_OFF : K_RAY_ORIGIN_PUSH_OFF;

                float3 albedo = !isFrontFace ? exp(-(1 - _Color.xyz) * RayTCurrent() * _ExtinctionCoefficient) : float3(1, 1, 1);

                payload.k                       = (doRefraction == 1) ? 1 - fresnelFactor : fresnelFactor;
                payload.albedo                  = albedo;
                payload.emission                = float3(0, 0, 0);
                payload.bounceIndexTransparent  = payload.bounceIndexTransparent + 1;
                payload.bounceRayOrigin         = worldPosition + pushOff * worldNormal;
                payload.bounceRayDirection      = bounceRayDir;
            }

            ENDHLSL
        }
    
    }

    CustomEditor "PathTracingSimpleGlassShaderGUI"
}