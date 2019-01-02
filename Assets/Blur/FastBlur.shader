// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/BlurOnce" {
    Properties{
      _MainTex("Base (RGB)", 2D) = "white" {}
    }

      CGINCLUDE

#include "UnityCG.cginc"

      sampler2D _MainTex;
    uniform half4 _MainTex_TexelSize;
    uniform half _Parameter;
    uniform fixed _Light = 1;

    struct v2f_tap
    {
      half4 pos : SV_POSITION;
      half2 uv : TEXCOORD0;
    };

    v2f_tap vert4Tap(appdata_img v)
    {
      v2f_tap o;

      o.pos = UnityObjectToClipPos(v.vertex);
      o.uv = v.texcoord.xy;

      return o;
    }

    fixed4 fragDownsample(v2f_tap i) : COLOR
    {
      fixed4 color = tex2D(_MainTex, i.uv);
    return color * _Light;
    }

      // weight curves

      static const half4 curve4[7] = { half4(0.0205,0.0205,0.0205,0), half4(0.0855,0.0855,0.0855,0), half4(0.232,0.232,0.232,0),
      half4(0.324,0.324,0.324,1), half4(0.232,0.232,0.232,0), half4(0.0855,0.0855,0.0855,0), half4(0.0205,0.0205,0.0205,0) };

    struct v2f_withBlurCoords8
    {
      half4 pos : SV_POSITION;
      half2 uv : TEXCOORD0;
      half2 offs : TEXCOORD1;
    };

    v2f_withBlurCoords8 vertBlurHorizontal(appdata_img v)
    {
      v2f_withBlurCoords8 o;
      o.pos = UnityObjectToClipPos(v.vertex);

      o.uv = v.texcoord.xy;
      o.offs = _MainTex_TexelSize.xy * half2(1.0, 0.0) * _Parameter;

      return o;
    }

    v2f_withBlurCoords8 vertBlurVertical(appdata_img v)
    {
      v2f_withBlurCoords8 o;
      o.pos = UnityObjectToClipPos(v.vertex);

      o.uv = v.texcoord.xy;
      o.offs = _MainTex_TexelSize.xy * half2(0.0, 1.0) * _Parameter;

      return o;
    }

    half4 fragBlur8(v2f_withBlurCoords8 i) : COLOR
    {
      half2 netFilterWidth = i.offs;
      half2 coords = i.uv - netFilterWidth * 3.0f;

      half4 color = 0;
      for (int l = 0; l < 7; l++)
      {
        half4 tap = tex2D(_MainTex, coords);
        color += tap * curve4[l];
        coords += netFilterWidth;
      }

      return color;
    }

      ENDCG

      SubShader {
      ZTest Off Cull Off ZWrite Off Blend Off
        Fog{ Mode off }

        // 0
        Pass{
        CGPROGRAM

#pragma vertex vert4Tap
#pragma fragment fragDownsample
#pragma fragmentoption ARB_precision_hint_fastest 

        ENDCG

      }

        // 1
        Pass{
        CGPROGRAM

#pragma vertex vertBlurVertical
#pragma fragment fragBlur8
#pragma fragmentoption ARB_precision_hint_fastest 

        ENDCG
      }

        // 2
        Pass{
        CGPROGRAM

#pragma vertex vertBlurHorizontal
#pragma fragment fragBlur8
#pragma fragmentoption ARB_precision_hint_fastest 

        ENDCG
      }

    }

    FallBack Off
  }
