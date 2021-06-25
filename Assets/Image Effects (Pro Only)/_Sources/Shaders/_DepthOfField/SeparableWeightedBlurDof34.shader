// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/SeparableWeightedBlurDof34" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
        _TapMedium ("TapMedium (RGB)", 2D) = "" {}
        _TapLow ("TapLow (RGB)", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    float4 offsets;
    float4 _Threshhold;
    sampler2D _MainTex;

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 uv01 : TEXCOORD1;
        float4 uv23 : TEXCOORD2;
        float4 uv45 : TEXCOORD3;
    };

    struct v2fSingle {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert (appdata_img v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv.xy = v.texcoord.xy;
        o.uv01 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1);
        o.uv23 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 2.0;
        o.uv45 =  v.texcoord.xyxy + offsets.xyxy * float4(1,1, -1,-1) * 3.0;

        return o;
    }

    v2fSingle vertSingleTex (appdata_img v) {
        v2fSingle o;
        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv.xy = v.texcoord.xy;

        return o;
    }

    // std flat blur

    float4 fragBlurUnweighted (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D(_MainTex, i.uv.xy);
        half4 sampleB = tex2D(_MainTex, i.uv01.xy);
        half4 sampleC = tex2D(_MainTex, i.uv01.zw);
        half4 sampleD = tex2D(_MainTex, i.uv23.xy);
        half4 sampleE = tex2D(_MainTex, i.uv23.zw);

        blurredColor += sampleA;
        blurredColor += sampleB * 1.25;
        blurredColor += sampleC * 1.25;
        blurredColor += sampleD * 1.5;
        blurredColor += sampleE * 1.5;

        blurredColor /= 6.5;

        return blurredColor;
    }

    // special, more bokeh style style, weighted blurring

    float4 fragBlur (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D(_MainTex, i.uv.xy);
        half4 sampleB = tex2D(_MainTex, i.uv01.xy);
        half4 sampleC = tex2D(_MainTex, i.uv01.zw);
        half4 sampleD = tex2D(_MainTex, i.uv23.xy);
        half4 sampleE = tex2D(_MainTex, i.uv23.zw);

        half sum = sampleA.a + dot (half4 (1.0, 1.0, 1.0, 1.0), half4 (sampleB.a,sampleC.a,sampleD.a,sampleE.a));

        sampleA.rgb = sampleA.rgb * sampleA.a;
        sampleB.rgb = sampleB.rgb * sampleB.a;// * 1.25;
        sampleC.rgb = sampleC.rgb * sampleC.a;// * 1.25;
        sampleD.rgb = sampleD.rgb * sampleD.a;// * 1.5;
        sampleE.rgb = sampleE.rgb * sampleE.a;// * 1.5;

        blurredColor += sampleA;
        blurredColor += sampleB;
        blurredColor += sampleC;
        blurredColor += sampleD;
        blurredColor += sampleE;

        blurredColor /= sum;
        half4 color = blurredColor;
        color.a = sampleA.a;
        return color;
    }

    float4 fragBlurMax (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D (_MainTex, i.uv.xy);
        half4 sampleD = tex2D (_MainTex, i.uv23.xy);
        half4 sampleE = tex2D (_MainTex, i.uv23.zw);

        half sum = dot (half3 (1.0, 1.0, 1.0), half3 (sampleA.a,sampleD.a,sampleE.a));

        sampleA.rgb = sampleA.rgb * sampleA.a;
        sampleD.rgb = sampleD.rgb * sampleD.a;
        sampleE.rgb = sampleE.rgb * sampleE.a;

        blurredColor += sampleA;
        blurredColor += sampleD;
        blurredColor += sampleE;

        blurredColor /= sum;
        //blurredColor = max(blurredColor,sampleD);
        //blurredColor = max(blurredColor,sampleE);
        half4 color = blurredColor;
        color.a = sampleA.a;

        return color;
    }

    // pass 3:
    // happens before applying final coc/blur result to screen,
    // mixes various low rez buffers according to settings etc
    // (nice we can do it in half/quarter resolution)

    sampler2D _TapMedium;
    sampler2D _TapLow;

    float4 fragMixMediumAndLowTap (v2fSingle i) : SV_Target
    {
        half4 tapMedium = tex2D (_TapMedium, i.uv.xy);
        half4 tapLow = tex2D (_TapLow, i.uv.xy);

        tapLow.rgb = lerp (tapMedium.rgb, tapLow.rgb, (tapLow.a * tapLow.a));

        return tapLow;
    }

    ENDCG

Subshader {
    ZTest Always Cull Off ZWrite Off
    Fog { Mode off }

  Pass {
      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBlur

      ENDCG
  }
  Pass {
      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBlurUnweighted

      ENDCG
  }
  Pass {
      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBlurMax

      ENDCG
  }
  Pass {
      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertSingleTex
      #pragma fragment fragMixMediumAndLowTap

      ENDCG
  }
}

Fallback off

} // shader
