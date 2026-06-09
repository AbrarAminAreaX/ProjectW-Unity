Shader "ProjectW/Nebula"
{
    // Procedural warm-cosmic nebula for the Onboarding_Animation splash.
    // Domain-warped value-noise fbm clouds over a vertical gradient, slowly
    // scrolling so the background stays alive through every phase. Unlit,
    // drawn in the Background queue with ZWrite off so it always sits behind
    // the robot + UI. Tune colors/speed in the material.
    Properties
    {
        _ColorDeep   ("Deep Space Color",        Color) = (0.025, 0.012, 0.06, 1)
        _ColorMid    ("Nebula Mid Color",         Color) = (0.32, 0.07, 0.26, 1)
        _ColorWarm   ("Warm Highlight Color",     Color) = (0.98, 0.45, 0.18, 1)
        _Scale       ("Noise Scale",              Float) = 3.0
        _Speed       ("Scroll Speed (xy)",        Vector) = (0.012, 0.006, 0, 0)
        _Intensity   ("Cloud Intensity",          Range(0,3)) = 1.5
        _WarpAmount  ("Domain Warp",              Range(0,2)) = 0.7
        _GradientPow ("Vertical Gradient Power",  Range(0.1,4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Background" }
        Pass
        {
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _ColorDeep;
            float4 _ColorMid;
            float4 _ColorWarm;
            float4 _Speed;
            float  _Scale;
            float  _Intensity;
            float  _WarpAmount;
            float  _GradientPow;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash(i + float2(0, 0));
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    v += amp * vnoise(p);
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y;
                float2 motion = _Speed.xy * t;

                // Domain warp for a wispy, organic nebula rather than blobby noise.
                float warp = fbm(uv * _Scale + motion);
                float clouds = fbm(uv * _Scale * 1.7 - motion * 1.3 + warp * _WarpAmount);
                clouds = saturate(clouds * _Intensity);

                // Mostly deep space. A faint vertical gradient lifts the base
                // toward the mid tone; nebula wisps add color only where dense,
                // and the warm highlight shows only at the brightest cloud cores
                // so the frame stays dark and cosmic, not a wall of fire.
                float g = pow(saturate(uv.y), _GradientPow);
                float3 col = lerp(_ColorDeep.rgb, _ColorMid.rgb, g * 0.35);

                float midMask  = smoothstep(0.35, 0.78, clouds);
                float warmMask = smoothstep(0.72, 0.96, clouds);
                col = lerp(col, _ColorMid.rgb,  midMask * 0.75);
                col = lerp(col, _ColorWarm.rgb, warmMask * 0.65);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
