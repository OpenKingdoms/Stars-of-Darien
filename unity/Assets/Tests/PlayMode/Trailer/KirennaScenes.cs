// KirennaScenes.cs - stills of Kirenna's remastered model on the real
// engine, for review: beside the original from the front, a low
// three-quarter, the side, behind, her face and the classic camera, a
// turntable, her walk, a cast and the pieces her death throws, and in three
// players' colours. KirennaCaptures.cs is the entry point. Snap, Both,
// Approach and Stage are in ElsinScenes.cs.
using System;
using System.Collections;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        public (string Name, Func<TrailerDirector, IEnumerator> Run)[] KirennaScenes => new (string, Func<TrailerDirector, IEnumerator>)[]
        {
            ("stills", d => d.KirennaStills()),
            ("anims", d => d.KirennaAnimations()),
            ("colours", d => d.KirennaColours()),
        };

        const string KirennaMap = "into the woods";

        UnitState Kirenna(int player) =>
            Units(u => u.Player == player && string.Equals(B.UnitDefs[u.Def].ObjectName, "VERMAGE", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        IEnumerator KirennaBattle(int colour = 0)
        {
            int start = SunStart(KirennaMap);
            var you = Seat.You("VERUNA", start);
            you.Colour = colour;
            var foe = Seat.Ai("ARAMON", 1, FarStart(KirennaMap, start));
            foe.Colour = colour == 6 ? 5 : 6;
            yield return Battle(KirennaMap, WeatherChoice.Off, 20000, you, foe);
            // Turned a little from the sun, so the trident's shadow falls clear of her face.
            var spot = Stage(start);
            yield return Approach(Kirenna(PlayerOf(0)).Handle, spot + Forward(SunYaw + 40f) * 6f, spot);
        }

        IEnumerator KirennaStills()
        {
            yield return KirennaBattle();
            int me = PlayerOf(0);
            var e = Kirenna(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            Note($"Kirenna stands at {at} facing {e.Heading:0}, the sun's yaw {SunYaw:0}");
            Lift = 2.2f;
            yield return Both("kirenna-front", e, Look(at, 10.5f, 10f, face));
            yield return Both("kirenna-low34", e, Look(at, 10.5f, 6f, face + 38f));
            yield return Both("kirenna-side", e, Look(at, 10.5f, 6f, face + 90f));
            yield return Both("kirenna-behind", e, Look(at, 10.5f, 12f, face + 160f));
            // Her face from her left, clear of the trident in her right hand.
            Lift = 3.64f;
            yield return Both("kirenna-face", e, Look(at, 2.3f, 6f, face - 22f));
            yield return Snap("kirenna-face-front", Look(at, 2.0f, 4f, face));
            yield return Snap("kirenna-face-side", Look(at, 2.2f, 5f, face - 75f));
            Lift = 2.2f;
            for (int k = 0; k < 8; k++)
                yield return Snap($"kirenna-turn-{k}", Look(at, 10f, 8f, face + k * 45f));
            // The classic camera looks north, so she turns to face south for it.
            yield return Approach(e.Handle, at + new Vector3(0f, 0f, 6f), at);
            e = Kirenna(me);
            Lift = 0.9f;
            yield return Both("kirenna-classic", e, Look(e.Position, 17f, GameCamera.ClassicPitch, 0f));
            yield return Both("kirenna-classic-far", e, Look(e.Position, 34f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        IEnumerator KirennaAnimations()
        {
            yield return KirennaBattle();
            int me = PlayerOf(0);
            var e = Kirenna(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            var side = Forward(e.Heading + 90f);
            Lift = 2.2f;

            // Her walk, across the view.
            B.Command(GameCommand.To(CommandKind.Move, e.Handle, at + side * 14f));
            yield return FastForward(30);
            for (int k = 0; k < 6; k++)
            {
                var now = Kirenna(me);
                yield return Snap($"anim-walk-{k}", Look(now.Position, 10f, 9f, face - 40f), 6);
            }
            yield return Approach(e.Handle, at + Forward(SunYaw) * 6f, at);

            // Her first spell, at the ground to her side, seen from in front.
            e = Kirenna(me);
            var view = Look(e.Position, 10f, 9f, face);
            Note($"cast: {Cast(e.Handle, 0, e.Position + side * 8f)}");
            for (int k = 0; k < 8; k++) yield return Snap($"anim-cast-{k}", view, 5);
            yield return FastForward(60 * 4);
            B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
            yield return FastForward(60 * 3);

            // Her death: what vermage.cob's Killed throws when a blast kills
            // her, and the rest of her gone, as the original takes her away.
            e = Kirenna(me);
            var model = ModelOf(e);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            int posed = B.ReadUnitPose(e.Handle, poses);
            var ents = Root.World.Entities;
            int id = 1 << 29;
            foreach (var name in new[] { "ArmUR", "ArmLR", "HandR", "ArmUL", "ArmLL", "HandL", "Torso", "LegUR", "LegLR", "FootR", "LegUL", "LegLL", "FootL", "Hip" })
            {
                int p = Array.FindIndex(model.Data.Pieces, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p < 0 || p >= posed) continue;
                var how = name == "Torso" ? PieceExplode.Fall | PieceExplode.Bitmap5 : PieceExplode.Fall;
                ents.ThrowPiece(new PieceEvent { Id = id++, Unit = e.Handle, Def = e.Def, Player = e.Player, Model = e.Model, Piece = p, How = how, Pose = poses[p].Matrix });
            }
            int dead = e.Handle;
            var hidden = ents.Hidden;
            ents.Hidden = u => u.Handle == dead || (hidden?.Invoke(u) ?? false);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-death-{k}", Look(e.Position, 10f, 16f, face - 40f), k == 0 ? 1 : 8);
            ents.Hidden = hidden;
            Lift = 0f;
        }

        // The stills again in three players' colours, a battle for each.
        IEnumerator KirennaColours()
        {
            foreach (var (colour, name) in new[] { (0, "blue"), (1, "red"), (3, "green") })
            {
                yield return KirennaBattle(colour);
                var e = Kirenna(PlayerOf(0));
                float face = e.Heading + 180f;
                Lift = 2.2f;
                yield return Snap($"colours-{name}-front", Look(e.Position, 10.5f, 10f, face));
                yield return Snap($"colours-{name}-low34", Look(e.Position, 10.5f, 6f, face + 38f));
                yield return Snap($"colours-{name}-behind", Look(e.Position, 10.5f, 12f, face + 160f));
                Lift = 0f;
            }
        }
    }
}
