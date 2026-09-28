// Effects in one premultiplied blend: each vertex says whether its picture
// adds light, as the original adds most magic, or alpha blends. Added art
// keeps its own bytes and hot cores go past white. Soft where it meets the ground.
Shader "OpenKingdoms/Presentation/Effect"
{
    Properties
    {
        _MainTex ("Strip", 2D) = "white" {}
        _Soft ("Soft edge, world units", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            // Set by the effects: the scene's depth is there to fade against,
            // and how far past white the hottest added art goes.
            float _OkuFxSoft, _OkuFxHot;
            float _Soft;
            // fx.x 1 where the picture adds light, fx.y brightness (past 1
            // for a glowing core), fx.z the soft edge's scale.
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 fx : TEXCOORD1; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 fx : TEXCOORD1;
                float4 screen : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.fx = v.fx;
                o.screen = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.screen.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            // Added art is summed in the original's own gamma coding. Here it
            // is summed against a middling ground, which keeps its colour and
            // lets hot overlaps run past white into the bloom.
            #define OKU_GROUND 0.35
            float4 frag(v2f i) : SV_Target
            {
                float add = i.fx.x;
                float4 t = tex2D(_MainTex, i.uv);
                float3 c = t.rgb * i.color.rgb;
                float3 rgb;
                if (add > 0.5)
                {
                    float3 g = c * t.a;
                    rgb = pow(OKU_GROUND + g, 2.2) - pow(OKU_GROUND, 2.2);
                    rgb *= 1 + _OkuFxHot * smoothstep(0.55, 1.0, dot(g, float3(0.3, 0.59, 0.11)));
                }
                else rgb = GammaToLinearSpace(c) * t.a;
                float cov = t.a * i.color.a;
                rgb *= i.color.a * i.fx.y;
                if (_OkuFxSoft > 0.5)
                {
                    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                    float fade = saturate((scene - i.screen.z) / max(1e-3, _Soft * i.fx.z));
                    rgb *= fade;
                    cov *= fade;
                }
                float keep = cov * (1 - add);
                float4 o = float4(rgb, 1);
                // In fog added light fades away and alpha art toward the fog.
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, float4(unity_FogColor.rgb * keep, 1));
                return float4(o.rgb, keep);
            }
            ENDCG
        }
    }
}
