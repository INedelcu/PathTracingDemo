// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

 Shader "Hidden/Dof/DepthOfField34" {
    Properties {
        _MainTex ("Base", 2D) = "" {}
        _TapLowBackground ("TapLowBackground", 2D) = "" {}
        _TapLowForeground ("TapLowForeground", 2D) = "" {}
        _TapMedium ("TapMedium", 2D) = "" {}

        // for fragApplyLayer only
        // _ColorBuffer ("ColorBuffer", 2D) = "" {}
        // _LuminanceHeuristic ("LuminanceHeuristic", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv1 : TEXCOORD0;
    };

    struct v2fDofApply {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 uv1[4] : TEXCOORD1;
    };

    sampler2D _MainTex;
    sampler2D_float _CameraDepthTexture;
    sampler2D _TapLowBackground;
    sampler2D _TapLowForeground;
    sampler2D _TapMedium;

    float4 _CurveParams;
    uniform float4 _MainTex_TexelSize;

    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos (v.vertex);
        o.uv1.xy = v.texcoord.xy;

        return o;
    }

    v2fDofApply vertDofApply( appdata_img v ) {
        v2fDofApply o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv.xy = v.texcoord.xy;

        const half2 blurOffsets[4] = {
            half2(-0.5, +1.5),
            half2(+0.5, -1.5),
            half2(+1.5, +0.5),
            half2(-1.5, -0.5)
        };

        o.uv1[0].xy = v.texcoord.xy + _MainTex_TexelSize.xy * blurOffsets[0];
        o.uv1[1].xy = v.texcoord.xy + _MainTex_TexelSize.xy * blurOffsets[1];
        o.uv1[2].xy = v.texcoord.xy + _MainTex_TexelSize.xy * blurOffsets[2];
        o.uv1[3].xy = v.texcoord.xy + _MainTex_TexelSize.xy * blurOffsets[3];

        return o;
    }

    float _ForegroundBlurExtrude;

    struct v2fDown {
        float4 pos : SV_POSITION;
        float2 uv0 : TEXCOORD0;
        float2 uv[2] : TEXCOORD1;
    };

    uniform float2 _InvRenderTargetSize;

    v2fDown vertDownsample(appdata_img v) {
        v2fDown o;
        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv0.xy = v.texcoord.xy;
        o.uv[0].xy = v.texcoord.xy + float2( -1.0, -1.0 ) * _InvRenderTargetSize;
        o.uv[1].xy = v.texcoord.xy + float2( +1.0, -1.0 ) * _InvRenderTargetSize;

        return o;
    }

    // @NOTE: this actually fucks with the clean mask,
    // mixing COC weightes of foreground and background,
    // the result however is not very perceivable

    half4 fragDownsample(v2fDown i) : SV_Target {
        float2 rowOfs[4];

        rowOfs[0] = 0;
        rowOfs[1] = half2(0.0, _InvRenderTargetSize.y);
        rowOfs[2] = half2(0.0, _InvRenderTargetSize.y) * 2;
        rowOfs[3] = half2(0.0, _InvRenderTargetSize.y) * 3;

        half4 color = (tex2D(_MainTex, i.uv0.xy));

        color += (tex2D(_MainTex, i.uv[0].xy + rowOfs[0]));
        color += (tex2D(_MainTex, i.uv[1].xy + rowOfs[0]));
        color += (tex2D(_MainTex, i.uv[0].xy + rowOfs[2]));
        color += (tex2D(_MainTex, i.uv[1].xy + rowOfs[2]));

        color /= 5;

        return color;
    }

    void getSmallBlurWeighted(out half4 weighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh) {
        weighted = half4(tapHigh.rgb,1.0) * tapHigh.a;

        half4 tap = tex2D(hrColorMap, i.uv1[0].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;

        tap = tex2D(hrColorMap, i.uv1[1].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;

        tap = tex2D(hrColorMap, i.uv1[2].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;

        tap = tex2D(hrColorMap, i.uv1[3].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;

        weighted /= weighted.a+0.0000001;
    }

    void getSmallBlurUnweighted(out half4 unweighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh) {
        unweighted = tapHigh;

        half4 tap = tex2D(hrColorMap, i.uv1[0].xy);
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[1].xy);
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[2].xy);
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[3].xy);
        unweighted += tap;

        unweighted /= 5.0;
    }

    void getSmallBlur(out half4 weighted, out half4 unweighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh) {
        weighted = half4(0,0,0,1);
        unweighted = half4(0,0,0,0);

        weighted += half4(tapHigh.rgb,1.0) * tapHigh.a;
        unweighted += tapHigh;

        half4 tap = tex2D(hrColorMap, i.uv1[0].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[1].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[2].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;
        unweighted += tap;

        tap = tex2D(hrColorMap, i.uv1[3].xy);
        weighted += half4(tap.rgb,1.0) * tap.a;
        unweighted += tap;

        weighted /= weighted.a;
        unweighted /= 5.0;
    }

    half4 fragDofApplyBg (v2fDofApply i) : SV_Target {
        half4 finalColor = half4 (0.0, 0.0, 0.0, 1.0);

        // @NOTE: tapLow and tapMedium have already been mixed in a low rez pass
        half4 tapHigh = tex2D (_MainTex, i.uv.xy);

        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            i.uv.xy = i.uv.xy * half2(1,-1)+half2(0,1);
        #endif

        half4 tapLow = tex2D (_TapLowBackground, i.uv.xy);

        // @NOTE: pow -- depending on falloff curves -- might not be needed
        finalColor = lerp (tapHigh, tapLow, pow(tapHigh.a,0.5));

        return finalColor;
    }

    half4 fragDofApplyBgDebug (v2fDofApply i) : SV_Target {
        half4 tapHigh = tex2D (_MainTex, i.uv.xy);
        half4 tapLow = tex2D (_TapLowBackground, i.uv.xy);

        // @NOTE: need to simulate the low rez pass mixing here
        half4 tapMedium = tex2D (_TapMedium, i.uv.xy);
        tapMedium.rgb = (tapMedium.rgb + half3 (1, 1, 0)) * 0.5;
        tapLow.rgb = (tapLow.rgb + half3 (0, 1, 0)) * 0.5;

        tapLow = lerp (tapMedium, tapLow, saturate (tapLow.a * tapLow.a));
        tapLow = tapLow * 0.5 + tex2D (_TapLowBackground, i.uv.xy) * 0.5;

        return lerp (tapHigh, tapLow, tapHigh.a);
    }

    half4 fragDofApplyFg (v2fDofApply i) : SV_Target {

        half4 fgBlur = tex2D(_TapLowForeground, i.uv.xy);
        half4 fgColor = tex2D(_MainTex,i.uv.xy);

        // many different ways to combine the blurred coc and the high resolution coc
        // we are using the 2*blurredCoc-highResCoc from CallOfDuty, 'cause it seems to
        // give most satisfying results for most cases

        //fgBlur.a = saturate(fgBlur.a*_ForegroundBlurWeight+saturate(fgColor.a-fgBlur.a));
        fgBlur.a = max (fgColor.a, (2.0 * fgBlur.a - fgColor.a)) * _ForegroundBlurExtrude;

        return lerp (fgColor, fgBlur, saturate(fgBlur.a));
    }

    half4 fragDofApplyFgDebug (v2fDofApply i) : SV_Target {
        half4 fgBlur = tex2D(_TapLowForeground, i.uv.xy);
        half4 fgColor = tex2D(_MainTex,i.uv.xy);

        fgBlur.a = max (fgColor.a, (2.0*fgBlur.a-fgColor.a)) * _ForegroundBlurExtrude;

        half4 tapMedium = half4 (1, 1, 0, fgBlur.a);
        tapMedium.rgb = 0.5 * (tapMedium.rgb + fgColor.rgb);

        fgBlur.rgb = 0.5 * (fgBlur.rgb + half3(0,1,0));
        fgBlur.rgb = lerp (tapMedium.rgb, fgBlur.rgb, saturate (fgBlur.a * fgBlur.a));

        return lerp ( fgColor, fgBlur, saturate(fgBlur.a));
    }

    half4 fragCocBg (v2f i) : SV_Target {
        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1.xy);
        d = Linear01Depth (d);
        half coc = 0.0;

        half focalDistance01 = _CurveParams.w + _CurveParams.z;

        if (d > focalDistance01)
            coc = (d - focalDistance01);

        coc = saturate (coc * _CurveParams.y);

        return coc;
    }

    half4 fragCocAndColorFg (v2f i) : SV_Target {
        half4 color = tex2D (_MainTex, i.uv1.xy);
        color.a = 0.0;

        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0)
            i.uv1.xy = i.uv1.xy * half2(1,-1)+half2(0,1);
        #endif

        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1.xy);
        d = Linear01Depth (d);

        half focalDistance01 = (_CurveParams.w - _CurveParams.z);

        if (d < focalDistance01)
            color.a = (focalDistance01 - d);

        color.a = saturate (color.a * _CurveParams.x);

        return color;
    }

    // fragMask
    // not being used atm

    half4 fragMask (v2f i) : SV_Target {
        half4 color = tex2D(_MainTex, i.uv1.xy);
        half4 colorB = tex2D(_MainTex, i.uv1.xy + _MainTex_TexelSize.xy * 3);
        half4 colorC = tex2D(_MainTex, i.uv1.xy - _MainTex_TexelSize.xy * 3);
        half4 colorD = tex2D(_MainTex, i.uv1.xy + _MainTex_TexelSize.xy * half2(1,-1) * 3);

        half diff = Luminance ( abs(color.rgb * 5 - colorB.rgb - colorC.rgb - colorD.rgb));

        half4 colorM2 = color;
        if (diff < 0.40)
            colorM2.rgb *= 0;
        return 0;
    }

    half4 fragCombine (v2f i) : SV_Target {
        half4 fgColorAndCoc = tex2D( _MainTex, i.uv1.xy );
        half4 bgColorAndCoc = tex2D( _TapMedium, i.uv1.xy );

        half4 color = fgColorAndCoc * saturate( 2.0 * fgColorAndCoc.a ) + bgColorAndCoc * saturate( 2.0 * bgColorAndCoc.a );
        if(fgColorAndCoc.a > 0.0 && bgColorAndCoc.a > 0.0)
            color.a = min (fgColorAndCoc.a, bgColorAndCoc.a);
        return color;
    }

    // fragApplyLayer
    // blends e.g. bokeh's into the defocused textures for fore- and background bokeh effects

    // sampler2D _ColorBuffer;          // needed for this pass only
    // sampler2D _LuminanceHeuristic;   // needed for this pass only

    uniform half _BlendStrength;    // needed for this pass only

    half4 fragApplyLayer (v2f i) : SV_Target {
        half4 from = tex2D( _MainTex, i.uv1.xy );

        // half lum = saturate( Luminance (to.rgb));
        // to.rgb *= saturate(1.75 - Luminance(to.rgb+from.rgb*_BlendStrength));
        // return to + from * _BlendStrength;
        // return 1 - ( 1 - from ) * ( 1 - ( to ) );

        half4 outColor = _BlendStrength * from;

        return outColor;
    }

    ENDCG

Subshader {

 // pass 0

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplyBg

      ENDCG
    }

 // pass 1

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplyFgDebug

      ENDCG
    }

 // pass 2

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplyBgDebug

      ENDCG
    }

 // pass 3

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask A
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCocBg

      ENDCG
    }


 // pass 4


 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplyFg

      ENDCG
    }

 // pass 5

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask ARGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCocAndColorFg

      ENDCG
    }

 // pass 6

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGBA
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDownsample
      #pragma fragment fragDownsample

      ENDCG
    }

 // pass 7
 // not being used atm

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGBA
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragMask

      ENDCG
    }

 // pass 8

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGBA
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCombine

      ENDCG
    }

 // pass 9

 Pass {
      ZTest Always Cull Off ZWrite Off
      Blend OneMinusDstColor One
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragApplyLayer

      ENDCG
    }
  }

Fallback off

}
