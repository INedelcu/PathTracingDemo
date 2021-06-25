// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/SeparableBlurPlus" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;

        float4 uv01 : TEXCOORD1;
        float4 uv23 : TEXCOORD2;
        float4 uv45 : TEXCOORD3;

        float4 uv67 : TEXCOORD4;
    };

    float4 offsets;

    sampler2D _MainTex;
    half4     _MainTex_ST;

    v2f vert (appdata_img v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv.xy = v.texcoord.xy;

        o.uv01 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1);
        o.uv23 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 2.0;
        o.uv45 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 3.0;

        o.uv67 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 4.5;
        o.uv67 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 7.0;

        return o;
    }

    half4 frag (v2f i) : SV_Target {
        half4 color = float4 (0,0,0,0);

        color += 0.250 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv, _MainTex_ST));
        color += 0.150 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv01.xy, _MainTex_ST));
        color += 0.150 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv01.zw, _MainTex_ST));
        color += 0.110 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv23.xy, _MainTex_ST));
        color += 0.110 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv23.zw, _MainTex_ST));
        color += 0.075 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv45.xy, _MainTex_ST));
        color += 0.075 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv45.zw, _MainTex_ST));
        color += 0.040 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv67.xy, _MainTex_ST));
        color += 0.040 * tex2D (_MainTex, UnityStereoScreenSpaceUVAdjust( i.uv67.zw, _MainTex_ST));

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
