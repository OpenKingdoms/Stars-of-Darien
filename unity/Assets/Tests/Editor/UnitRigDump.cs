// UnitRigDump.cs - writes a unit's original pieces and its script's poses
// to a JSON file outside the repository, for a Blender kit that builds a
// replacement model to preview the animations on. Runs only with
// OKU_RIG_DUMP set to the output folder, and OKU_RIG_UNIT naming the
// unit's object name (ARAKING by default). Needs okengine and the game files.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class UnitRigDump
    {
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static void Matrix(StringBuilder sb, Matrix4x4 m)
        {
            sb.Append('[');
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                {
                    if (r + c > 0) sb.Append(',');
                    sb.Append(F(m[r, c]));
                }
            sb.Append(']');
        }

        [Test]
        public void DumpTheRig()
        {
            string dir = Environment.GetEnvironmentVariable("OKU_RIG_DUMP");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_RIG_DUMP to a folder to write the rig to");
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            string unit = Environment.GetEnvironmentVariable("OKU_RIG_UNIT");
            if (string.IsNullOrEmpty(unit)) unit = "ARAKING";
            using var backend = new EngineBackend();
            var s = new SkirmishSetup { MapId = "two castles", Seed = 7, LineOfSight = false, MapRevealed = true };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            backend.StartSkirmish(s);
            for (int i = 0; i < 4000 && !backend.PumpLoading().Done; i++) { }
            UnitDef def = null;
            foreach (var d in backend.UnitDefs)
                if (string.Equals(d.ObjectName, unit, StringComparison.OrdinalIgnoreCase)) { def = d; break; }
            Assert.IsNotNull(def, unit);
            int id = backend.LoadModel(def.ObjectName, 0);
            var md = backend.GetModel(id);
            Assert.IsNotNull(md);
            var sb = new StringBuilder();
            sb.Append("{\"unit\":\"").Append(def.ObjectName).Append("\",\"scale\":").Append(F(md.Scale));
            sb.Append(",\"tps\":").Append(backend.TicksPerSecond);
            sb.Append(",\"pieces\":[");
            for (int p = 0; p < md.Pieces.Length; p++)
            {
                var pc = md.Pieces[p];
                if (p > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(pc.Name).Append("\",\"parent\":").Append(pc.Parent)
                  .Append(",\"offset\":[").Append(F(pc.Offset.x)).Append(',').Append(F(pc.Offset.y)).Append(',').Append(F(pc.Offset.z)).Append("]}");
            }
            sb.Append("],\"animations\":{");
            var poses = new PiecePose[256];
            bool first = true;
            var anims = new System.Collections.Generic.List<string>(def.Animations) { "" };
            foreach (var anim in anims)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(anim).Append("\":[");
                float length = anim.StartsWith("walk") ? 3f : 6f;
                int frames = Mathf.RoundToInt(length * 15f);
                for (int f = 0; f <= frames; f++)
                {
                    int n = backend.PoseModel(id, anim, f / 15f, poses);
                    if (f > 0) sb.Append(',');
                    sb.Append('[');
                    for (int p = 0; p < n; p++)
                    {
                        if (p > 0) sb.Append(',');
                        sb.Append("{\"m\":");
                        Matrix(sb, poses[p].Matrix);
                        sb.Append(",\"h\":").Append(poses[p].Hidden ? 1 : 0).Append('}');
                    }
                    sb.Append(']');
                }
                sb.Append(']');
            }
            sb.Append("}}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, def.ObjectName.ToLowerInvariant() + "-rig.json"), sb.ToString());
            Debug.Log($"RIG_DUMP {def.ObjectName} {md.Pieces.Length} pieces, animations {string.Join(",", def.Animations)}");
        }
    }
}
