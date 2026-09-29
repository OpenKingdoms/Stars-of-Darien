// SelectionRing.cs - the ring round a selected unit: a true circle on the
// ground, laid over its bumps so no part of it sinks from sight, sized
// from a building's footprint or a unit's model.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class SelectionRing
    {
        public const int Segments = 48;
        public const float Inner = 0.85f;   // of the radius
        public const float Lift = 0.06f;    // above the ground under each point

        // A building's ring clears its footprint's longer side, a unit's
        // its model, the same way round.
        public static float Radius(UnitDef def, float modelRadius) =>
            def != null && def.IsBuilding
                ? Mathf.Max(def.Footprint.x, def.Footprint.y) * 0.5f + 0.3f
                : Mathf.Max(0.4f, modelRadius);

        // Adds a ring round centre. With ground, each point sits Lift over
        // the ground under it and never below floor; without, the ring is
        // flat at floor, as on water or under a flyer.
        public static void Add(Vector3 centre, float radius, Func<float, float, float> ground, float floor, List<Vector3> verts, List<int> tris)
        {
            int b0 = verts.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts.Add(Point(centre + d * (radius * Inner), ground, floor));
                verts.Add(Point(centre + d * radius, ground, floor));
                if (i == Segments) break;
                int b = b0 + i * 2;
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
        }

        static Vector3 Point(Vector3 p, Func<float, float, float> ground, float floor)
        {
            p.y = ground != null ? Mathf.Max(ground(p.x, p.z) + Lift, floor) : floor;
            return p;
        }
    }
}
