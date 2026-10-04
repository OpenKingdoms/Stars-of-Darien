// ScarMap.cs - the battlefield's scars: craters dented into the drawn
// ground with their thrown rims, scorch, churned earth, stone, frost,
// blight, wet and fading light, kept for the whole battle.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed partial class ScarMap
    {
        // The scar map of the battle being drawn, null when there is none.
        public static ScarMap Current { get; private set; }

        // How far the drawn ground lies from the engine's height at a world
        // point, in world units: below 0 in a crater, above 0 on its thrown
        // rim, 0 where nothing dented it or the quality draws no dips.
        public static float GroundOffset(float x, float z)
        {
            var m = Current;
            return m != null ? m.Offset(x, z) : 0f;
        }

        // How far to move something standing at a world point, a unit, a
        // feature, a corpse or rubble, so it sits on the dented ground.
        // Anything well above the engine's ground, a flyer or a shot, stays.
        public static float SinkY(Vector3 at)
        {
            var m = Current;
            return m != null ? m.Offset(at.x, at.z) * m.Standing(at) : 0f;
        }

        // SinkY as a move, to multiply onto a drawn matrix from the left.
        public static Matrix4x4 Sink(Vector3 at)
        {
            float y = SinkY(at);
            return y != 0f ? Matrix4x4.Translate(new Vector3(0f, y, 0f)) : Matrix4x4.identity;
        }

        ScarMap() { }

        float Offset(float x, float z) => 0f;

        float Standing(Vector3 at) => 1f;
    }
}
