// Models, features and sprites: texture times vertex colour times an
// instanced tint, alpha tested, lit, casting and taking shadows, with a
// faint rim of sky light.
Shader "OpenKingdoms/Presentation/Model"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        _Rim ("Rim light", Range(0, 1)) = 0.3
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma multi_compile_instancing
        #pragma target 3.5
        sampler2D _MainTex;
        half _Glossiness;
        half _Rim;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 viewDir;
        };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * IN.color * UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            // A soft sky-coloured rim, so small models read against the ground.
            half rim = 1 - saturate(dot(normalize(IN.viewDir), o.Normal));
            o.Emission = unity_AmbientSky.rgb * c.rgb * pow(rim, 3) * _Rim * 2;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
