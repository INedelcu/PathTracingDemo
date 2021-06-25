
Shader "Hidden/Dof/Bokeh34" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
    _Source ("Base (RGB)", 2D) = "black" {}
    // _Debug ("Debug (RGB)", 2D) = "black" {}
    // _LuminanceHeuristic ("LumHeur (RGB)", 2D) = "black" {}
}

SubShader {
    Pass {
        Blend OneMinusDstColor One
        // BlendOp Max

        ZTest Always Cull Off ZWrite Off
        Fog { Mode off }

        CGPROGRAM

        #pragma fragmentoption ARB_precision_hint_fastest
        #pragma vertex vert
        #pragma fragment frag
        #pragma glsl

        #pragma target 3.0

        #include "UnityCG.cginc"

        sampler2D _MainTex;
        // sampler2D _Debug;
        sampler2D _Source;
        // sampler2D _LuminanceHeuristic;

        // aspect ratio correction

        uniform half2 _ArScale;

        // user tweaked values

        uniform half _Scale; // 0.065
        uniform half _Intensity; // 0.051251265
        uniform half _Threshhold; // 0.4

        struct v2f {
            float4 pos : SV_POSITION;
            float2 uv2 : TEXCOORD0;
            float4 source : TEXCOORD1;
        };

        v2f vert (appdata_full v)
        {
            v2f o;

            o.pos = v.vertex;

            o.uv2.xy = v.texcoord.xy*2;

            #ifdef UNITY_UV_STARTS_AT_TOP
                o.source = tex2Dlod (_Source, half4 (v.texcoord1.xy * half2(1,-1) + half2(0,1), 0, 0));
            #else
                o.source = tex2Dlod (_Source, half4 (v.texcoord1.xy, 0, 0));
            #endif

            half coc = _Scale * o.source.a * step (_Threshhold, dot (o.source.rgb, half3 (0.3,0.5,0.2)));

            o.pos.xy += (v.texcoord.xy * 2 - 1) * _ArScale * coc + coc * half2(0.5,0.75);

            o.source.rgb *= (_Intensity); // * ( 1.0 - (Luminance ( tex2Dlod(_LuminanceHeuristic,  half4 (v.texcoord1.xy, 0, 0)).rgb)));

            return o;
        }

        half4 frag (v2f i) : SV_Target
        {
            half4 color = tex2D (_MainTex, i.uv2);
            color.rgb *= i.source.rgb * i.source.a;

            return color;
        }

        ENDCG
    }
}

Fallback off

}
