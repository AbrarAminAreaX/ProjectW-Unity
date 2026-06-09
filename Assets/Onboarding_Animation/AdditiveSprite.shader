Shader "ProjectW/AdditiveSprite"
{
    // Unlit additive textured quad for the glowing world-space elements
    // (chest heart, face smile). Black areas of the source add nothing, so
    // the glow reads as transparent. Fade via the _Color alpha (driven from a
    // MaterialPropertyBlock by the controller).
    Properties
    {
        _MainTex   ("Texture",   2D)    = "white" {}
        _Color     ("Tint",      Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float  _Intensity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half3 col = tex.rgb * _Color.rgb * _Intensity * _Color.a;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
