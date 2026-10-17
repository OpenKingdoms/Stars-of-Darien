// LokkenScenes.cs - stills of Lokken's remastered model on the real engine,
// for review: beside the original from the front, a low three-quarter,
// the side and behind, his face and the classic camera, a turntable, his
// walk, his three spells and the pieces his death throws, and in three
// players' colours. LokkenCaptures.cs is the entry point. Snap, Both,
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
        public (string Name, Func<TrailerDirector, IEnumerator> Run)[] LokkenScenes => new (string, Func<TrailerDirector, IEnumerator>)[]
        {
            ("stills", d => d.LokkenStills()),
            ("anims", d => d.LokkenAnimations()),
            ("colours", d => d.LokkenColours()),
        };

        const string LokkenMap = "into the woods";

        UnitState Lokken(int player) =>
            Units(u => u.Player == player && string.Equals(B.UnitDefs[u.Def].ObjectName, "TARNECRO", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        IEnumerator LokkenBattle(int colour = 0)
        {
            int start = SunStart(LokkenMap);
            var you = Seat.You("TAROS", start);
            you.Colour = colour;
            var foe = Seat.Ai("ARAMON", 1, FarStart(LokkenMap, start));
            foe.Colour = colour == 6 ? 5 : 6;
            yield return Battle(LokkenMap, WeatherChoice.Off, 20000, you, foe);
            var spot = Stage(start);
            var e = Lokken(PlayerOf(0));
            yield return Approach(e.Handle, spot + Forward(SunYaw) * 6f, spot);
        }

        IEnumerator LokkenStills()
        {
            yield return LokkenBattle();
            int me = PlayerOf(0);
            var e = Lokken(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            Note($"Lokken stands at {at} facing {e.Heading:0}, the sun's yaw {SunYaw:0}");
            Lift = 2.0f;
            yield return Both("lokken-front", e, Look(at, 9.5f, 10f, face));
            yield return Both("lokken-low34", e, Look(at, 9.5f, 6f, face + 38f));
            yield return Both("lokken-side", e, Look(at, 9.5f, 6f, face + 90f));
            yield return Both("lokken-behind", e, Look(at, 9.5f, 12f, face + 160f));
            // His face leans out ahead of him, so the close shots look there.
            var head = at + Forward(e.Heading) * 0.85f;
            Lift = 3.15f;
            yield return Both("lokken-face", e, Look(head, 2.4f, 6f, face + 18f));
            yield return Snap("lokken-face-front", Look(head, 2.1f, 4f, face));
            yield return Snap("lokken-face-side", Look(head, 2.3f, 6f, face + 70f));
            Lift = 2.0f;
            for (int k = 0; k < 8; k++)
                yield return Snap($"lokken-turn-{k}", Look(at, 9f, 8f, face + k * 45f));
            // The classic camera looks north, so he turns to face south for it.
            yield return Approach(e.Handle, at + new Vector3(0f, 0f, 6f), at);
            e = Lokken(me);
            Lift = 0.8f;
            yield return Both("lokken-classic", e, Look(e.Position, 16f, GameCamera.ClassicPitch, 0f));
            yield return Both("lokken-classic-far", e, Look(e.Position, 34f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        IEnumerator LokkenAnimations()
        {
            yield return LokkenBattle();
            int me = PlayerOf(0);
            var e = Lokken(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            var ahead = Forward(e.Heading);
            var side = Forward(e.Heading + 90f);
            Lift = 2.0f;
            var view = Look(at, 12f, 20f, face - 30f);

            // His walk, across the view.
            B.Command(GameCommand.To(CommandKind.Move, e.Handle, at + side * 14f));
            yield return FastForward(30);
            for (int k = 0; k < 6; k++)
            {
                var now = Lokken(me);
                yield return Snap($"anim-walk-{k}", Look(now.Position, 11f, 18f, face - 30f), 6);
            }
            yield return Approach(e.Handle, at + Forward(SunYaw) * 6f, at);

            // The fire ball, the guided fire ball and the third spell, each at the ground ahead.
            string[] spells = { "fireball", "guided", "third" };
            for (int s = 0; s < 3; s++)
            {
                e = Lokken(me);
                var target = e.Position + ahead * (s == 2 ? 5f : 9f);
                Note($"cast {spells[s]}: {Cast(e.Handle, s, target)}");
                for (int k = 0; k < 6; k++) yield return Snap($"anim-{spells[s]}-{k}", view, 7);
                yield return FastForward(60 * 4);
                B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
                yield return FastForward(60 * 3);
            }

            // His death: what tarnecro.cob's Killed throws when a blast kills
            // him, and the rest of him gone, as the original takes him away.
            e = Lokken(me);
            var model = ModelOf(e);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            int posed = B.ReadUnitPose(e.Handle, poses);
            var ents = Root.World.Entities;
            int id = 1 << 29;
            foreach (var name in new[] { "staff", "ruparm", "luparm", "head", "torso", "rupleg", "lupleg" })
            {
                int p = Array.FindIndex(model.Data.Pieces, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p < 0 || p >= posed) continue;
                var how = name == "torso" ? PieceExplode.Fall | PieceExplode.Bitmap5 : PieceExplode.Fall;
                ents.ThrowPiece(new PieceEvent { Id = id++, Unit = e.Handle, Def = e.Def, Player = e.Player, Model = e.Model, Piece = p, How = how, Pose = poses[p].Matrix });
            }
            int dead = e.Handle;
            var hidden = ents.Hidden;
            ents.Hidden = u => u.Handle == dead || (hidden?.Invoke(u) ?? false);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-death-{k}", Look(e.Position, 11f, 24f, face - 30f), k == 0 ? 1 : 8);
            ents.Hidden = hidden;
            Lift = 0f;
        }

        // The stills again in three players' colours, a battle for each.
        IEnumerator LokkenColours()
        {
            foreach (var (colour, name) in new[] { (0, "blue"), (1, "red"), (3, "green") })
            {
                yield return LokkenBattle(colour);
                var e = Lokken(PlayerOf(0));
                float face = e.Heading + 180f;
                Lift = 2.0f;
                yield return Snap($"colours-{name}-front", Look(e.Position, 9.5f, 10f, face));
                yield return Snap($"colours-{name}-low34", Look(e.Position, 9.5f, 6f, face - 38f));
                yield return Snap($"colours-{name}-behind", Look(e.Position, 9.5f, 12f, face + 160f));
                Lift = 0f;
            }
        }
    }
}
