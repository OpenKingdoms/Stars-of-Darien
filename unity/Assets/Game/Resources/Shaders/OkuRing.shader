// The land past the map's edge: the map's own picture mirrored, lit by the
// sun, darkening and losing its colour into the climate's haze with
// distance from the edge. One untagged pass, so it draws in the built-in
// pipeline and in URP alike, and it casts no shadow.
Shader "OpenKingdoms/Presentation/EdgeRing"
{
    Properties
    {
        _MainTex ("Map picture", 2D) = "white" {}
        _Width ("Ring width, cells", Float) = 32
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "../../Shaders/OkuFog.hlsl"
            sampler2D _MainTex;
            float4 _OkuHaze;    // the climate's haze, set with the fog
            float _Width;
            float4 _OkuSunDir, _OkuSunColor, _OkuAmbient;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 dist : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; half3 normal : TEXCOORD2; float3 world : TEXCOORD4; UNITY_FOG_COORDS(3) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.dist = v.dist.x;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                half ndl = saturate(dot(normalize(i.normal), normalize(_OkuSunDir.xyz)));
                c *= (_OkuAmbient.rgb + _OkuSunColor.rgb * ndl) * 0.8;
                // The fog of war at the nearest edge, so the ring is never
                // brighter than the map beside it.
                float3 edge = float3(clamp(i.world.x, 0, _OkuMapSize.x), i.world.y, clamp(i.world.z, -_OkuMapSize.y, 0));
                c *= OkuFogLight(edge) * 0.8;
                float f = saturate(i.dist / _Width);
                fixed grey = dot(c, fixed3(0.3, 0.59, 0.11));
                c = lerp(c, grey.xxx, f * 0.7);
                c = lerp(c, _OkuHaze.rgb, smoothstep(0, 1, f));
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
