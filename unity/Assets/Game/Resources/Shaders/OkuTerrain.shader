// The ground: its picture, lit, taking shadows, with a faint detail
// noise up close and a wet darkening near the water line.
Shader "OpenKingdoms/Presentation/Terrain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SeaLevel ("Sea level", Float) = -100
        _Glossiness ("Smoothness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.5
        sampler2D _MainTex;
        float _SeaLevel;
        half _Glossiness;
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            float wet = saturate(1 - (IN.worldPos.y - _SeaLevel) / 0.6);
            c.rgb *= lerp(1, 0.72, wet);
            o.Albedo = c.rgb;
            o.Smoothness = lerp(_Glossiness, 0.6, wet);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
