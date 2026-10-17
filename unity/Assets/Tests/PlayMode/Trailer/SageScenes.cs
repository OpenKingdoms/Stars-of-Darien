// SageScenes.cs - stills of the Great Sage's remastered model on the real
// engine, for review: beside the original from the front, a low
// three-quarter, the side, behind, his face and the classic camera, a
// turntable, his walk, his three weapons with the blade opening into jaws,
// building, the pieces his death throws, three players' colours and the
// key of Creon on his shield.
// SageCaptures.cs is the entry point. Snap, Both and Approach are in
// ElsinScenes.cs.
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
        public (string Name, Func<TrailerDirector, IEnumerator> Run)[] SageScenes => new (string, Func<TrailerDirector, IEnumerator>)[]
        {
            ("stills", d => d.SageStills()),
            ("anims", d => d.SageAnimations()),
            ("colours", d => d.SageColours()),
            ("emblem", d => d.SageEmblem()),
        };

        const string SageMap = "edmont's field";

        UnitState Sage(int player) =>
            Units(u => u.Player == player && string.Equals(B.UnitDefs[u.Def].ObjectName, "CRESAGE", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        // A battle with him walked out to open ground, clear of the walls
        // by his start and of trees for a camera standing back.
        IEnumerator SageBattle(int colour = 0)
        {
            int start = SunStart(SageMap);
            var you = Seat.You("CREON", start);
            you.Colour = colour;
            var foe = Seat.Ai("ARAMON", 1, FarStart(SageMap, start));
            foe.Colour = colour == 6 ? 5 : 6;
            yield return Battle(SageMap, WeatherChoice.Off, 20000, you, foe);
            var spot = FlatGround(Vector3.Lerp(Start(start), MapCentre, 0.3f), 48f, 15f);
            yield return Approach(Sage(PlayerOf(0)).Handle, spot + Forward(SunYaw) * 6f, spot);
        }

        IEnumerator SageStills()
        {
            yield return SageBattle();
            int me = PlayerOf(0);
            var e = Sage(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            Note($"the Sage stands at {at} facing {e.Heading:0}, the sun's yaw {SunYaw:0}");
            Lift = 2.4f;
            yield return Both("sage-front", e, Look(at, 12f, 10f, face));
            yield return Both("sage-low34", e, Look(at, 12f, 6f, face + 38f));
            yield return Both("sage-side", e, Look(at, 12f, 8f, face + 90f));
            yield return Both("sage-behind", e, Look(at, 12f, 12f, face + 160f));
            Lift = 3.55f;
            yield return Both("sage-face", e, Look(at, 3.2f, 6f, face + 20f));
            yield return Snap("sage-face-front", Look(at, 2.9f, 4f, face));
            yield return Snap("sage-face-34", Look(at, 3.0f, 6f, face + 35f));
            yield return Snap("sage-face-side", Look(at, 3.2f, 6f, face + 75f));
            Lift = 2.4f;
            for (int k = 0; k < 8; k++)
                yield return Snap($"sage-turn-{k}", Look(at, 11f, 8f, face + k * 45f));
            // The classic camera looks north, so he turns to face south for it.
            yield return Approach(e.Handle, at + new Vector3(0f, 0f, 6f), at);
            e = Sage(me);
            Lift = 1f;
            yield return Both("sage-classic", e, Look(e.Position, 18f, GameCamera.ClassicPitch, 0f));
            yield return Both("sage-classic-far", e, Look(e.Position, 34f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        IEnumerator SageAnimations()
        {
            yield return SageBattle();
            int me = PlayerOf(0);
            var e = Sage(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            var ahead = Forward(e.Heading);
            var side = Forward(e.Heading + 90f);
            Lift = 2.2f;

            // His walk, across the view.
            B.Command(GameCommand.To(CommandKind.Move, e.Handle, at + side * 14f));
            yield return FastForward(30);
            for (int k = 0; k < 6; k++)
            {
                var now = Sage(me);
                yield return Snap($"anim-walk-{k}", Look(now.Position, 11f, 9f, face + 40f), 6);
            }
            yield return Approach(e.Handle, at + Forward(SunYaw) * 6f, at);

            // His three weapons at the ground ahead: the blue flame from the
            // blade's jaws, the mortar, the energy blast round him.
            string[] weapons = { "flame", "mortar", "blast" };
            for (int s = 0; s < 3; s++)
            {
                e = Sage(me);
                var target = e.Position + ahead * (s == 1 ? 12f : 6f);
                Note($"weapon {weapons[s]}: {Cast(e.Handle, s, target)}");
                var view = s == 0 ? Look(e.Position + ahead * 1.5f, 8f, 16f, face + 50f) : Look(e.Position, 11f, 12f, face + 40f);
                for (int k = 0; k < 6; k++) yield return Snap($"anim-{weapons[s]}-{k}", view, 7);
                yield return FastForward(60 * 3);
                B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
                yield return FastForward(60 * 3);
            }

            // Building a tower ahead of him.
            e = Sage(me);
            Note($"build: {BuildNear(e.Handle, Def("CREGATL"), e.Position + ahead * 6f, 0f, 12f)}");
            yield return FastForward(60);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-build-{k}", Look(e.Position + ahead * 2f, 12f, 14f, face + 40f), 12);
            B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
            yield return FastForward(60 * 3);

            // His death: what cresage.cob's Killed throws, and the rest of
            // him gone, as the original takes him away.
            e = Sage(me);
            var model = ModelOf(e);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            int posed = B.ReadUnitPose(e.Handle, poses);
            var ents = Root.World.Entities;
            int id = 1 << 29;
            foreach (var (name, how) in new[] { ("luparm", PieceExplode.Fall), ("rarm", PieceExplode.Fall), ("rhand", PieceExplode.Fall),
                                                ("torso", PieceExplode.Fall | PieceExplode.Bitmap5), ("lupleg", PieceExplode.Fall),
                                                ("lfoot", PieceExplode.Fall), ("ltoe", PieceExplode.Fall), ("rupleg", PieceExplode.Fall) })
            {
                int p = Array.FindIndex(model.Data.Pieces, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p < 0 || p >= posed) continue;
                ents.ThrowPiece(new PieceEvent { Id = id++, Unit = e.Handle, Def = e.Def, Player = e.Player, Model = e.Model, Piece = p, How = how, Pose = poses[p].Matrix });
            }
            int dead = e.Handle;
            var hidden = ents.Hidden;
            ents.Hidden = u => u.Handle == dead || (hidden?.Invoke(u) ?? false);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-death-{k}", Look(e.Position, 11f, 16f, face + 40f), k == 0 ? 1 : 8);
            ents.Hidden = hidden;
            Lift = 0f;
        }

        // The key of Creon on his shield's hub: the whole of him from the
        // shield's side, the shield close from eight sides aimed at its
        // middle, then the classic camera a little closer than the game's.
        IEnumerator SageEmblem()
        {
            yield return SageBattle();
            int me = PlayerOf(0);
            var e = Sage(me);
            var model = ModelOf(e);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            int posed = B.ReadUnitPose(e.Handle, poses);
            int hand = Array.FindIndex(model.Data.Pieces, x => string.Equals(x.Name, "lhand", StringComparison.OrdinalIgnoreCase));
            // The shield's middle in the hand's 3DO frame (x, y up, z back), in fixed point.
            var at = e.Position + Vector3.up * 1.5f;
            if (hand >= 0 && hand < posed) at = poses[hand].Matrix.MultiplyPoint3x4(new Vector3(0.25f, -0.18f, 0f) * (16f * 65536f));
            float ground = B.GroundHeight(at.x, at.z);
            Note($"the shield at {at}, {at.y - ground:0.00} over the ground, the Sage at {e.Position}");
            float face = e.Heading + 180f;
            Lift = 2.4f;
            yield return Both("sage-shieldside-front", e, Look(e.Position, 12f, 10f, face - 20f));
            yield return Both("sage-shieldside-34", e, Look(e.Position, 12f, 6f, face - 40f));
            Lift = at.y - ground;
            for (int k = 0; k < 8; k++)
                yield return Snap($"shield-round-{k}", Look(at, 3.4f, 15f, face + k * 45f));
            yield return Approach(e.Handle, e.Position + new Vector3(0f, 0f, 6f), e.Position);
            e = Sage(me);
            Lift = 1f;
            yield return Both("emblem-classic", e, Look(e.Position, 11f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        // The stills again in three players' colours, a battle for each.
        IEnumerator SageColours()
        {
            foreach (var (colour, name) in new[] { (0, "blue"), (1, "red"), (3, "green") })
            {
                yield return SageBattle(colour);
                var e = Sage(PlayerOf(0));
                float face = e.Heading + 180f;
                Lift = 2.4f;
                yield return Snap($"colours-{name}-front", Look(e.Position, 12f, 10f, face));
                yield return Snap($"colours-{name}-low34", Look(e.Position, 12f, 6f, face + 38f));
                yield return Snap($"colours-{name}-behind", Look(e.Position, 12f, 12f, face + 160f));
                Lift = 0f;
            }
        }
    }
}
