// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/AddAlphaHack" {
    Properties {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    // Shader code pasted into all further CGPROGRAM blocks
    CGINCLUDE

        #pragma fragmentoption ARB_precision_hint_fastest

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;
    float4 _Color;
    half4  _MainTex_ST;

    v2f vert (appdata_base v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = UnityStereoScreenSpaceUVAdjust(MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord), _MainTex_ST);
        return o;
    }

    half4 frag(v2f i) : SV_Target {
        return half4(0, 0, 0, 10.0) * tex2D(_MainTex, i.uv);
    }

    ENDCG

Subshader {
 Blend One One
 ZWrite Off
 ZTest LEqual

 Tags { "RenderType"="Opaque" "RenderQueue" = "Transparent+1000" }
  Pass {

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest

      #pragma vertex vert
      #pragma fragment frag

      ENDCG
   }
 }

Subshader {
 Blend One One
 ZWrite Off
 ZTest LEqual

 Tags { "RenderType"="Transparent" "RenderQueue" = "Transparent+1000" }
  Pass {

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest

      #pragma vertex vert
      #pragma fragment frag

      ENDCG
   }
 }

Fallback off

} // shader
