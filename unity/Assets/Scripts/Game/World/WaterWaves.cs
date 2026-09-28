// WaterWaves.cs - the sea's swell on the CPU, the same sum of Gerstner
// waves OkuWaterCommon.hlsl draws, so ships bob on the waves the eye sees.
// WaterView sets the clock, wind and height each frame.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class WaterWaves
    {
        // The swell the mesh carries: wavelength, turn from the wind in
        // degrees and steepness. The shader's normals add shorter waves.
        public static readonly Vector3[] Swell =
        {
            new Vector3(8.7f, 0f, 0.045f),
            new Vector3(5.3f, 23f, 0.04f),
            new Vector3(3.1f, -31f, 0.035f),
            new Vector3(1.9f, 57f, 0.03f),
        };
        public const int MeshWaves = 2;

        public static float Time;
        public static Vector2 Wind = new Vector2(0.93f, 0.37f);
        public static float Amplitude = 1f;
        public static float SeaLevel = -1f;

        public static Vector2 Direction(int i)
        {
            float a = Mathf.Atan2(Wind.y, Wind.x) + Swell[i].y * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        // The surface's offset at a rest point, with the waves damped by
        // `damp` (0 flat, 1 full), from the waves the mesh carries.
        public static Vector3 Offset(float x, float z, float damp)
        {
            var d = Vector3.zero;
            for (int i = 0; i < MeshWaves; i++)
            {
                var w = Swell[i];
                float k = 2f * Mathf.PI / w.x;
                float a = w.z / k * Amplitude * damp;
                var dir = Direction(i);
                float c = Mathf.Sqrt(9.8f * k) * 0.35f;
                float f = k * (dir.x * x + dir.y * z) - c * Time;
                d.x += dir.x * a * Mathf.Cos(f);
                d.y += a * Mathf.Sin(f);
                d.z += dir.y * a * Mathf.Cos(f);
            }
            return d;
        }

        // The surface height above sea level near a point. One fixed step
        // undoes most of the horizontal drift, enough for bobbing.
        public static float Height(float x, float z, float damp)
        {
            var o = Offset(x, z, damp);
            return Offset(x - o.x, z - o.z, damp).y;
        }

        // How much the waves are damped over water this deep.
        public static float Damp(float depth) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(depth / 1.5f));
    }
}
