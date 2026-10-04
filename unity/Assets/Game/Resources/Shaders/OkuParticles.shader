// The battle's shared particles in one premultiplied blend, read from a
// buffer the effects fill each frame far to near. Puffs of smoke and dust
// are lit as soft balls by the sun and the sky and glow with their heat,
// cooling from added light to smoke. Glows and streaks add light past white
// into the bloom. Flakes and drops are small lit bits. Soft where they meet
// the ground, and faded by the fog.
Shader "OpenKingdoms/Presentation/Particles"
{
    Properties
    {
        _MainTex ("Puffs", 2D) = "white" {}
        _Soft ("Soft edge, share of the size", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct Particle
            {
                float3 pos;
                float size;
                float3 axis;
                float angle;
                uint colour;
                float heat;
                float alpha;
                uint look;
            };
            StructuredBuffer<Particle> _Particles;
            sampler2D _MainTex;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _OkuFxDepth, _OkuFxSoft, _Soft;
            // The way toward the sun, its light and the sky's, as the effects set them.
            float4 _OkuFxSunWay;
            float4 _OkuFxSun;
            float4 _OkuFxAmbient;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;        // in the atlas
                float2 local : TEXCOORD1;     // -1 to 1 across the ball, as the screen sees it
                float4 fx : TEXCOORD2;        // heat, look, tinted, size
                float4 screen : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                float3 light : TEXCOORD5;     // the sun's and the sky's light on it
            };

            static const float2 Corner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            float3 Unpack(uint c, out float a)
            {
                a = ((c >> 24) & 255) / 255.0;
                return float3(c & 255, (c >> 8) & 255, (c >> 16) & 255) / 255.0;
            }

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                Particle p = _Particles[vid / 6];
                float2 c = Corner[vid % 6];
                uint look = p.look & 7;
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 world;
                float2 local;
                if (look == 5)
                {
                    // A streak stands along its axis, turned to face the eye.
                    float len = length(p.axis);
                    float3 along = len > 1e-4 ? p.axis / len : up;
                    float3 toEye = normalize(_WorldSpaceCameraPos - p.pos);
                    float3 side = cross(along, toEye);
                    side = dot(side, side) > 1e-8 ? normalize(side) : right;
                    float reach = max(len * 0.5, p.size * 0.5) * 1.15;
                    world = p.pos + side * (c.x * p.size * 0.5) + along * (c.y * reach);
                    local = c;
                }
                else
                {
                    float s = sin(p.angle), k = cos(p.angle);
                    float2 r = float2(k * c.x - s * c.y, s * c.x + k * c.y);
                    world = p.pos + (right * r.x + up * r.y) * (p.size * 0.5);
                    local = r;
                }
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                float cell = look;
                float2 uv = c * (0.5 / 1.1) + 0.5;
                o.uv = float2((fmod(cell, 4) + uv.x) / 4.0, (floor(cell / 4) + uv.y) / 2.0);
                o.local = local;
                float a;
                float3 rgb = Unpack(p.colour, a);
                o.colour = float4(GammaToLinearSpace(rgb), a * p.alpha);
                o.fx = float4(p.heat, look, (p.look & 8) != 0 ? 1 : 0, p.size);
                // Lit at the corners as a ball facing the eye would be, and eased
                // across, which costs the pixels nothing.
                float3 toEye = normalize(UNITY_MATRIX_V[2].xyz);
                float3 n = look < 4 ? normalize(right * local.x * 0.7 + up * local.y * 0.7 + toEye * 0.55) : toEye;
                float3 sunWay = normalize(_OkuFxSunWay.xyz + float3(0, 1e-4, 0));
                float wrap = saturate(dot(n, sunWay) * 0.5 + 0.5);
                o.light = _OkuFxAmbient.rgb * (0.75 + 0.25 * n.y) + _OkuFxSun.rgb * wrap;
                o.screen = ComputeScreenPos(o.pos);
                o.screen.z = -mul(UNITY_MATRIX_V, float4(world, 1)).z;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Fire from deep red through orange to a white-hot core, past 1.
            float3 FireRamp(float h)
            {
                float3 c = lerp(float3(0.45, 0.05, 0.01), float3(1.0, 0.33, 0.05), saturate(h * 2.0));
                c = lerp(c, float3(1.0, 0.82, 0.5), saturate(h * 2.0 - 1.0));
                return c * (0.5 + 5.5 * h * h);
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                float heat = i.fx.x;
                uint look = (uint)(i.fx.y + 0.5);
                bool tinted = i.fx.z > 0.5;
                float fade = 1;
                if (_OkuFxDepth > 0.5 && _OkuFxSoft > 0.5)
                {
                    float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                    fade = saturate((scene - i.screen.z) / max(0.05, _Soft * i.fx.w));
                }
                float cov = t.a * i.colour.a * fade;
                // The clear corners of each quad cost blending and show nothing.
                if (cov < 0.004) discard;
                float3 rgb;
                float keep;
                if (look == 4 || look == 5)
                {
                    // Added light: its colour, hotter past white.
                    rgb = i.colour.rgb * (1.0 + 3.0 * heat) * cov;
                    keep = 0;
                }
                else
                {
                    // The billow's own light and shade on the light from the corners.
                    float shade = look < 4 ? 0.6 + 0.4 * t.r : t.r;
                    float3 lit = i.colour.rgb * i.light * shade;
                    float h = look < 4 ? saturate(heat * (0.55 + 0.9 * t.g)) : 0;
                    float3 glow = tinted ? i.colour.rgb * (0.8 + 4.0 * h) : FireRamp(h);
                    rgb = (lit * (1.0 - h) + glow * h) * cov;
                    // Hot smoke adds light, and holds back what is behind it as it cools.
                    keep = cov * (1.0 - 0.85 * h);
                }
                float4 o = float4(rgb, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, float4(unity_FogColor.rgb * keep, 1));
                return float4(o.rgb, keep);
            }
            ENDCG
        }
    }
}
