// The land past the map's edge: the map's own picture mirrored, lit by the
// sun, darkening and losing its colour into the climate's haze with
// distance from the edge. One untagged pass, so it draws in the built-in
// pipeline and in URP alike, and it casts no shadow.
Shader "OpenKingdoms/Presentation/EdgeRing"
{
    Properties
    {
        _MainTex ("Map picture", 2D) = "white" {}
        _Haze ("Haze", Color) = (0.32, 0.34, 0.36, 1)
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
            sampler2D _MainTex;
            fixed4 _Haze;
            float _Width;
            float4 _OkuSunDir, _OkuSunColor, _OkuAmbient;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 dist : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; half3 normal : TEXCOORD2; UNITY_FOG_COORDS(3) };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.dist = v.dist.x;
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                half ndl = saturate(dot(normalize(i.normal), normalize(_OkuSunDir.xyz)));
                c *= _OkuAmbient.rgb + _OkuSunColor.rgb * ndl;
                float f = saturate(i.dist / _Width);
                fixed grey = dot(c, fixed3(0.3, 0.59, 0.11));
                c = lerp(c, grey.xxx, f * 0.7);
                c = lerp(c, _Haze.rgb, smoothstep(0, 1, f) * 0.85);
                fixed4 col = fixed4(c, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
