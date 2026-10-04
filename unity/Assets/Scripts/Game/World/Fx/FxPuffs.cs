// FxPuffs.cs - the pictures and shapes the particles are made of, drawn
// once in code: four billows of smoke, a glow, a streak, a flake and a drop
// side by side in one texture, and low rocks, clods, splinters and shards
// for debris, each faceted so the sun picks out its sides.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class FxPuffs
    {
        public const int Cell = 64, Columns = 4, Rows = 2;

        // Alpha is the puff's density. Red is how much light a part of the
        // billow catches, green where heat shows through.
        public static Texture2D Atlas()
        {
            int w = Cell * Columns, h = Cell * Rows;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "particles",
            };
            var px = new Color32[w * h];
            for (int cell = 0; cell < Columns * Rows; cell++)
            {
                int ox = (cell % Columns) * Cell, oy = (cell / Columns) * Cell;
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        // -1 to 1 across the cell, with a pixel of margin.
                        float u = (x + 0.5f) / Cell * 2.2f - 1.1f, v = (y + 0.5f) / Cell * 2.2f - 1.1f;
                        px[(oy + y) * w + ox + x] = Texel(cell, u, v);
                    }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Color32 Texel(int cell, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a, lit = 1f, heat = 1f;
            switch (cell)
            {
                case 4:
                {
                    float s = Mathf.Clamp01(1f - r);
                    a = s * s * (3f - 2f * s);
                    break;
                }
                case 5:
                {
                    float across = Mathf.Clamp01(1f - u * u);
                    float along = Mathf.Clamp01((1f - Mathf.Abs(v)) * 3f);
                    a = across * across * along;
                    break;
                }
                case 6:
                {
                    float ang = Mathf.Atan2(v, u);
                    float edge = 0.7f + 0.12f * Mathf.Sin(ang * 3f + 0.7f) + 0.06f * Mathf.Sin(ang * 7f);
                    float e = Mathf.Sqrt(u * u * 1.8f + v * v);
                    a = Mathf.Clamp01((edge - e) * 8f);
                    lit = 0.75f + 0.25f * Mathf.Clamp01(1f - Mathf.Abs(u) * 3f);
                    break;
                }
                case 7:
                {
                    float s = Mathf.Clamp01(1f - r * 1.15f);
                    a = Mathf.Sqrt(s);
                    lit = 0.8f + 0.2f * s;
                    break;
                }
                default:
                {
                    // A billow: a soft ball broken up by noise at a few scales.
                    float seed = cell * 17.31f;
                    float n = Fbm(u * 2.1f + seed, v * 2.1f - seed, 4);
                    float m = Fbm(u * 4.3f - seed, v * 4.3f + seed * 0.5f, 3);
                    float ball = Mathf.Clamp01(1f - r * (0.95f + 0.35f * (n - 0.5f)));
                    a = Mathf.Clamp01(ball * ball * (3f - 2f * ball) * (0.55f + 0.9f * n));
                    lit = Mathf.Clamp01(0.62f + 0.45f * m - 0.12f * v);
                    heat = Mathf.Clamp01(0.4f + 0.9f * (n - 0.35f));
                    break;
                }
            }
            return new Color32((byte)(lit * 255f), (byte)(heat * 255f), 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(x, y) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        // ── Shapes ────────────────────────────────────────────────────

        // A rough stone or clod: an icosahedron pulled about, about one unit across.
        public static Mesh Rock(int seed, float stretch, float flat)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var rng = new System.Random(seed);
            for (int i = 0; i < v.Count; i++)
            {
                float k = 0.75f + 0.45f * (float)rng.NextDouble();
                var p = v[i].normalized * 0.5f * k;
                v[i] = new Vector3(p.x * stretch, p.y * flat, p.z);
            }
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            return Faceted(v, f, "debris rock");
        }

        // A splinter of wood: a thin six-sided stick, one end broken to a point.
        public static Mesh Splinter(int seed)
        {
            var rng = new System.Random(seed);
            var v = new List<Vector3>();
            const int sides = 5;
            for (int end = 0; end < 2; end++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    float r = (end == 0 ? 0.13f : 0.05f) * (0.8f + 0.4f * (float)rng.NextDouble());
                    v.Add(new Vector3(end == 0 ? -0.6f : 0.55f + 0.2f * (float)rng.NextDouble(), Mathf.Cos(a) * r, Mathf.Sin(a) * r));
                }
            var f = new List<int>();
            for (int s = 0; s < sides; s++)
            {
                int a = s, b = (s + 1) % sides, c = sides + s, d = sides + (s + 1) % sides;
                f.AddRange(new[] { a, c, b, b, c, d });
            }
            for (int s = 1; s < sides - 1; s++) { f.AddRange(new[] { 0, s + 1, s }); f.AddRange(new[] { sides, sides + s, sides + s + 1 }); }
            return Faceted(v, f.ToArray(), "debris splinter");
        }

        // A shard of ice: a long four-sided spike, pointed at both ends.
        public static Mesh Shard(int seed)
        {
            var rng = new System.Random(seed);
            var v = new List<Vector3> { new Vector3(0f, 0.75f + 0.2f * (float)rng.NextDouble(), 0f), new Vector3(0f, -0.45f, 0f) };
            for (int s = 0; s < 4; s++)
            {
                float a = s * Mathf.PI * 0.5f + 0.3f * (float)rng.NextDouble();
                float r = 0.16f + 0.08f * (float)rng.NextDouble();
                v.Add(new Vector3(Mathf.Cos(a) * r, 0.05f * (float)rng.NextDouble(), Mathf.Sin(a) * r));
            }
            var f = new List<int>();
            for (int s = 0; s < 4; s++)
            {
                int a = 2 + s, b = 2 + (s + 1) % 4;
                f.AddRange(new[] { 0, b, a, 1, a, b });
            }
            return Faceted(v, f.ToArray(), "debris shard");
        }

        // Each face its own corners, so the shape lights flat side by side.
        static Mesh Faceted(List<Vector3> v, int[] faces, string name)
        {
            var verts = new Vector3[faces.Length];
            var normals = new Vector3[faces.Length];
            var uv = new Vector2[faces.Length];
            var tris = new int[faces.Length];
            for (int i = 0; i < faces.Length; i += 3)
            {
                Vector3 a = v[faces[i]], b = v[faces[i + 1]], c = v[faces[i + 2]];
                var n = Vector3.Cross(b - a, c - a).normalized;
                // Faces point outward, whichever way the list wound them.
                if (Vector3.Dot(n, (a + b + c) / 3f) < 0f) { (b, c) = (c, b); n = -n; }
                verts[i] = a; verts[i + 1] = b; verts[i + 2] = c;
                normals[i] = normals[i + 1] = normals[i + 2] = n;
                uv[i] = uv[i + 1] = uv[i + 2] = new Vector2(0.5f, 0.5f);
                tris[i] = i; tris[i + 1] = i + 1; tris[i + 2] = i + 2;
            }
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = verts, normals = normals, uv = uv, triangles = tris };
            m.RecalculateBounds();
            return m;
        }
    }
}
