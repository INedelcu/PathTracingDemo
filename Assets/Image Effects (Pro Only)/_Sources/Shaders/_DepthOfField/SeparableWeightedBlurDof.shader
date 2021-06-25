// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/SeparableWeightedBlurDof" {
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
    half4 _MainTex_ST;

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
        o.uv.xy = UnityStereoScreenSpaceUVAdjust(v.texcoord.xy, _MainTex_ST);
        o.uv01 = UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy + offsets.xyxy * half4(1, 1, -1, -1), _MainTex_ST);
        o.uv23 = UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy + offsets.xyxy * half4(1, 1, -1, -1) * 2.0, _MainTex_ST);
        o.uv45 = UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy + offsets.xyxy * half4(1, 1, -1, -1) * 3.0, _MainTex_ST);

        return o;
    }

    v2fSingle vertSingleTex (appdata_img v) {
        v2fSingle o;
        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv.xy = v.texcoord.xy;

        return o;
    }

    // used in pass 2

    float4 fragBlurUnweighted (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D(_MainTex, i.uv.xy);
        half4 sampleB = tex2D(_MainTex, i.uv01.xy);
        half4 sampleC = tex2D(_MainTex, i.uv01.zw);
        half4 sampleD = tex2D(_MainTex, i.uv23.xy);
        half4 sampleE = tex2D(_MainTex, i.uv23.zw);

        blurredColor += sampleA;
        blurredColor += sampleB;
        blurredColor += sampleC;
        blurredColor += sampleD;
        blurredColor += sampleE;

        blurredColor /= 5.0;
        blurredColor.a = sampleA.a;

        return blurredColor;
    }

    // used in pass 1 (regular , weighted sep'ed blur)

    float4 fragBlur (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D(_MainTex, i.uv.xy);
        half4 sampleB = tex2D(_MainTex, i.uv01.xy);
        half4 sampleC = tex2D(_MainTex, i.uv01.zw);
        half4 sampleD = tex2D(_MainTex, i.uv23.xy);
        half4 sampleE = tex2D(_MainTex, i.uv23.zw);

        half sum = sampleA.a + dot(half4(1,1,1,1),half4(sampleB.a,sampleC.a,sampleD.a,sampleE.a));

        sampleA.rgb = sampleA.rgb * sampleA.a;
        sampleB.rgb = sampleB.rgb * sampleB.a;
        sampleC.rgb = sampleC.rgb * sampleC.a;
        sampleD.rgb = sampleD.rgb * sampleD.a;
        sampleE.rgb = sampleE.rgb * sampleE.a;

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

    // pass 0, the more expensive "BOKEH" version

    float4 fragBokeh (v2f i) : SV_Target {
        half4 blurredColor = float4 (0,0,0,0);

        half4 sampleA = tex2D(_MainTex, i.uv.xy);
        half4 sampleB = tex2D(_MainTex, i.uv01.xy);
        half4 sampleC = tex2D(_MainTex, i.uv01.zw);
        half4 sampleD = tex2D(_MainTex, i.uv23.xy);
        half4 sampleE = tex2D(_MainTex, i.uv23.zw);

        half sum = sampleA.a + dot(half4(1,1,1,1),half4(sampleB.a,sampleC.a,sampleD.a,sampleE.a));

        sampleA.rgb = sampleA.rgb * sampleA.a;
        sampleB.rgb = sampleB.rgb * sampleB.a;
        sampleC.rgb = sampleC.rgb * sampleC.a;
        sampleD.rgb = sampleD.rgb * sampleD.a;
        sampleE.rgb = sampleE.rgb * sampleE.a;

        blurredColor += sampleA;//*4;
        blurredColor += sampleB;//*2;
        blurredColor += sampleC;//*2;
        blurredColor += sampleD;
        blurredColor += sampleE;

        blurredColor /= sum;

        // WOW! WE HAVE ENUFF SAMPLES TO PERFORM DIVIDE 'N CONQUER
        // I WISH WE HAD MRT ... then we would save the blurred buffer along
        // with the maxxed buffer and do some more tricks

        half4 maxColor = max(sampleB,sampleC);
        half4 maxColorDnC = max(sampleD,sampleE);
        maxColor = max(maxColor,maxColorDnC);
        maxColor = max(maxColor /* /(maxColor.a+0.001) */,(sampleA));

        half4 color = lerp(blurredColor, maxColor, (saturate(Luminance(maxColor.rgb)-_Threshhold.x)*_Threshhold.y));
        color = max(color,blurredColor);

        //color -= (maxColor-blurredColor)*saturate(maxColor.a-0.5);

        color.a = maxColor.a;// * saturate(sampleA.a*1000.0);

        return color;
    }

    // pass 3:
    // happens before applying final coc/blur result to screen,
    // mixes various low rez buffers according to settings etc
    // (nice we can do it in half/quarter resolution)

    sampler2D _TapMedium;
    sampler2D _TapLow;

    half4 _TapMedium_ST;
    half4 _TapLow_ST;


    float4 fragPrepareForBgApply (v2fSingle i) : SV_Target
    {
        half4 tapMedium = tex2D(_TapMedium, UnityStereoScreenSpaceUVAdjust(i.uv.xy, _TapMedium_ST));
        half4 tapLow = tex2D(_TapLow, UnityStereoScreenSpaceUVAdjust(i.uv.xy, _TapLow_ST));

        tapLow.rgb =lerp(tapMedium.rgb,tapLow.rgb, pow(tapLow.a,1.5));

        return tapLow;
    }

    uniform float4 _MainTex_TexelSize;

    // pass 4:
    // NOT USED

    float4 fragPost (v2f i) : SV_Target
    {
        half4 tap = tex2D(_MainTex, i.uv.xy);
        half4 savedTap = tap;

         tap += tex2D(_MainTex, i.uv.xy + _MainTex_TexelSize.xy);
         tap += tex2D(_MainTex, i.uv.xy - _MainTex_TexelSize.xy);
         tap += tex2D(_MainTex, i.uv.xy + _MainTex_TexelSize.xy*half2(1,-1));
         tap += tex2D(_MainTex, i.uv.xy - _MainTex_TexelSize.xy*half2(1,-1));

        tap *= 0.2;

        tap =  savedTap - (savedTap-tap)*0.18;
        tap.a = savedTap;
        return savedTap;
    }

    ENDCG

Subshader {
  Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBokeh

      ENDCG
  }
  Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBlur

      ENDCG
  }
  Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragBlurUnweighted

      ENDCG
  }
  Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertSingleTex
      #pragma fragment fragPrepareForBgApply

      ENDCG
  }
  // pass 4:
  Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragPost

      ENDCG
  }
}

Fallback off

} // shader
