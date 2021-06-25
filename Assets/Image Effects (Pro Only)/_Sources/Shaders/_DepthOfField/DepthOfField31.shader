// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

 Shader "Hidden/DepthOfField31" {
    Properties {
        _MainTex ("Base (RGB)", 2D) = "" {}
        _BgLowRez ("_BgLowRez (RGB)", 2D) = "" {}
        _FgLowRez ("_FgLowRez (RGB)", 2D) = "" {}
        _BgUnblurredTex ("_BgUnblurredTex (RGB)", 2D) = "" {}
        _MaskTex ("_BTest (RGB)", 2D) = "" {}
        _SourceTex("_BTest (RGB)", 2D) = "" {}
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
    sampler2D _BgLowRez;
    sampler2D _FgLowRez;
    sampler2D _BgUnblurredTex;

    uniform float focalDistance01;
    uniform float focalFalloff;

    uniform float4 _BokehThreshhold;

    uniform float focalStart01;
    uniform float focalEnd01;
    uniform float focalSize;

    uniform float4 _MainTex_ST;
    uniform float4 _BgLowRez_ST;
    uniform float4 _MainTex_TexelSize;
    uniform float4 _CameraDepthTexture_ST;
    uniform float4 _CameraDepthTexture_TexelSize;

    float4 _CurveParams;

    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv1.xy = TRANSFORM_TEX(v.texcoord, _CameraDepthTexture);

        return o;
    }


    v2fDofApply vertDofApply( appdata_img v )
    {
        v2fDofApply o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv.xy = UnityStereoScreenSpaceUVAdjust(v.texcoord.xy, _MainTex_ST);

        const half2 blurOffsets[4] = {
            half2(-0.5, +1.5),
            half2(+0.5, -1.5),
            half2(+1.5, +0.5),
            half2(-1.5, -0.5)
        };

    o.uv1[0].xy = (v.texcoord.xy + _MainTex_TexelSize.xy*blurOffsets[0]);
        o.uv1[1].xy = (v.texcoord.xy + _MainTex_TexelSize.xy*blurOffsets[1]);
        o.uv1[2].xy = (v.texcoord.xy + _MainTex_TexelSize.xy*blurOffsets[2]);
        o.uv1[3].xy = (v.texcoord.xy + _MainTex_TexelSize.xy*blurOffsets[3]);

        return o;
    }

    float _ForegroundBlurWeight;

    // downsample thingie (& calc COC)

    struct v2fDown
    {
        float4 pos : SV_POSITION;
        float2 uv0 : TEXCOORD0;
        float2 uv[2] : TEXCOORD1;
    };

    uniform float2 _InvRenderTargetSize;

    v2fDown vertDownsample(appdata_img v)
    {
        v2fDown o;
        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv0.xy = v.texcoord.xy;
        o.uv[0].xy = v.texcoord.xy + float2( -1.0, -1.0 ) * _InvRenderTargetSize;
        o.uv[1].xy = v.texcoord.xy + float2( +1.0, -1.0 ) * _InvRenderTargetSize;

        return o;
    }

    // @NOTE: this actually fucks with the mask, mixing
    // COC weightes of foreground and background,
    // the result however is very non perceivable

    half4 fragDownsample(v2fDown i) : SV_Target
    {
        float2 rowOfs[4];

        rowOfs[0] = 0;
        rowOfs[1] = half2(0.0, _InvRenderTargetSize.y);
        rowOfs[2] = half2(0.0, _InvRenderTargetSize.y) * 2;
        rowOfs[3] = half2(0.0, _InvRenderTargetSize.y) * 3;

        half4 color = tex2D(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv0.xy, _MainTex_ST));

        half4 sampleA = tex2D(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv[0].xy + rowOfs[0], _MainTex_ST));
        half4 sampleB = tex2D(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv[1].xy + rowOfs[0], _MainTex_ST));
        half4 sampleC = tex2D(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv[0].xy + rowOfs[2], _MainTex_ST));
        half4 sampleD = tex2D(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv[1].xy + rowOfs[2], _MainTex_ST));

        color += sampleA + sampleB + sampleC + sampleD;
        color *= 0.2;

        return color;
    }

    void getSmallBlurWeighted(out half4 weighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh)
    {
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

    void getSmallBlurUnweighted(out half4 unweighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh)
    {
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

    void getSmallBlur(out half4 weighted, out half4 unweighted, sampler2D hrColorMap, v2fDofApply i, half4 tapHigh)
    {
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

    half3 getBlendWeights(half coc, half xc)
    {
        half3 weights;

        weights.z = saturate(coc-xc) / (1.0-xc); // w2
        weights.x = saturate(coc/xc); // w1
        weights.y = (1.0-weights.z) * weights.x;
        weights.x = (1.0-weights.x) * (1.0-weights.z);

        return weights;
    }

    half4 fragDofApplyBg(v2fDofApply i) : SV_Target
    {
        // NOTE: moved some stuff to low2 resolution calculations

        half4 finalColor = half4(0.0,0.0,0.0,1.0);
        half4 tapHigh = tex2D(_MainTex, i.uv.xy);
        half4 tapLow = tex2D(_BgLowRez, i.uv.xy);

        /*
        #if defined(DEBUG_BLUR_REGIONS_ON)
            half4 tapMedium = half4(1,0,0,tex2D(_BgUnblurredTex, i.uv.xy).a);
            tapLow = half4(0,1,0,tapLow.a);
            tapLow = lerp(tapMedium,tapLow, saturate(tapLow.a-0.5)*2);
        #endif
        */

        finalColor = lerp(tapHigh, tapLow, ((tapHigh.a)));

        return finalColor;
    }

    half4 fragDofApplySimple(v2fDofApply i) : SV_Target
    {
        half4 finalColor = half4(0.0,0.0,0.0,1.0);
        half4 tapHigh = tex2D(_MainTex, i.uv.xy);
        half4 tapLow = tex2D(_BgLowRez, i.uv.xy);
        half4 tapMedium = tex2D(_BgUnblurredTex, i.uv.xy);

        /*
        #if defined(DEBUG_BLUR_REGIONS_ON)
            tapMedium = half4(1,0,0,tapMedium.a);
            tapLow = half4(0,1,0,tapLow.a);
        #endif
        */

        tapLow = lerp(tapMedium,tapLow, saturate(tapLow.a-0.5)*2);
        finalColor = lerp(tapHigh, tapLow, saturate(tapHigh.a));

        return finalColor;
    }

    half4 fragDofApplyVerySimple(v2fDofApply i) : SV_Target
    {
        half4 finalColor = half4(0.0,0.0,0.0,1.0);
        half4 tapHigh = tex2D(_MainTex, i.uv.xy);
        half4 tapLow = tex2D(_BgLowRez, i.uv.xy);
        half4 tapMedium = tex2D(_BgUnblurredTex, i.uv.xy);

        /*
        #if defined(DEBUG_BLUR_REGIONS_ON)
            tapMedium = half4(1,0,0,tapMedium.a);
            tapLow = half4(0,1,0,tapLow.a);
        #endif
        */

        tapLow = lerp(tapMedium,tapLow, saturate(tapLow.a-0.5)*2);
        finalColor = lerp(tapHigh, tapLow, saturate(tapHigh.a*2));

        return finalColor;
    }

    // NOTE: The Foreground Blur
    //
    // as this is an extra pass for getting nice foreground blurs
    // we should keep this shader as simple as possible
    //
    // (in the beginning of times, it was a full blown DOF apply,
    //  with sampling 3 blur textures and such, but that turned
    //  out overkill for real world use cases)
    //
    // so let's just sample the low rez and high rez colors,
    // and blend based on 2 COC values and a foreground blur weight

    half4 fragDofApplyFg(v2fDofApply i) : SV_Target
    {
        // get values
        //half4 tapHigh = tex2D(_MainTex, i.uv.xy);

        half4 fgBlur = tex2D(_FgLowRez, i.uv.xy);
        half4 fgColor = tex2D(_MainTex,i.uv.xy);

        // many different ways to combine the blurred coc and the high resolution coc
        // we are using the 2*blurredCoc-highResCoc from COD, casue overall it gives
        // most satisfying results for most cases

        //fgBlur.a = saturate(fgBlur.a*_ForegroundBlurWeight+saturate(fgColor.a-fgBlur.a));
        fgBlur.a = max(fgColor.a, (2.0*fgBlur.a-fgColor.a))* _ForegroundBlurWeight;

        /*
        #if defined(DEBUG_BLUR_REGIONS_ON)
            fgBlur = half4(0,1,0, fgBlur.a);
        #endif
        */

        // final blend and output
        half4 finalColor  = lerp(fgColor, fgBlur, saturate(fgBlur.a));
        return finalColor;
    }

    half4 fragPreBg(v2f i) : SV_Target
    {
        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1.xy);
        d = Linear01Depth(d);
        half preDof = 0.0;

        float modifiedFocalDistance01 = (focalDistance01+focalSize);

        if(d>modifiedFocalDistance01)
            preDof = saturate(d-modifiedFocalDistance01) * _CurveParams.y/modifiedFocalDistance01;


        preDof *= focalFalloff;
        return saturate(preDof);
    }

    half4 fragCocBgCenter(v2f i) : SV_Target
    {
        return half4(1,0,1,0);
    }

    half4 fragCocFgCenter(v2f i) : SV_Target
    {
        return half4(1,0,1,0);
    }

    half4 fragCocFg(v2f i) : SV_Target
    {
        half4 color = tex2D(_MainTex,  UnityStereoScreenSpaceUVAdjust(i.uv1.xy, _MainTex_ST));

        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1.xy);
        d = Linear01Depth(d);

        half preDof = 0.0;
        float modifiedFocalDistance01 = (focalDistance01-focalSize);

        if(d>modifiedFocalDistance01)
            preDof = 0.0;
        else
            preDof = saturate(modifiedFocalDistance01-d)  * _CurveParams.x/modifiedFocalDistance01;

        color.a = saturate(preDof * focalFalloff);
        return color;
    }

    half4 fragPreFgBgCenter(v2f i) : SV_Target
    {
        return half4(1,0,1,0);

    }

    half4 fragPreFgBg(v2f i) : SV_Target
    {
        float preDof = 0.0;

        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1.xy);
        d = Linear01Depth(d);

        if(d>focalDistance01)
            preDof = saturate(d-focalDistance01-focalSize) * _CurveParams.y/(focalDistance01+focalSize);
        else
            preDof = saturate(focalDistance01-d-focalSize) * _CurveParams.x/(focalDistance01-focalSize);

        return saturate(preDof*focalFalloff);
    }

    struct v2fFgBlur {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;

        float4 uv01 : TEXCOORD1;
        float4 uv23 : TEXCOORD2;
        float4 uv45 : TEXCOORD3;
    };

    float2 _Vh;

    v2fFgBlur vertFgBlur (appdata_img v) {
        v2fFgBlur o;

        o.pos = UnityObjectToClipPos(v.vertex);

        o.uv.xy = v.texcoord.xy;

        half2 offsets = _MainTex_TexelSize.xy * _Vh.xy;

        o.uv01 =  UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy +  offsets.xyxy * float4(1,1, -1,-1), _MainTex_ST);
        o.uv23 =  UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy +  offsets.xyxy * float4(1,1, -1,-1) * 2.0, _MainTex_ST);
        o.uv45 =  UnityStereoScreenSpaceUVAdjust(v.texcoord.xyxy +  offsets.xyxy * float4(1,1, -1,-1) * 3.0, _MainTex_ST);

        return o;
    }

    half4 fragFgBlurBokeh (v2fFgBlur i) : SV_Target
    {
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

        blurredColor /= 5;

        half4 maxColor = max(sampleB*sampleB.a, max(sampleC*sampleC.a, max(sampleD*sampleD.a, sampleE*sampleE.a)));// * 0.97;
        maxColor = max(maxColor,sampleA*sampleA.a);
        half4 color = lerp(blurredColor, maxColor, saturate((Luminance(maxColor.rgb)-_BokehThreshhold.x)*_BokehThreshhold.y));
        color = max(color,blurredColor);

        color.a = saturate(blurredColor.a*(1.0+Luminance(abs(color.rgb-sampleA.rgb))));//blurredColor.a;// lerp(blurredColor.a, maxColor.a, saturate(Luminance((color.rgb))));

        //color.rgb += (maxColor-sampleA*sampleA.a).rgb * _BokehThreshhold.w; //*saturate(sampleA.a-0.5);

        return color;
    }

    half4 fragFgBlurSimple (v2fFgBlur i) : SV_Target
    {
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

        blurredColor /= 5;

        return blurredColor;
    }

    ENDCG

