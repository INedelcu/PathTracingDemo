Shader "PathTracing/Standard"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MainTex("Albedo", 2D) = "white" {}

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle]_Cutout("Cutout Material", float) = 0

        _NormalMap("NormalMap", 2D) = "bump" {}

        [Toggle]_Emission("Emission", float) = 0

         [HDR]_EmissionColor("EmissionColor", Color) = (0,0,0)
        _EmissionTex("Emission", 2D) = "white" {}

        _SpecularColor("SpecularColor", Color) = (1, 1, 1, 1)

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0

        _IOR("Index of Refraction", Range(1.0, 2.8)) = 1.5
    }    
   
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100
     
         Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            #pragma shader_feature _EMISSION

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                #if _EMISSION
                float2 uv1 : TEXCOORD1;
                #endif
                float3 normal : NORMAL;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            sampler2D _EmissionTex;
            float4 _EmissionTex_ST;
            float4 _EmissionColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv0 = TRANSFORM_TEX(v.uv, _MainTex);
                #if _EMISSION
                    o.uv1 = TRANSFORM_TEX(v.uv, _EmissionTex);
                #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv0) * _Color * saturate(saturate(dot(float3(-0.4, -1, -0.5), i.normal)) + saturate(dot(float3(0.4, 1, 0.5), i.normal)));
                #if _EMISSION
                    col += tex2D(_EmissionTex, i.uv1) * _EmissionColor;
                #endif
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

            #pragma shader_feature_raytracing _EMISSION
            #pragma shader_feature_raytracing GEOM_TYPE_LEAF

            float4 _Color;
            float4 _SpecularColor;

            Texture2D<float4> _MainTex;
            float4 _MainTex_ST;
            SamplerState sampler__MainTex;

            Texture2D<float3> _NormalMap;
            float4 _NormalMap_ST;
            SamplerState sampler__NormalMap;

            Texture2D<float4> _EmissionTex;
            float4 _EmissionTex_ST;
            SamplerState sampler__EmissionTex;

            float4 _EmissionColor;

            float _Smoothness;
            float _Metallic;
            float _IOR;
            float _Cutoff;

            float4 unity_WorldTransformParams;
            float4x4 unity_MatrixPreviousM;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float4 tangent;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.tangent = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTangent);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(tangent);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            [shader("anyhit")]
            void AnyHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                float alpha = _MainTex.SampleLevel(sampler__MainTex, _MainTex_ST.xy * v.uv + _MainTex_ST.zw, 0).w;

                if (alpha < _Cutoff)
                    IgnoreHit();
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                if (payload.bounceIndexOpaque == g_BounceCountOpaque)
                {
                    payload.bounceIndexOpaque = INVALID_BOUNCE_INDEX;
                    return;
                }

                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);
            
                float3 emission = float3(0, 0, 0);

#if _EMISSION
                emission = _EmissionColor * _EmissionTex.SampleLevel(sampler__EmissionTex, _EmissionTex_ST.xy * v.uv + _EmissionTex_ST.zw, 0).xyz;
#endif               
                bool isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

                float3 localNormal = isFrontFace ? v.normal : -v.normal;

                float3 worldNormal = normalize(mul(localNormal, (float3x3)WorldToObject()));

                float3 worldTangent = normalize(mul(v.tangent.xyz, (float3x3)WorldToObject()));
                float3x3 tangentToWorld = CreateTangentToWorld(worldNormal, worldTangent, sign(v.tangent.w), unity_WorldTransformParams.w);
                float3 normalTS = _NormalMap.SampleLevel(sampler__NormalMap, _NormalMap_ST.xy * v.uv + _NormalMap_ST.zw, 0).xyz * 2 - 1;

                worldNormal = normalize(mul(normalTS, tangentToWorld));

                float fresnelFactor = FresnelReflectAmountOpaque(isFrontFace ? 1 : _IOR, isFrontFace ? _IOR : 1, WorldRayDirection(), worldNormal);

                float specularChance = lerp(_Metallic, 1, fresnelFactor * _Smoothness);

                uint2 launchIndex = uint2(DispatchRaysIndex().x, DispatchRaysDimensions().y - DispatchRaysIndex().y - 1);
                // Calculate whether we are going to do a diffuse or specular reflection ray 
