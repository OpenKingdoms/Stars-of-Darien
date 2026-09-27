// FlatPieceTests.cs - a model's flat pieces, such as the Aramon keep's
// build pad, draw with a depth offset so they never fight the ground.
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FlatPieceTests
    {
        [Test]
        public void OnlyThinWidePiecesAreFlat()
        {
            Assert.IsTrue(ModelCache.IsFlat(new Bounds(Vector3.zero, new Vector3(3, 0, 3))), "a pad");
            Assert.IsFalse(ModelCache.IsFlat(new Bounds(Vector3.zero, new Vector3(3, 1, 3))), "a wall");
            Assert.IsFalse(ModelCache.IsFlat(new Bounds(Vector3.zero, new Vector3(0.1f, 0, 0.1f))), "a speck");
        }

        [Test]
        public void TheKeepsBuildPadDrawsAboveTheGround()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            using (var b = new EngineBackend())
            {
                var s = new SkirmishSetup { MapId = "two castles", Seed = 5, LineOfSight = false, MapRevealed = true };
                s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = b.Sides[0].Id, Colour = 0, Team = 0 });
                s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = b.Sides[0].Id, Colour = 1, Team = 1 });
                b.StartSkirmish(s);
                for (int i = 0; i < 4000 && !b.PumpLoading().Done; i++) { }
                var cache = new ModelCache(b);
                int flat = 0;
                foreach (var name in new[] { "ARAKEEP", "VERKEEP" })
                {
                    var m = cache.Get(b.LoadModel(name, 0));
                    Assert.IsNotNull(m, name);
                    for (int p = 0; p < m.Pieces.Length; p++)
                    {
                        if (m.Pieces[p] == null || !ModelCache.IsFlat(m.Pieces[p].bounds)) continue;
                        flat++;
                        Debug.Log($"Flat piece: {name} {m.Data.Pieces[p].Name}, {m.Pieces[p].bounds.size.x:F2} by {m.Pieces[p].bounds.size.z:F2}, {m.Pieces[p].bounds.size.y:F3} thick, at height {m.Data.Pieces[p].Offset.y * m.Data.Scale + m.Pieces[p].bounds.center.y:F3}");
                        foreach (var mat in m.Materials[p])
                            Assert.AreEqual(ModelCache.FlatOffsetUnits, mat.GetFloat("_OffsetUnits"), $"{name} piece {m.Data.Pieces[p].Name}");
                    }
                }
                Assert.Greater(flat, 0, "the keeps have a flat pad");
                b.EndGame();
            }
        }
    }
}