Subshader {

 // pass 0
 // 0 -> coc for BG

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
 // 1 -> dof apply (LQ)

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplyVerySimple

      ENDCG
    }

 // pass 2
 // 2 -> dof apply (MQ)

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertDofApply
      #pragma fragment fragDofApplySimple

      ENDCG
    }

 // pass 3
 // 3 -> create COC map for bg and fg

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask A
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragPreFgBg

      ENDCG
    }

 // pass 4
 // 4 -> create COC map for background

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask A
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragPreBg

      ENDCG
    }

 // pass 5
 // FREE NOW

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask A
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragPreFgBgCenter

      ENDCG
    }

  // pass 6
  // 6 -> custom blur

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask RGBA
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertFgBlur
      #pragma fragment fragFgBlurSimple

      ENDCG
    }

 // pass 7
 // FREE NOW

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask A
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCocBgCenter

      ENDCG
    }

  // pass 8
  // FREE NOW

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask ARGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCocFgCenter

      ENDCG
    }

  // pass 9
  // 9 -> custom blur

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask ARGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vertFgBlur
      #pragma fragment fragFgBlurBokeh

      ENDCG
    }

  // pass 10
  // 10 -> coc for FG

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

  // pass 11
  // 11 -> ole test

 Pass {
      ZTest Always Cull Off ZWrite Off
      ColorMask ARGB
      Fog { Mode off }

      CGPROGRAM
      #pragma fragmentoption ARB_precision_hint_fastest
      #pragma vertex vert
      #pragma fragment fragCocFg

      ENDCG
    }

 // --------------------------------------------
 // pass 12
 // 12 -> downsample test

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

  }

Fallback off

}
