Shader "ProjectW/Onboarding2/SpaceBackdrop"
{
    // Procedural green deep-space backdrop for the Onboarding_Animation splash.
    // Domain-warped value-noise fbm clouds over a vertical gradient, slowly
    // scrolling so the background stays alive through every phase. Unlit,
    // drawn in the Background queue with ZWrite off so it always sits behind
    // the robot + UI. Tune colors/speed in the material.
    Properties
    {
        _ColorDeep   ("Deep Space Color",        Color) = (0.005, 0.012, 0.014, 1)
        _ColorMid    ("Nebula Mid Color",         Color) = (0.03, 0.28, 0.16, 1)
        _ColorWarm   ("Bright Green Highlight",   Color) = (0.35, 1.0, 0.62, 1)
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

            float planetMask(float2 uv, float2 center, float radius, float feather)
            {
                return smoothstep(radius + feather, radius - feather, length(uv - center));
            }

            float ringMask(float2 uv, float2 center, float2 scale, float width, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                float2 p = uv - center;
                p = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                float d = abs(length(p / scale) - 1.0);
                return smoothstep(width, 0.0, d);
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

                // Mostly deep space. Keep the center calmer and darker so the
                // robot stays readable while the green nebula wraps the edges.
                float g = pow(saturate(uv.y), _GradientPow);
                float3 col = lerp(_ColorDeep.rgb, _ColorMid.rgb, g * 0.35);

                float midMask  = smoothstep(0.35, 0.78, clouds);
                float warmMask = smoothstep(0.72, 0.96, clouds);
                float centerCalm = 1.0 - smoothstep(0.05, 0.42, length(uv - float2(0.5, 0.5)));
                float edgeBias = saturate(1.0 - centerCalm * 0.75);
                col = lerp(col, _ColorMid.rgb,  midMask * 0.85 * edgeBias);
                col = lerp(col, _ColorWarm.rgb, warmMask * 0.55 * edgeBias);

                float2 planetUv = uv;
                planetUv.x = (planetUv.x - 0.5) * 0.72 + 0.5;

                float ring = ringMask(planetUv, float2(0.86, 0.66), float2(0.18, 0.055), 0.035, -0.42);
                float ringOccluder = planetMask(planetUv, float2(0.86, 0.66), 0.105, 0.01);
                col = lerp(col, float3(0.55, 0.50, 0.43), ring * (1.0 - ringOccluder * 0.55) * 0.5);

                float p1 = planetMask(planetUv, float2(0.86, 0.66), 0.105, 0.012);
                float p1Shade = saturate(dot(normalize(float3((planetUv - float2(0.86, 0.66)) / 0.105, 0.65)), normalize(float3(-0.45, 0.55, 0.75))));
                float p1Bands = 0.5 + 0.5 * sin((planetUv.y + planetUv.x * 0.22) * 85.0);
                float3 p1Col = lerp(float3(0.18, 0.13, 0.10), float3(0.67, 0.55, 0.42), p1Shade);
                p1Col += p1Bands * 0.08;
                col = lerp(col, p1Col, p1 * 0.9);

                float p2 = planetMask(planetUv, float2(0.72, 0.95), 0.18, 0.018);
                float p2Shade = saturate(dot(normalize(float3((planetUv - float2(0.72, 0.95)) / 0.18, 0.75)), normalize(float3(-0.35, 0.7, 0.65))));
                float p2Cloud = fbm(planetUv * 18.0 + 4.0);
                float3 p2Col = lerp(float3(0.08, 0.18, 0.15), float3(0.62, 0.95, 0.78), p2Shade);
                p2Col = lerp(p2Col, float3(0.75, 1.0, 0.84), smoothstep(0.55, 0.82, p2Cloud) * 0.45);
                col = lerp(col, p2Col, p2 * 0.68);

                float p3 = planetMask(planetUv, float2(0.10, 0.08), 0.16, 0.02);
                float p3Shade = saturate(dot(normalize(float3((planetUv - float2(0.10, 0.08)) / 0.16, 0.7)), normalize(float3(0.45, 0.6, 0.65))));
                float3 p3Col = lerp(float3(0.10, 0.07, 0.055), float3(0.52, 0.44, 0.34), p3Shade);
                col = lerp(col, p3Col, p3 * 0.38);

                float vignette = smoothstep(0.78, 0.18, length(uv - float2(0.5, 0.5)));
                col *= lerp(0.72, 1.08, vignette);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
