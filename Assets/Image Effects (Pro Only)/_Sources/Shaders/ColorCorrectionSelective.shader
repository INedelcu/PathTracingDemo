// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/ColorCorrectionSelective" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;
    half4 _MainTex_ST;

    float4 selColor;
    float4 targetColor;

    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos (v.vertex);
        o.uv = UnityStereoScreenSpaceUVAdjust(v.texcoord.xy, _MainTex_ST);
        return o;
    }

    float4 frag(v2f i) : SV_Target {
        float4 color = tex2D (_MainTex, i.uv);

        float differenz = 1.0 - saturate (length (color.rgb - selColor.rgb));
        color = lerp (color, targetColor, differenz);
        return color;
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
