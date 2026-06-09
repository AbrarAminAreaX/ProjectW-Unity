Shader "ProjectW/Stars"
{
    // Additive procedural starfield + faint drifting dust for the cosmic
    // backdrop. Three parallax layers of hashed-grid points that twinkle via
    // _Time, plus a soft low-frequency dust glow. Blends additively over the
    // Nebula quad. Sits on a camera-parented quad just in front of the nebula.
    Properties
    {
        _StarColor    ("Star Color",        Color) = (1.0, 0.95, 0.88, 1)
        _DustColor    ("Dust Glow Color",   Color) = (0.6, 0.35, 0.5, 1)
        _Density      ("Base Star Density", Float) = 28
        _Brightness   ("Star Brightness",   Range(0,4)) = 1.2
        _Coverage     ("Sparsity (hi=fewer)", Range(0.5,0.99)) = 0.86
        _TwinkleSpeed ("Twinkle Speed",     Float) = 1.8
        _Drift        ("Drift Speed",       Float) = 0.006
        _DustAmount   ("Dust Amount",       Range(0,2)) = 0.5
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

            float4 _StarColor;
            float4 _DustColor;
            float  _Density;
            float  _Brightness;
            float  _Coverage;
            float  _TwinkleSpeed;
            float  _Drift;
            float  _DustAmount;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i + float2(0, 0));
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // One layer of stars: hashed grid, jittered point per cell, soft
            // falloff, per-star twinkle.
            float starLayer(float2 uv, float density, float t)
            {
                uv *= density;
                float2 id = floor(uv);
                float2 gv = frac(uv) - 0.5;
                float r = hash21(id);
                if (r < _Coverage) return 0.0;          // empty cell
                float2 off = (float2(hash21(id + 1.7), hash21(id + 4.3)) - 0.5) * 0.7;
                float d = length(gv - off);
                float core = smoothstep(0.06, 0.0, d);  // soft round star
                float tw = 0.55 + 0.45 * sin(t * (0.5 + r * 3.0) + r * 6.2831);
                // Remap r above coverage to 0..1 so brightness varies per star.
                float mag = (r - _Coverage) / (1.0 - _Coverage);
                return core * tw * mag;
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
                float t = _Time.y * _TwinkleSpeed;
                float2 drift = float2(_Drift, _Drift * 0.6) * _Time.y;

                // Three parallax layers at different densities/offsets for depth.
                float stars =
                    starLayer(uv + drift,            _Density,        t) * 1.0 +
                    starLayer(uv * 1.7 + drift * 1.6, _Density * 0.7, t) * 0.7 +
                    starLayer(uv * 2.6 - drift * 0.8, _Density * 1.3, t) * 0.5;

                float3 col = _StarColor.rgb * stars * _Brightness;

                // Faint drifting dust glow so empty space isn't dead flat.
                float dust = vnoise(uv * 3.0 + drift * 3.0);
                dust = pow(saturate(dust), 3.0) * _DustAmount;
                col += _DustColor.rgb * dust;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
