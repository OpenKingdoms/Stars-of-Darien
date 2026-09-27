// AnimOverrideTests.cs - animation nudges round-trip through their file,
// stack the all-animations nudge under an animation's own, and carry a
// piece's children along.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class AnimOverrideTests
    {
        [Test]
        public void NudgesRoundTripThroughJson()
        {
            var o = new AnimOverride { Model = "araking" };
            o.Set("torso", AnimOverride.All, new AnimOverride.Nudge { Move = new Vector3(0, 0.25f, 0) });
            o.Set("larm", "walk", new AnimOverride.Nudge { Turn = new Vector3(10, 0, -5) });
            var back = AnimOverride.FromJson(o.ToJson());
            Assert.AreEqual("araking", back.Model);
            Assert.AreEqual(new Vector3(0, 0.25f, 0), back.Get("torso", AnimOverride.All).Move);
            Assert.AreEqual(new Vector3(10, 0, -5), back.Get("LARM", "walk").Turn, "piece names match in any case");
            Assert.IsTrue(back.Get("larm", AnimOverride.All).IsZero);
            back.Set("torso", AnimOverride.All, default);
            back.Set("larm", "walk", default);
            Assert.IsTrue(back.IsEmpty, "clearing every nudge leaves nothing");
        }

        [Test]
        public void AChildFollowsItsNudgedParent()
        {
            var pieces = new[]
            {
                new PieceInfo { Name = "base", Parent = -1 },
                new PieceInfo { Name = "arm", Parent = 0, Offset = new Vector3(1, 0, 0) },
                new PieceInfo { Name = "hand", Parent = 1, Offset = new Vector3(1, 0, 0) },
            };
            var m = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(1, 0, 0)), Matrix4x4.Translate(new Vector3(2, 0, 0)) };
            var o = new AnimOverride();
            o.Set("arm", AnimOverride.All, new AnimOverride.Nudge { Move = new Vector3(0, 1, 0) });
            o.Set("hand", "attack", new AnimOverride.Nudge { Move = new Vector3(0, 0, 1) });
            o.Apply(pieces, m, 3, "walk");
            Assert.AreEqual(new Vector3(0, 0, 0), (Vector3)m[0].GetColumn(3));
            Assert.AreEqual(new Vector3(1, 1, 0), (Vector3)m[1].GetColumn(3));
            Assert.AreEqual(new Vector3(2, 1, 0), (Vector3)m[2].GetColumn(3), "the hand rides the arm, and attack's nudge is not in walk");

            var m2 = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(1, 0, 0)), Matrix4x4.Translate(new Vector3(2, 0, 0)) };
            o.Apply(pieces, m2, 3, "attack");
            Assert.AreEqual(new Vector3(2, 1, 1), (Vector3)m2[2].GetColumn(3), "attack stacks its own nudge on the all one");
        }
    }
}
