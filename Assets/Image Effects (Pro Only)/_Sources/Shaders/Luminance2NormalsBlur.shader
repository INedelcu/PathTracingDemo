// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'


Shader "Hidden/Luminance2NormalsBlur" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
    _BlurTex ("Base (RGB)", 2D) = "white" {}

}

SubShader {
    Pass {
        ZTest Always Cull Off ZWrite Off
        Fog { Mode off }

CGPROGRAM

#pragma multi_compile SHOW_DEBUG_ON SHOW_DEBUG_OFF
#pragma vertex vert
#pragma fragment frag
// Program 'frag', error X4506: ps_4_0_level_9_1 input limit (8) exceeded, shader uses 9 inputs. (on d3d11_9x)
#pragma exclude_renderers d3d11_9x
#pragma fragmentoption ARB_precision_hint_fastest

// Luminance calculation is in gamma space, but SM2.0 can't handle the extra instructions needed for the correct conversion.
#ifndef UNITY_NO_LINEAR_COLORSPACE
#define UNITY_NO_LINEAR_COLORSPACE
#endif

#include "UnityCG.cginc"

uniform sampler2D _MainTex;
uniform float4 _MainTex_TexelSize;
uniform float _OffsetScale;
uniform float _BlurRadius;

struct v2f {
    float4 pos : SV_POSITION;
    float2 uv[8] : TEXCOORD0;
};

    v2f vert( appdata_img v )
    {
        v2f o;
        o.pos = UnityObjectToClipPos (v.vertex);

        float2 uv = v.texcoord.xy;

        #if SHADER_API_D3D9
        if (_MainTex_TexelSize.y < 0)
            uv.y = 1-uv.y;
        #endif

        float2 up = float2(0.0, _MainTex_TexelSize.y) * _OffsetScale;
        float2 right = float2(_MainTex_TexelSize.x, 0.0) * _OffsetScale;

        o.uv[0].xy = uv + up;
        o.uv[1].xy = uv - up;
        o.uv[2].xy = uv + right;
        o.uv[3].xy = uv - right;
        o.uv[4].xy = uv - right + up;
        o.uv[5].xy = uv - right -up;
        o.uv[6].xy = uv + right + up;
        o.uv[7].xy = uv + right -up;

        return o;
    }

    half4 frag (v2f i) : SV_Target
    {
        // get luminance values
        //  maybe: experiment with different luminance calculations
        float topL = Luminance( tex2D(_MainTex, i.uv[0]).rgb );
        float bottomL = Luminance( tex2D(_MainTex, i.uv[1]).rgb );
        float rightL = Luminance( tex2D(_MainTex, i.uv[2]).rgb );
        float leftL = Luminance( tex2D(_MainTex, i.uv[3]).rgb );
        float leftTopL = Luminance( tex2D(_MainTex, i.uv[4]).rgb );
        float leftBottomL = Luminance( tex2D(_MainTex, i.uv[5]).rgb );
        float rightBottomL = Luminance( tex2D(_MainTex, i.uv[6]).rgb );
        float rightTopL = Luminance( tex2D(_MainTex, i.uv[7]).rgb );

        // 2 triangle subtractions
        float sum0 = dot(float3(1,1,1), float3(rightTopL,bottomL,leftTopL));
        float sum1 = dot(float3(1,1,1), float3(leftBottomL,topL,rightBottomL));
        float sum2 = dot(float3(1,1,1), float3(leftTopL,rightL,leftBottomL));
        float sum3 = dot(float3(1,1,1), float3(rightBottomL,leftL,rightTopL));

        // figure out "normal"
        float2 blurDir = half2((sum0-sum1), (sum3-sum2));
        blurDir *= _MainTex_TexelSize.xy * _BlurRadius;

        // reconstruct normal uv
        float2 uv_ = (i.uv[0] + i.uv[1]) * 0.5;

        float4 returnColor = tex2D(_MainTex, uv_);
        returnColor += tex2D(_MainTex, uv_+ blurDir.xy);
        returnColor += tex2D(_MainTex, uv_ - blurDir.xy);
        returnColor += tex2D(_MainTex, uv_ + float2(blurDir.x, -blurDir.y));
        returnColor += tex2D(_MainTex, uv_ - float2(blurDir.x, -blurDir.y));

#if defined(SHOW_DEBUG_ON)
        blurDir = half2((sum0-sum1), (sum3-sum2)) * _BlurRadius;
        return half4(normalize( half3(blurDir,1) * 0.5 + 0.5), 1);
#else
        return returnColor * 0.2;
#endif
    }

    ENDCG
    }
}

Fallback off

}
