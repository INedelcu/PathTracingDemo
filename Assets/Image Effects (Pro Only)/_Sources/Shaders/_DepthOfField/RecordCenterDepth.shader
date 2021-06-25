// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

 Shader "Hidden/RecordCenterDepth" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D_float _CameraDepthTexture;
    uniform float4 _CameraDepthTexture_TexelSize;

    uniform float deltaTime;

    sampler2D _MainTex;

    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv =  MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord);
        return o;
    }

    half4 frag(v2f i) : SV_Target
    {
        float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, half2(0.5,0.5));
        depth += SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, half2(0.5,0.5) + _CameraDepthTexture_TexelSize.xy * half2( 1, 1));
        depth += SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, half2(0.5,0.5) + _CameraDepthTexture_TexelSize.xy * half2(-1,-1));

        depth /= 3.0;

        depth = Linear01Depth(depth);

        if(depth>0.9999)
            return lerp(tex2D(_MainTex, half2(0.5,0.5)), half4(1,1,1,1), saturate(deltaTime));
        else
            return lerp(tex2D(_MainTex, half2(0.5,0.5)), EncodeFloatRGBA(depth), saturate(deltaTime));
    }

    ENDCG

Subshader {
 Pass {
    ZTest Always Cull Off ZWrite Off
    Fog { Mode off }

    CGPROGRAM

    #pragma fragmentoption ARB_precision_hint_fastest
    #pragma vertex vert
    #pragma fragment frag

    ENDCG
    }
  }

Fallback off

}