#ifdef USE_BLUENOISE_SAMPLING
                uint bounceNum = payload.bounceIndexOpaque + payload.bounceIndexTransparent;
                float doSpecular = (GetBNDSequenceSample(launchIndex, payload.rngState, NB_RAND_BOUNCE * bounceNum + 2) < specularChance) ? 1 : 0;
#else
                float doSpecular = (RandomFloat01(payload.rngState) < specularChance) ? 1 : 0;
#endif
                // Get a cosine-weighted distribution by using the formula from https://www.iue.tuwien.ac.at/phd/ertl/node100.html
                
                float3 diffuseRayDir =  SampleDiffuse(payload.rngState, launchIndex, payload.rngState, NB_RAND_BOUNCE * payload.bounceIndexOpaque, worldNormal);

                float3 specularRayDir = reflect(WorldRayDirection(), worldNormal);
              
                specularRayDir = normalize(lerp(diffuseRayDir, specularRayDir, _Smoothness));

                float3 reflectedRayDir = lerp(diffuseRayDir, specularRayDir, doSpecular);

                float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1)).xyz;
                float3 prevWorldPos = mul(unity_MatrixPreviousM, float4(v.position, 1.0)).xyz;

                // Bounced ray origin is pushed off of the surface using the face normal (not the interpolated normal).
                float3 e0 = v1.position - v0.position;
                float3 e1 = v2.position - v0.position;

                float3 worldFaceNormal = normalize(mul(cross(e0, e1), (float3x3)WorldToObject()));

                float3 albedo = _Color.xyz * _MainTex.SampleLevel(sampler__MainTex, _MainTex_ST.xy * v.uv + _MainTex_ST.zw, 0).xyz;

                payload.albedo              = lerp(albedo, _SpecularColor.xyz, doSpecular);
                payload.emission            = emission;                
                payload.bounceIndexOpaque   = payload.bounceIndexOpaque + 1;
                payload.bounceRayOrigin     = worldPosition + K_RAY_ORIGIN_PUSH_OFF * worldFaceNormal;
                payload.bounceRayDirection  = reflectedRayDir;
                payload.lastWorldNormal     = worldNormal;
                payload.lastWorldPosition   = worldPosition;
                payload.intersectionT       = RayTCurrent();
                payload.velocity            = worldPosition - prevWorldPos;
            }

            ENDHLSL
        }

        Pass
        {
            Name "PathTracingGBuffer"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

             #pragma raytracing gbuffer

            #include "UnityRaytracingMeshUtils.cginc"
            #include "RayPayloadGBuffer.hlsl"
            #include "Utils.hlsl"

            float4x4 unity_MatrixPreviousM;
            float4 unity_WorldTransformParams;

            Texture2D<float3> _NormalMap;
            float4 _NormalMap_ST;
            SamplerState sampler__NormalMap;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float4 tangent;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.tangent = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTangent);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(tangent);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayloadGBuffer payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                float3 worldPos = mul(ObjectToWorld(), float4(v.position, 1.0)).xyz;
                float3 prevWorldPos = mul(unity_MatrixPreviousM, float4(v.position, 1.0)).xyz;

                float3 worldNormal = normalize(mul(v.normal, (float3x3)WorldToObject()));

                float3 worldTangent = normalize(mul(v.tangent.xyz, (float3x3)WorldToObject()));
                float3x3 tangentToWorld = CreateTangentToWorld(worldNormal, worldTangent, sign(v.tangent.w), unity_WorldTransformParams.w);
                float3 normalTS = _NormalMap.SampleLevel(sampler__NormalMap, _NormalMap_ST.xy * v.uv + _NormalMap_ST.zw, 0).xyz * 2 - 1;

                payload.worldNormal = normalize(mul(normalTS, tangentToWorld));
                payload.intersectionT = RayTCurrent();
                payload.velocity = worldPos - prevWorldPos;
            }

            ENDHLSL
        }
    }

    CustomEditor "PathTracingSimpleShaderGUI"
}