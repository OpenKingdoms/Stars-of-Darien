// Ground marks from the effects, scorch and shot shadows: the ground under
// them darkened toward the vertex colour by the mark's alpha.
Shader "OpenKingdoms/Presentation/Effect Decal"
{
    Properties
    {
        _MainTex ("Marks", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "IgnoreProjector" = "True" }
        Blend DstColor Zero
        ZWrite Off
        Cull Off
        Offset -1, -2
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * i.color.a;
                float4 c = float4(lerp(float3(1, 1, 1), i.color.rgb, a), 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, float4(1, 1, 1, 1));
                return c;
            }
            ENDCG
        }
    }
}
