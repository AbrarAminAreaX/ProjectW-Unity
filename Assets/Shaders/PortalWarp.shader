// Procedural swirling-portal effect for the Ship World → Flutter warp.
// Drawn on a full-screen Screen-Space-Overlay RawImage by PortalWarpController.
// Animate _Progress 0->1: a glowing vortex grows from the centre, spiral arms
// rotate, and at _Progress~1 it fully covers the screen (the navigation fires
// near the peak, so Flutter slides in under full coverage — no hard cut).
Shader "UI/PortalWarp"
{
    Properties
    {
        _Progress   ("Progress (0..1)", Range(0,1)) = 0
        _Color      ("Core Color", Color) = (0.45, 0.12, 0.85, 1)   // purple
        _RingColor  ("Ring/Arm Color", Color) = (0.85, 0.30, 1.0, 1) // magenta
        _Arms       ("Spiral Arms", Float) = 6
        _Swirl      ("Swirl Amount", Float) = 7
        _Speed      ("Spin Speed", Float) = 3
        // UI plumbing (so it works on a RawImage/Image material)
        _MainTex    ("Sprite (unused)", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            fixed4 _Color, _RingColor;
            float _Progress, _Arms, _Swirl, _Speed;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Centre UVs, correct for aspect so the portal stays circular.
                float2 p = i.uv - 0.5;
                p.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);

                float r = length(p);
                float ang = atan2(p.y, p.x);

                // Portal radius grows past the screen corner (~0.9 in this space)
                // so _Progress=1 fully covers. Ease for a snappy open.
                float prog = saturate(_Progress);
                float radius = prog * prog * 1.25;

                // Spiral: arms twist more toward the centre and spin over time.
                float swirl = ang * _Arms + r * _Swirl - _Time.y * _Speed;
                float arms = 0.5 + 0.5 * sin(swirl);

                // Brightness rises toward the centre and along the arms.
                float core = saturate(1.0 - r / max(radius, 1e-3));
                float energy = core * (0.35 + 0.65 * arms);

                // Glowing rim at the leading edge of the portal.
                float ring = smoothstep(0.06, 0.0, abs(r - radius)) * step(0.001, prog);

                fixed3 col = lerp(_Color.rgb, _RingColor.rgb, arms) * energy;
                col += _RingColor.rgb * ring * 1.5;

                // Inside the portal is opaque; soft feathered edge; ring adds glow.
                float inside = smoothstep(radius, radius - 0.05, r);
                float alpha = max(inside, ring);
                alpha = saturate(alpha) * i.color.a;

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
