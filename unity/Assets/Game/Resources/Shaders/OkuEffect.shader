// Effects in one premultiplied blend: each vertex says whether its picture
// adds light, as the original adds most magic, or alpha blends. Added art is
// summed with the ground as the screen shows it, as the original sums bytes,
// and hot cores go past white. Art drawn larger than itself is sampled
// bicubic. Soft where it meets the ground.
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
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _CameraOpaqueTexture;
            // Set by the effects: the scene's depth is there to fade against,
            // how far past white the hottest added art goes, the picture
            // under the effects is there to sum with, and the post chain's
            // exposure, white balance, contrast and saturation (x, LMS, y, z).
            float _OkuFxSoft, _OkuFxDepth, _OkuFxHot, _OkuFxOpaque, _OkuFxPost;
            // The sea's surface, far below the map when there is none.
            float _OkuSeaLevel;
            float4 _OkuFxGrade;
            float4 _OkuFxBalance;
            float _Soft;
            // fx.x 1 where the picture adds light, fx.y brightness (past 1
            // for a glowing core), fx.z the soft edge's scale, fx.w 1 to
            // sample smoothly.
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 fx : TEXCOORD1; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 fx : TEXCOORD1;
                float4 screen : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                float3 world : TEXCOORD4;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.fx = v.fx;
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                COMPUTE_EYEDEPTH(o.screen.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Catmull-Rom through nine bilinear taps of the top mip.
            float4 Bicubic(float2 uv)
            {
                float2 p = uv * _MainTex_TexelSize.zw;
                float2 p1 = floor(p - 0.5) + 0.5;
                float2 f = p - p1;
                float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
                float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
                float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
                float2 w3 = f * f * (-0.5 + 0.5 * f);
                float2 w12 = w1 + w2;
                float2 t0 = (p1 - 1) * _MainTex_TexelSize.xy;
                float2 t3 = (p1 + 2) * _MainTex_TexelSize.xy;
                float2 t12 = (p1 + w2 / w12) * _MainTex_TexelSize.xy;
                float4 r = 0;
                r += tex2Dlod(_MainTex, float4(t0.x, t0.y, 0, 0)) * w0.x * w0.y;
                r += tex2Dlod(_MainTex, float4(t12.x, t0.y, 0, 0)) * w12.x * w0.y;
                r += tex2Dlod(_MainTex, float4(t3.x, t0.y, 0, 0)) * w3.x * w0.y;
                r += tex2Dlod(_MainTex, float4(t0.x, t12.y, 0, 0)) * w0.x * w12.y;
                r += tex2Dlod(_MainTex, float4(t12.x, t12.y, 0, 0)) * w12.x * w12.y;
                r += tex2Dlod(_MainTex, float4(t3.x, t12.y, 0, 0)) * w3.x * w12.y;
                r += tex2Dlod(_MainTex, float4(t0.x, t3.y, 0, 0)) * w0.x * w3.y;
                r += tex2Dlod(_MainTex, float4(t12.x, t3.y, 0, 0)) * w12.x * w3.y;
                r += tex2Dlod(_MainTex, float4(t3.x, t3.y, 0, 0)) * w3.x * w3.y;
                return saturate(r);
            }

            // URP's Neutral curve, its inverse, and the LDR grade around it.
            float3 NeutralCurve(float3 z)
            {
                return ((z * (0.2 * z + 0.24 * 0.29) + 0.272 * 0.02) / (z * (0.2 * z + 0.29) + 0.272 * 0.3)) - 0.02 / 0.3;
            }
            static const float NeutralWhite = 1.0 / 0.761384;
            float3 Neutral(float3 x) { return NeutralCurve(x * NeutralWhite) * NeutralWhite; }
            float3 NeutralInverse(float3 y)
            {
                float3 u = y / NeutralWhite + 0.02 / 0.3;
                float3 A = 0.2 * (u - 1);
                float3 B = 0.29 * (u - 0.24);
                float3 C = 0.272 * (0.3 * u - 0.02);
                float3 z = (-B - sqrt(max(B * B - 4 * A * C, 0))) / min(2 * A, -1e-5);
                return max(z, 0) / NeutralWhite;
            }
            float3 LogC(float3 x) { return 0.244161 * log10(max(5.555556 * x + 0.047996, 1e-6)) + 0.386036; }
            float3 LogCInverse(float3 x) { return (pow(10.0, (x - 0.386036) / 0.244161) - 0.047996) / 5.555556; }
            float3 ToLms(float3 x) { return mul(float3x3(3.90405e-1, 5.49941e-1, 8.92632e-3, 7.08416e-2, 9.63172e-1, 1.35775e-3, 2.31082e-2, 1.28021e-1, 9.36245e-1), x); }
            float3 FromLms(float3 x) { return mul(float3x3(2.85847, -1.62879, -2.48910e-2, -2.10182e-1, 1.15820, 3.24281e-4, -4.18120e-2, -1.18169e-1, 1.06867), x); }
            static const float3 Luma = float3(0.2126729, 0.7151522, 0.0721750);
            #define MIDGREY 0.4135884

            // Scene light to what the screen shows, linear 0 to 1, and back.
            float3 Show(float3 x)
            {
                if (_OkuFxPost < 0.5) return saturate(x);
                x = saturate(Neutral(x * _OkuFxGrade.x));
                x = FromLms(ToLms(x) * _OkuFxBalance.xyz);
                x = LogCInverse((LogC(x) - MIDGREY) * _OkuFxGrade.y + MIDGREY);
                x = max(x, 0);
                float l = dot(x, Luma);
                return saturate(l + _OkuFxGrade.z * (x - l));
            }
            float3 Unshow(float3 y)
            {
                if (_OkuFxPost < 0.5) return y;
                float l = dot(y, Luma);
                y = max(l + (y - l) / _OkuFxGrade.z, 0);
                y = LogCInverse((LogC(y) - MIDGREY) / _OkuFxGrade.y + MIDGREY);
                y = max(FromLms(ToLms(max(y, 0)) / _OkuFxBalance.xyz), 0);
                return NeutralInverse(min(y, 0.985)) / _OkuFxGrade.x;
            }

            // Without the picture below, or over water it cannot see, the sum
            // is taken against a middling ground.
            #define OKU_GROUND 0.35
            float4 frag(v2f i) : SV_Target
            {
                float add = i.fx.x;
                float2 duv = float2(length(ddx(i.uv * _MainTex_TexelSize.zw)), length(ddy(i.uv * _MainTex_TexelSize.zw)));
                float2 gx = ddx(i.uv), gy = ddy(i.uv);
                float4 t;
                if (i.fx.w > 0.5 && max(duv.x, duv.y) < 1.0) t = Bicubic(i.uv);
                else t = tex2Dgrad(_MainTex, i.uv, gx, gy);
                float cov = t.a * i.color.a;
                float fade = 1, sunk = 0;
                if (_OkuFxDepth > 0.5)
                {
                    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                    if (_OkuFxSoft > 0.5) fade = saturate((scene - i.screen.z) / max(1e-3, _Soft * i.fx.z));
                    // How far under the sea the ground behind lies: the copy
                    // of the picture holds the bed, not the water over it.
                    float3 behind = _WorldSpaceCameraPos + (i.world - _WorldSpaceCameraPos) * (scene / max(i.screen.z, 1e-3));
                    sunk = saturate((_OkuSeaLevel - behind.y) / 1.5);
                }
                float3 c = t.rgb * i.color.rgb;
                float3 rgb;
                if (add > 0.5)
                {
                    // The art's bytes added to the screen's, as the original adds
                    // them. A frame's weight scales the whole sum, so a frame
                    // eased into the next adds up to the same as either.
                    float3 g = c * t.a * min(i.fx.y, 1);
                    float hot = max(i.fx.y, 1) * (1 + _OkuFxHot * smoothstep(0.55, 1.0, dot(g, float3(0.3, 0.59, 0.11))));
                    float3 middling = pow(OKU_GROUND + g, 2.2) - pow(OKU_GROUND, 2.2);
                    rgb = middling;
                    if (_OkuFxOpaque > 0.5 && sunk < 1)
                    {
                        float3 under = tex2D(_CameraOpaqueTexture, i.screen.xy / i.screen.w).rgb;
                        float3 shown = LinearToGammaSpace(Show(under));
                        float3 sum = GammaToLinearSpace(min(shown + g, 0.985));
                        rgb = lerp(max(Unshow(sum) - under, 0), middling, sunk);
                    }
                    rgb *= hot * i.color.a * fade;
                    cov = 0;
                }
                else
                {
                    rgb = GammaToLinearSpace(c) * t.a * i.color.a * i.fx.y * fade;
                    cov *= fade;
                }
                float keep = cov;
                float4 o = float4(rgb, 1);
                // In fog added light fades away and alpha art toward the fog.
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, float4(unity_FogColor.rgb * keep, 1));
                return float4(o.rgb, keep);
            }
            ENDCG
        }
    }
}
