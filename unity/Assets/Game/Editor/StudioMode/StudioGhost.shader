// The original laid over a studio model: the build ghost's look, drawn
// after everything and over it, so the outline shows through the model.
Shader "OpenKingdoms/Studio/Ghost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.55, 0.85, 1, 1)
        _Mix ("Mix to tint", Range(0, 1)) = 0.35
        _Alpha ("Alpha", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            half _Mix, _Alpha;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; half light : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                half3 n = normalize(UnityObjectToWorldNormal(v.normal));
                o.light = 0.65 + 0.35 * saturate(dot(n, normalize(half3(-0.4, 0.8, -0.45))));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                clip(c.a - 0.4);
                fixed3 rgb = lerp(c.rgb, _Tint.rgb, _Mix) * i.light;
                return fixed4(rgb, _Alpha);
            }
            ENDCG
        }
    }
}
