// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/HollywoodFlareStretchShader" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    float4 offsets;
    float stretchWidth;

    sampler2D _MainTex;

    half4     _MainTex_ST;

    v2f vert (appdata_img v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv =  v.texcoord.xy;
        return o;
    }

    half4 frag (v2f i) : SV_Target {
        float4 color = tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv, _MainTex_ST));

        float b = stretchWidth;

        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv + b * 2.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv - b * 2.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv + b * 4.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv - b * 4.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv + b * 8.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv - b * 8.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv + b * 14.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv - b * 14.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv + b * 20.0 * offsets.xy, _MainTex_ST)));
        color = max(color,tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv - b * 20.0 * offsets.xy, _MainTex_ST)));


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

} // shader
