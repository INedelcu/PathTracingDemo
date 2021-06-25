// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/BlendOneOne" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    // Shader code pasted into all further CGPROGRAM blocks
    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;
    float intensity;
    half4     _MainTex_ST;

    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = UnityStereoScreenSpaceUVAdjust(v.texcoord.xy, _MainTex_ST);
        return o;
    }

    half4 frag(v2f i) : SV_Target {
        return tex2D(_MainTex, i.uv) * intensity;
    }

    ENDCG

Subshader {
 Blend One One
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

} // shader
