Shader "PathTracing/Standard"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MainTex("Albedo", 2D) = "white" {}

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        [Toggle]_Emission("Emission", float) = 0

         [HDR]_EmissionColor("EmissionColor", Color) = (0,0,0)
        _EmissionTex("Emission", 2D) = "white" {}

        _SpecularColor("SpecularColor", Color) = (1, 1, 1, 1)

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0

        _IOR("Index of Refraction", Range(1.0, 2.8)) = 1.5

        [HideInInspector] _SurfaceType("__type", Integer) = 0
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _Cull("__cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True"}
        LOD 100

        Pass
        {
            Cull [_Cull]

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            #pragma shader_feature_local EMISSION_ON
			#pragma shader_feature_local ALPHATEST_ON

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                #if EMISSION_ON
                float2 uv1 : TEXCOORD1;
                #endif
                float3 normal : NORMAL;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            float _Cutoff;

            sampler2D _EmissionTex;
            float4 _EmissionTex_ST;
            float4 _EmissionColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv0 = TRANSFORM_TEX(v.uv, _MainTex);
                #if EMISSION_ON
                    o.uv1 = TRANSFORM_TEX(v.uv, _EmissionTex);
                #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv0);
                #if ALPHATEST_ON
                    clip(albedo.a - _Cutoff);
                #endif
                fixed4 col = albedo * _Color * saturate(saturate(dot(float3(-0.4, -1, -0.5), i.normal)) + saturate(dot(float3(0.4, 1, 0.5), i.normal)));
                #if EMISSION_ON
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

            #include "GlobalResources.hlsl"
            #include "RayPayload.hlsl"
            #include "Shading.hlsl"
            #include "UnityRaytracingMeshUtils.cginc"

            #pragma raytracing main_hit_group

            #pragma shader_feature_raytracing EMISSION_ON
            #pragma shader_feature_raytracing ALPHATEST_ON
			#pragma shader_feature_raytracing DOUBLE_SIDED_ON

            float4 _Color;
            float4 _SpecularColor;

            Texture2D<float4> _MainTex;
            float4 _MainTex_ST;
            SamplerState sampler__MainTex;

            Texture2D<float4> _EmissionTex;
            float4 _EmissionTex_ST;
            SamplerState sampler__EmissionTex;

            float _Cutoff;

            float4 _EmissionColor;

            float _Smoothness;
            float _Metallic;
            float _IOR;

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

            SurfaceHit LoadSurfaceHit(AttributeData attribs)
            {
                uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                Vertex v0 = FetchVertex(tri.x);
                Vertex v1 = FetchVertex(tri.y);
                Vertex v2 = FetchVertex(tri.z);

                float3 bary = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y,
                                     attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, bary);

                SurfaceHit s;
                s.isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

                float3 localNormal = s.isFrontFace ? v.normal : -v.normal;
                s.worldNormal = normalize(mul(localNormal, (float3x3)WorldToObject()));

                // Push-off uses the face normal, not the interpolated normal.
                float3 e0 = v1.position - v0.position;
                float3 e1 = v2.position - v0.position;
                s.worldFaceNormal = normalize(mul(cross(e0, e1), (float3x3)WorldToObject()));

                s.worldPosition = mul(ObjectToWorld(), float4(v.position, 1)).xyz;
                s.uv = v.uv;
                return s;
            }

            MaterialSample EvaluateMaterial(float2 uv)
            {
                float3 baseColor = _Color.xyz * _MainTex.SampleLevel(sampler__MainTex, _MainTex_ST.xy * uv + _MainTex_ST.zw, 0).xyz;

                float3 emission = float3(0, 0, 0);
#if EMISSION_ON
                emission = _EmissionColor.xyz * _EmissionTex.SampleLevel(sampler__EmissionTex, _EmissionTex_ST.xy * uv + _EmissionTex_ST.zw, 0).xyz;
#endif

                // Dielectric F0 from IOR, tinted by SpecularColor; metals use baseColor as F0.
                float iorF0 = (1.0 - _IOR) / (1.0 + _IOR);
                iorF0 *= iorF0;

                MaterialSample m;
                m.F0            = lerp(iorF0 * _SpecularColor.xyz, baseColor, _Metallic);
                m.diffuseAlbedo = baseColor * (1.0 - _Metallic);
                m.alpha         = SmoothnessToAlpha(_Smoothness);
                m.emission      = emission;
                return m;
            }

            [shader("anyhit")]
            void AnyHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float2 uv0 = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0);
                float2 uv1 = UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0);
                float2 uv2 = UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0);

                float2 uv = uv0 * (1.0 - attribs.barycentrics.x - attribs.barycentrics.y) + uv1 * attribs.barycentrics.x + uv2 * attribs.barycentrics.y;
                float alpha = _MainTex.SampleLevel(sampler__MainTex, _MainTex_ST.xy * uv + _MainTex_ST.zw, 0).a;
                if (alpha < _Cutoff)
                {
                    IgnoreHit();
                }
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                if (payload.GetBounceIndexOpaque() >= g_MaxBounceCountOpaque)
                {
                    payload.Terminate();
                    return;
                }

                SurfaceHit hit = LoadSurfaceHit(attribs);
                MaterialSample mat = EvaluateMaterial(hit.uv);

                ShadeOpaqueSurface(payload, hit, mat, -WorldRayDirection());
            }

            ENDHLSL
        }
    }

    CustomEditor "PathTracingSimpleShaderGUI"
}
