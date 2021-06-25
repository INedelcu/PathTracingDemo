// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/RadialBlur" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
    }

    // Shader code pasted into all further CGPROGRAM blocks
    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : POSITION;
        float2 uv : TEXCOORD0;
    };

    float4 blurTo;
    float4 blurDir;
    float4 _MainTex_TexelSize;

    sampler2D _MainTex;

    v2f vert (appdata_img v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord.xy;
        return o;
    }

    half4 frag (v2f i) : SV_Target
    {
        float4 color = float4 (0,0,0,0);
        float2 rad = normalize(blurTo.xy-i.uv.xy);

        float2 ofs = blurDir.xy;
        ofs *=_MainTex_TexelSize.xy * blurDir.w;

        color += 0.40 * tex2D(_MainTex, i.uv);
        color += 0.20 * tex2D(_MainTex, i.uv + 1.0 * ofs.xy);
        color += 0.15 * tex2D(_MainTex, i.uv + 2.0 * ofs.xy);
        color += 0.10 * tex2D(_MainTex, i.uv + 3.0 * ofs.xy);
        color += 0.10 * tex2D(_MainTex, i.uv + 4.0 * ofs.xy);
        color += 0.05 * tex2D(_MainTex, i.uv + 5.0 * ofs.xy);

        return color; // min(color, tex2D(_MainTex, i.uv)); //.rgb,half3(0.59,0.3,0.11));
    }

    ENDCG

Subshader {
 Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      ENDCG
  }
}

} // shader
