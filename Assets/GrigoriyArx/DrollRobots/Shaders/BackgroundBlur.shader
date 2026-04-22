Shader "Hidden/BackgroundBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "KawaseBlur"
            ZTest Always ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Offset;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _BlitTexture_TexelSize.xy;

                half4 col = 0;
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-_Offset, -_Offset) * texelSize);
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-_Offset,  _Offset) * texelSize);
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( _Offset, -_Offset) * texelSize);
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( _Offset,  _Offset) * texelSize);
                col *= 0.25;

                return col;
            }
            ENDHLSL
        }
    }
}
