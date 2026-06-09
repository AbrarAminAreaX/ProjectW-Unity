Shader "Skybox/DayNightBlend"
{
    Properties
    {
        _DayTex   ("Day Cubemap",   Cube) = "white" {}
        _NightTex ("Night Cubemap", Cube) = "black" {}
        _Blend    ("Blend (0=Day, 1=Night)", Range(0,1)) = 0
        _Exposure ("Exposure",      Range(0, 4)) = 1
        _Rotation ("Rotation",      Range(0,360)) = 0
        _DayTint   ("Day Tint",   Color) = (1,1,1,1)
        _NightTint ("Night Tint", Color) = (0.7, 0.75, 1.0, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _DayTex;
            samplerCUBE _NightTex;
            float       _Blend;
            float       _Exposure;
            float       _Rotation;
            fixed4      _DayTint;
            fixed4      _NightTint;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float3 RotateAroundY(float3 v, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float a = _Rotation * UNITY_PI / 180.0;
                o.dir = RotateAroundY(v.vertex.xyz, a);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 day   = texCUBE(_DayTex,   i.dir) * _DayTint;
                fixed4 night = texCUBE(_NightTex, i.dir) * _NightTint;

                fixed4 col = lerp(day, night, _Blend);
                col.rgb *= _Exposure;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
