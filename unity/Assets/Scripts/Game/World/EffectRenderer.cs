// EffectRenderer.cs - the engine's effects and picture projectiles, each
// frame a quad standing on its point and turned to the camera like the
// sprite features, gathered into one mesh per strip picture and redrawn
// every frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class EffectRenderer
    {
        public const int MaxEffects = 2048;

        readonly IGameBackend backend;
        readonly EffectState[] effects = new EffectState[MaxEffects];
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>();
        readonly List<Object> owned = new List<Object>();
        public int Count { get; private set; }

        sealed class Batch
        {
            public Mesh Mesh;
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int> T = new List<int>();
        }

        public EffectRenderer(IGameBackend backend) => this.backend = backend;

        public void Render(Camera cam)
        {
            foreach (var b in batches.Values) { b.V.Clear(); b.U.Clear(); b.C.Clear(); b.T.Clear(); }
            Count = Mathf.Min(backend.ReadEffects(effects), effects.Length);
            var fwd = cam.transform.forward;
            fwd.y = 0;
            var face = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
            var right = face * Vector3.right;
            for (int i = 0; i < Count; i++)
            {
                var e = effects[i];
                if (e.Width <= 0 || e.Top <= e.Bottom) continue;
                if (!batches.TryGetValue(e.Strip, out var b)) batches[e.Strip] = b = new Batch();
                var left = e.Position - right * e.OffsetX;
                var r = right * e.Width;
                var lo = Vector3.up * e.Bottom;
                var hi = Vector3.up * e.Top;
                int v = b.V.Count;
                b.V.Add(left + lo); b.V.Add(left + r + lo); b.V.Add(left + r + hi); b.V.Add(left + hi);
                // The strip is uploaded the right way up, so row 0 is v = 1.
                float u0 = e.UvMin.x, u1 = e.UvMax.x, vTop = 1f - e.UvMin.y, vBottom = 1f - e.UvMax.y;
                b.U.Add(new Vector2(u0, vBottom)); b.U.Add(new Vector2(u1, vBottom)); b.U.Add(new Vector2(u1, vTop)); b.U.Add(new Vector2(u0, vTop));
                for (int k = 0; k < 4; k++) b.C.Add(new Color32(255, 255, 255, 255));
                b.T.Add(v); b.T.Add(v + 2); b.T.Add(v + 1);
                b.T.Add(v); b.T.Add(v + 3); b.T.Add(v + 2);
            }
            foreach (var kv in batches)
            {
                var b = kv.Value;
                if (b.V.Count == 0) continue;
                var mat = MaterialFor(kv.Key);
                if (mat == null) continue;
                if (b.Mesh == null) { b.Mesh = new Mesh { name = "effects " + kv.Key, hideFlags = HideFlags.DontSave }; b.Mesh.MarkDynamic(); owned.Add(b.Mesh); }
                b.Mesh.Clear();
                b.Mesh.indexFormat = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                b.Mesh.SetVertices(b.V);
                b.Mesh.SetUVs(0, b.U);
                b.Mesh.SetColors(b.C);
                b.Mesh.SetTriangles(b.T, 0);
                b.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                Graphics.RenderMesh(new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, b.Mesh, 0, Matrix4x4.identity);
            }
        }

        Material MaterialFor(int strip)
        {
            if (materials.TryGetValue(strip, out var m)) return m;
            var tex = UI.UiKit.ToTexture(backend.EffectStrip(strip), false);
            if (tex != null) { tex.filterMode = FilterMode.Bilinear; owned.Add(tex); }
            m = tex != null ? new Material(Looks.Find("OkuEffect", "Sprites/Default")) { mainTexture = tex, hideFlags = HideFlags.DontSave } : null;
            if (m != null) owned.Add(m);
            materials[strip] = m;
            return m;
        }

        public void Dispose()
        {
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            materials.Clear();
            batches.Clear();
        }
    }
}
