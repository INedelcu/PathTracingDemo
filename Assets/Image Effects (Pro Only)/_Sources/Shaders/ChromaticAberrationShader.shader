// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/ChromaticAberrationShader" {
    Properties {
        _MainTex ("Base", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;

    float4 _MainTex_TexelSize;
    float _ChromaticAberration;


    v2f vert( appdata_img v ) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);

        #ifdef SHADER_API_D3D9
        if (_MainTex_TexelSize.y < 0)
             v.texcoord.y = 1.0 - v.texcoord.y ;
        #endif

        o.uv = v.texcoord.xy;
        return o;
    }

    half4 fragSimpleCopy(v2f i) : SV_Target {
        return tex2D (_MainTex, i.uv.xy);
    }

    half4 frag(v2f i) : SV_Target {
        half2 coords = i.uv;
        half2 uv = i.uv;

        coords = (coords - 0.5) * 2.0;
        half coordDot = dot (coords,coords);

        float2 uvG = uv - _MainTex_TexelSize.xy * _ChromaticAberration * coords * coordDot;
        half4 color = tex2D (_MainTex, uv);
        #if SHADER_API_D3D9
            // Work around Cg's code generation bug for D3D9 pixel shaders :(
            color.g = color.g * 0.0001 + tex2D (_MainTex, uvG).g;
        #else
            color.g = tex2D (_MainTex, uvG).g;
        #endif

        return color;
    }

    ENDCG

Subshader {
 Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma vertex vert
      #pragma fragment fragSimpleCopy

      ENDCG
  }
Pass {
      ZTest Always Cull Off ZWrite Off
      Fog { Mode off }

      CGPROGRAM

      #pragma vertex vert
      #pragma fragment frag

      ENDCG
  }
}

Fallback off

} // shader
