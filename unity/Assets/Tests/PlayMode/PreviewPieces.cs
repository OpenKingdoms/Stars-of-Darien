// PreviewPieces.cs - a placement preview stands at rest, while a finished
// building's script may turn some of its pieces, as the wind turns a gate's
// flags. Comparing the two leaves out the pieces turned away from the body.
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public static class PreviewPieces
    {
        // The names of a unit's pieces its script has turned more than a degree from its root piece.
        public static HashSet<string> Turned(IGameBackend b, int handle)
        {
            var names = new HashSet<string>();
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            for (int i = 0; i < n; i++)
            {
                if (units[i].Handle != handle) continue;
                var model = b.GetModel(units[i].Model);
                var poses = new PiecePose[256];
                int count = Mathf.Min(b.ReadUnitPose(handle, poses), model?.Pieces.Length ?? 0);
                for (int p = 1; p < count; p++)
                    if (Quaternion.Angle(poses[p].Matrix.rotation, poses[0].Matrix.rotation) > 1f) names.Add(model.Pieces[p].Name);
                break;
            }
            return names;
        }
    }
}
