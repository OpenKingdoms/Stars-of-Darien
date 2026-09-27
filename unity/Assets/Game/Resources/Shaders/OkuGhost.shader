// A building being placed, as the original shows it: the model's colours
// mixed half way to green or red, at 140 of 255 alpha. One untagged pass,
// so it draws in the built-in pipeline and in URP alike.
Shader "OpenKingdoms/Presentation/Ghost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.235, 0.863, 0.353, 1)
        _Mix ("Mix to tint", Range(0, 1)) = 0.5
        _Alpha ("Alpha", Range(0, 1)) = 0.549
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            half _Mix, _Alpha;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; half light : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                // A fixed light from the upper south west, so the shape reads.
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
