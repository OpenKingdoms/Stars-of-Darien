// ScarMapGpu.cs - the scar map's textures, a byte a channel: marks (char,
// soil, blight, stone), shape (dip, rim, cracks) and fade (seconds left of
// frost, wet, holy light and heat), drawn a frame's stamps at a time.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed partial class ScarMap
    {
        static readonly int MarksId = Shader.PropertyToID("_OkuScarMarks"), ShapeId = Shader.PropertyToID("_OkuScarShape"),
            FadeId = Shader.PropertyToID("_OkuScarFade"), RectId = Shader.PropertyToID("_OkuScarRect"),
            TexelId = Shader.PropertyToID("_OkuScarTexel"), OnId = Shader.PropertyToID("_OkuScarOn"),
            SoilId = Shader.PropertyToID("_OkuScarSoil"), SnowId = Shader.PropertyToID("_OkuScarSnow"),
            StepId = Shader.PropertyToID("_Step"), TexelUnitsId = Shader.PropertyToID("_TexelUnits");

        RenderTexture marks, shape, fade;
        Material stampMat;
        Mesh stampMesh;
        readonly List<Vector3> sv = new List<Vector3>();
        readonly List<Vector4> s0 = new List<Vector4>(), s1 = new List<Vector4>(), s2 = new List<Vector4>(), s3 = new List<Vector4>(), s4 = new List<Vector4>();
        readonly List<int> st = new List<int>();

        public int TexW { get; private set; }
        public int TexH { get; private set; }
        // World units a texel spans.
        public float TexelUnits { get; private set; }
        public RenderTexture Marks => marks;
        public RenderTexture Shape => shape;
        public RenderTexture Fade => fade;
        // What the scar map holds, whatever lands on it.
        public long Bytes => (long)TexW * TexH * 4 * 3 + Dents.Bytes;

        void Size(EffectsQuality level)
        {
            float per = ScarStamps.PixelsPerUnit / Mathf.Max(1, quality.ScarTexelPixels);
            int w = Mathf.CeilToInt(size.x * per), h = Mathf.CeilToInt(size.y * per);
            float k = Mathf.Min(1f, MaxTexels(level) / (float)Mathf.Max(w, h));
            TexW = Mathf.Max(1, Mathf.RoundToInt(w * k));
            TexH = Mathf.Max(1, Mathf.RoundToInt(h * k));
            TexelUnits = size.x / TexW;
        }

        void MakeGpu()
        {
            marks = Target("scar marks");
            shape = Target("scar shape");
            fade = Target("scar fade");
            stampMat = new Material(Looks.Find("OkuScarStamp", "Hidden/Internal-Colored")) { hideFlags = HideFlags.DontSave };
            stampMesh = new Mesh { name = "scar stamps", hideFlags = HideFlags.DontSave };
            stampMesh.MarkDynamic();
        }

        RenderTexture Target(string name)
        {
            var rt = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false,
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            return rt;
        }

        void ReleaseGpu()
        {
            foreach (var rt in new[] { marks, shape, fade })
                if (rt != null) { rt.Release(); Looks.Release(rt); }
            marks = shape = fade = null;
            Looks.Release(stampMat);
            Looks.Release(stampMesh);
            stampMat = null;
            stampMesh = null;
        }

        // The textures at a new size, what they held carried over.
        void Resize()
        {
            var old = new[] { marks, shape, fade };
            marks = Target("scar marks");
            shape = Target("scar shape");
            fade = Target("scar fade");
            Graphics.Blit(old[0], marks);
            Graphics.Blit(old[1], shape);
            Graphics.Blit(old[2], fade);
            foreach (var rt in old) { rt.Release(); Looks.Release(rt); }
        }

        // One quad a stamp over its reach, in the map's texture space, with
        // what the stamp shader needs at every corner.
        void DrawStamps(List<ScarStamp> list)
        {
            if (stampMat == null) return;
            sv.Clear(); s0.Clear(); s1.Clear(); s2.Clear(); s3.Clear(); s4.Clear(); st.Clear();
            float margin = 2f * TexelUnits + 0.2f, since = now - steppedAt;
            foreach (var s in list)
            {
                float ext = s.Reach * 1.4f + margin;
                var a = new Vector4(s.Reach, s.Length, s.Dent, s.Floor);
                var b = new Vector4(s.Depth / ScarStamps.MaxDepthPx, s.Rim / ScarStamps.MaxRimPx, s.Crack, s.Stone);
                var c = new Vector4(s.Char, s.Soil, s.Blight, s.Outline);
                var d = new Vector4(Left(s.Frost, since), Left(s.Wet, since), Left(s.Holy, since), Left(s.Heat, since));
                int first = sv.Count;
                Corner(s, -ext, -(s.Length + ext), a, b, c, d);
                Corner(s, ext, -(s.Length + ext), a, b, c, d);
                Corner(s, ext, ext, a, b, c, d);
                Corner(s, -ext, ext, a, b, c, d);
                st.Add(first); st.Add(first + 1); st.Add(first + 2);
                st.Add(first); st.Add(first + 2); st.Add(first + 3);
            }
            stampMesh.Clear();
            stampMesh.SetVertices(sv);
            stampMesh.SetUVs(0, s0);
            stampMesh.SetUVs(1, s1);
            stampMesh.SetUVs(2, s2);
            stampMesh.SetUVs(3, s3);
            stampMesh.SetUVs(4, s4);
            stampMesh.SetTriangles(st, 0, false);
            stampMesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(100f, 100f, 100f));
            stampMat.SetFloat(TexelUnitsId, TexelUnits);
            Draw(marks, 0);
            Draw(shape, 1);
            Draw(fade, 2);
        }

        // Seconds left as the fade texture keeps them, counted from its last step.
        static float Left(float seconds, float since) => seconds > 0f ? Mathf.Clamp01((seconds + since) / FadeRange) : 0f;

        void Corner(in ScarStamp s, float lx, float ly, Vector4 a, Vector4 b, Vector4 c, Vector4 d)
        {
            // The stamp's frame: y along its direction, x to its right.
            float wx = s.X + s.DirZ * lx + s.DirX * ly, wz = s.Z - s.DirX * lx + s.DirZ * ly;
            sv.Add(new Vector3(wx / size.x, 1f + wz / size.y, 0f));
            s0.Add(new Vector4(lx, ly, s.Seed, (int)s.Kind));
            s1.Add(a);
            s2.Add(b);
            s3.Add(c);
            s4.Add(d);
        }

        void Draw(RenderTexture rt, int pass)
        {
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.PushMatrix();
            GL.LoadOrtho();
            stampMat.SetPass(pass);
            Graphics.DrawMeshNow(stampMesh, Matrix4x4.identity);
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        // Every half second of the battle the fading textures lose a step:
        // frost, wet, light and heat always, and on Low the lasting marks.
        void StepFading()
        {
            int steps = Mathf.FloorToInt((now - steppedAt) / StepSeconds);
            if (steps <= 0) return;
            steppedAt = steps > 255 ? now - (now - steppedAt) % StepSeconds : steppedAt + steps * StepSeconds;
            steps = Mathf.Min(steps, 255);
            StepDown(fade, steps);
            if (!quality.ScarsFade) return;
            StepDown(marks, steps);
            StepDown(shape, steps);
        }

        void StepDown(RenderTexture rt, int steps)
        {
            if (stampMat == null) return;
            stampMat.SetFloat(StepId, steps / 255f);
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.PushMatrix();
            GL.LoadOrtho();
            stampMat.SetPass(3);
            GL.Begin(GL.QUADS);
            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(1f, 0f, 0f);
            GL.Vertex3(1f, 1f, 0f);
            GL.Vertex3(0f, 1f, 0f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        void SetGlobals()
        {
            Shader.SetGlobalTexture(MarksId, marks);
            Shader.SetGlobalTexture(ShapeId, shape);
            Shader.SetGlobalTexture(FadeId, fade);
            Shader.SetGlobalVector(RectId, new Vector4(0f, 0f, size.x, size.y));
            Shader.SetGlobalVector(TexelId, new Vector4(1f / TexW, 1f / TexH, size.x / TexW, size.y / TexH));
            Shader.SetGlobalVector(OnId, new Vector4(1f, quality.Ground != ScarGround.Colour ? 1f : 0f, quality.Ground == ScarGround.Dips ? 1f : 0f, now - steppedAt));
            Shader.SetGlobalVector(SoilId, soil);
            Shader.SetGlobalVector(SnowId, snow);
        }

        static void ClearGlobals()
        {
            Shader.SetGlobalVector(OnId, Vector4.zero);
            Shader.SetGlobalTexture(MarksId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ShapeId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(FadeId, Texture2D.blackTexture);
        }

        // A texture's texels, row 0 on the map's south edge. Slow: for a
        // change of setting and for tests.
        public static Color32[] Read(RenderTexture rt)
        {
            if (rt == null) return null;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (!req.hasError) return req.GetData<Color32>().ToArray();
            }
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Looks.Release(tex);
            return px;
        }

        // The texel of a texture under a world point, from a copy of it.
        public Color32 TexelAt(Color32[] pixels, float x, float z)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(x / size.x * TexW), 0, TexW - 1);
            int j = Mathf.Clamp(Mathf.FloorToInt((1f + z / size.y) * TexH), 0, TexH - 1);
            return pixels[j * TexW + i];
        }
    }
}
