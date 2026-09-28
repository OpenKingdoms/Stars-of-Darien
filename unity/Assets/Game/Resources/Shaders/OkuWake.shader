// Ships' wakes, drawn from above into a small picture the sea reads
// (WaterWakes): r how much foam, g how churned the water is. Each texel
// keeps the most any wake gives it, so a ribbon folded on itself at a turn
// or two wakes crossing never stack. One untagged pass for either pipeline.
Shader "Hidden/OpenKingdoms/WakeSplat"
{
    SubShader
    {
        Pass
        {
            BlendOp Max
            Blend One One
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "../../Shaders/OkuWakeFoam.hlsl"
            float4 _OkuWakeRect;    // x, z of the picture's corner, 1 / width, 1 / depth
            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; float2 size : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float2 size : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                // Straight from the world's x and z to the picture.
                float2 uv = (v.vertex.xz - _OkuWakeRect.xy) * _OkuWakeRect.zw;
                float2 clip = uv * 2 - 1;
            #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
            #endif
                o.pos = float4(clip, 0.5, 1);
                o.uv = v.uv;
                o.size = v.size;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float churned;
                float foam = OkuWakeFoam(i.uv, i.size, churned);
                return float4(foam, churned, 0, 0);
            }
            ENDCG
        }
    }
}
