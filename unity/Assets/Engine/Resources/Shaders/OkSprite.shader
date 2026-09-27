// Sprite features: a quad that turns about the vertical to face the
// camera, as OpenKingdoms' 3D view draws them. The vertex carries the
// anchor, and uv2 the corner's offset across and up, in world units.
// A flat sprite (uv2 all zero) lies where its vertices are.
Shader "OpenKingdoms/Sprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.4
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "DisableBatching" = "True" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed _Cutoff;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 corner : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o;
                float3 anchor = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 right = float3(UNITY_MATRIX_V[0].x, 0, UNITY_MATRIX_V[0].z);
                float len = length(right);
                right = len > 1e-4 ? right / len : float3(1, 0, 0);
                float3 world = anchor + right * v.corner.x + float3(0, v.corner.y, 0);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Tint;
                clip(c.a - _Cutoff);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
