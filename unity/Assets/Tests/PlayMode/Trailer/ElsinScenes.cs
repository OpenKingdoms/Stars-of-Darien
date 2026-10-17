// ElsinScenes.cs - stills of Elsin's remastered model on the real engine,
// for review: beside the original from the front, a low three-quarter,
// behind and the classic camera, a turntable, his walk, his three spells,
// building and the pieces his death throws, with his army in a battle, and
// in three players' colours. ElsinCaptures.cs is the entry point.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        public (string Name, Func<TrailerDirector, IEnumerator> Run)[] ElsinScenes => new (string, Func<TrailerDirector, IEnumerator>)[]
        {
            ("stills", d => d.ElsinStills()),
            ("anims", d => d.ElsinAnimations()),
            ("army", d => d.ElsinArmy()),
            ("colours", d => d.ElsinColours()),
        };

        // A still of the world from a pose, as OutDir/<name>.png.
        public IEnumerator Snap(string name, ShotPose pose, int settle = 4)
        {
            Root.World.Entities.HideBars = true;
            Pose(pose);
            for (int i = 0; i < settle; i++) yield return null;
            Pose(pose);
            yield return null;
            Cam.rect = new Rect(0, 0, 1, 1);
            GrabWorld();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Note($"still {name}");
        }

        UnitState Elsin(int player) =>
            Units(u => u.Player == player && string.Equals(B.UnitDefs[u.Def].ObjectName, "ARAKING", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        PresentedModel ModelOf(UnitState u) => Root.World.Models.Get(u.Model, OverrideKind.Unit, B.UnitDefs[u.Def].ModelNames);

        // The same still with the original model, then the remaster.
        IEnumerator Both(string name, UnitState u, ShotPose pose)
        {
            var m = ModelOf(u);
            var remaster = m.Override;
            m.Override = null;
            yield return Snap(name + "-original", pose);
            m.Override = remaster;
            yield return Snap(name + "-remaster", pose);
        }

        // Walks a unit to `to`, arriving from `from`, so it ends facing from `from` toward `to`.
        IEnumerator Approach(int handle, Vector3 from, Vector3 to)
        {
            B.Command(GameCommand.To(CommandKind.Move, handle, from));
            yield return WaitArrive(new[] { handle }, from, 1.5f, 60 * 40);
            B.Command(GameCommand.To(CommandKind.Move, handle, to));
            yield return WaitArrive(new[] { handle }, to, 0.6f, 60 * 40);
            yield return FastForward(60 * 5);
        }

        // Open ground near home with the sun behind the camera that faces him there.
        Vector3 Stage(int seat, float reach = 20f)
        {
            var home = Start(seat);
            return FlatGround(Vector3.Lerp(home, MapCentre, 0.12f), reach, 5f);
        }

        const string ElsinMap = "into the woods";

        IEnumerator ElsinStills()
        {
            int start = SunStart(ElsinMap);
            yield return Battle(ElsinMap, WeatherChoice.Off, 20000, Seat.You("ARAMON", start), Seat.Ai("TAROS", 1, FarStart(ElsinMap, start)));
            int me = PlayerOf(0);
            var spot = Stage(start);
            float sun = SunYaw;
            var e = Elsin(me);
            yield return Approach(e.Handle, spot + Forward(sun) * 6f, spot);
            e = Elsin(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            Note($"Elsin stands at {at} facing {e.Heading:0}, the sun's yaw {sun:0}");
            Lift = 2.0f;
            yield return Both("elsin-front", e, Look(at, 8.5f, 10f, face));
            yield return Both("elsin-low34", e, Look(at, 8.5f, 6f, face + 38f));
            yield return Both("elsin-behind", e, Look(at, 8.5f, 12f, face + 160f));
            Lift = 3.45f;
            yield return Both("elsin-face", e, Look(at, 2.6f, 6f, face + 15f));
            Lift = 2.0f;
            for (int k = 0; k < 8; k++)
                yield return Snap($"elsin-turn-{k}", Look(at, 8.5f, 8f, face + k * 45f));
            // The classic camera looks north, so he turns to face south for it.
            yield return Approach(e.Handle, at + new Vector3(0f, 0f, 6f), at);
            e = Elsin(me);
            Lift = 0.8f;
            yield return Both("elsin-classic", e, Look(e.Position, 16f, GameCamera.ClassicPitch, 0f));
            yield return Both("elsin-classic-far", e, Look(e.Position, 34f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        IEnumerator ElsinAnimations()
        {
            int start = SunStart(ElsinMap);
            yield return Battle(ElsinMap, WeatherChoice.Off, 20000, Seat.You("ARAMON", start), Seat.Ai("TAROS", 1, FarStart(ElsinMap, start)));
            int me = PlayerOf(0);
            var spot = Stage(start);
            float sun = SunYaw;
            var e = Elsin(me);
            yield return Approach(e.Handle, spot + Forward(sun) * 6f, spot);
            e = Elsin(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            var ahead = Forward(e.Heading);
            var side = Forward(e.Heading + 90f);
            Lift = 2.0f;
            var view = Look(at, 10f, 9f, face + 40f);

            // His walk, across the view.
            B.Command(GameCommand.To(CommandKind.Move, e.Handle, at + side * 14f));
            yield return FastForward(30);
            for (int k = 0; k < 6; k++)
            {
                var now = Elsin(me);
                yield return Snap($"anim-walk-{k}", Look(now.Position, 10f, 9f, face + 40f), 6);
            }
            yield return Approach(e.Handle, at + Forward(sun) * 6f, at);

            // Lightning, meteor and the earthen wave, each at the ground ahead.
            string[] spells = { "lightning", "meteor", "wave" };
            for (int s = 0; s < 3; s++)
            {
                e = Elsin(me);
                var target = e.Position + ahead * (s == 2 ? 5f : 9f);
                Note($"cast {spells[s]}: {Cast(e.Handle, s, target)}");
                for (int k = 0; k < 6; k++) yield return Snap($"anim-{spells[s]}-{k}", view, 7);
                yield return FastForward(60 * 4);
                B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
                yield return FastForward(60 * 3);
            }

            // Building a tower ahead of him.
            e = Elsin(me);
            Note($"build: {BuildNear(e.Handle, Def("ARAAT"), e.Position + ahead * 6f, 0f, 12f)}");
            yield return FastForward(60);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-build-{k}", Look(e.Position + ahead * 2f, 12f, 14f, face + 40f), 12);
            B.Command(GameCommand.To(CommandKind.Stop, e.Handle, Vector3.zero));
            yield return FastForward(60 * 3);

            // His death: what araking.cob's Killed throws when a blast kills him,
            // and the rest of him gone, as the original takes him away.
            e = Elsin(me);
            var model = ModelOf(e);
            var poses = new PiecePose[EntityRenderer.MaxPieces];
            int posed = B.ReadUnitPose(e.Handle, poses);
            var ents = Root.World.Entities;
            int id = 1 << 29;
            foreach (var name in new[] { "Head", "ArmUR", "ArmUL", "ArmLR", "ArmLL", "Torso", "LegUL", "LegUR", "HandR", "HandL", "FootL", "FootR", "LegLL", "LegLR" })
            {
                int p = Array.FindIndex(model.Data.Pieces, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p < 0 || p >= posed) continue;
                var how = name == "Torso" ? PieceExplode.Fall | PieceExplode.Bitmap5 : PieceExplode.Fall;
                ents.ThrowPiece(new PieceEvent { Id = id++, Unit = e.Handle, Def = e.Def, Player = e.Player, Model = e.Model, Piece = p, How = how, Pose = poses[p].Matrix });
            }
            int dead = e.Handle;
            var hidden = ents.Hidden;
            ents.Hidden = u => u.Handle == dead || (hidden?.Invoke(u) ?? false);
            for (int k = 0; k < 6; k++) yield return Snap($"anim-death-{k}", Look(e.Position, 10f, 16f, face + 40f), k == 0 ? 1 : 8);
            ents.Hidden = hidden;
            Lift = 0f;
        }

        IEnumerator ElsinArmy()
        {
            const string map = "edmont's field";
            yield return Battle(map, WeatherChoice.Off, 30000, Seat.You("ARAMON", 0), Seat.Ai("TAROS", 1, FarStart(map, 0), AiDifficulty.Easy));
            int me = PlayerOf(0), them = PlayerOf(1);
            var army = new List<int>();
            foreach (var (u, c) in new[] { ("ARASWORD", 10), ("ARAPAL", 6), ("ARAARCH", 6), ("ARAKNIGH", 4), ("ARAPRIES", 2) })
                army.AddRange(Place(me, u, c));
            var foes = new List<int>();
            foreach (var (u, c) in new[] { ("TARTROOP", 12), ("TARZOM", 6), ("TARARCH", 6), ("TARKNIGH", 4) })
                foes.AddRange(Place(them, u, c));
            var e = Elsin(me);
            var home = Start(0);
            var field = Vector3.Lerp(home, MapCentre, 0.35f);
            field = FlatGround(field, 20f, 10f);
            var dir = field - home;
            dir.y = 0;
            float heading = YawOf(dir);
            var all = new List<int>(army) { e.Handle };
            March(all, field - dir.normalized * 6f, heading, 8, 2.2f);
            yield return WaitArrive(all, field - dir.normalized * 6f, 4f, 60 * 120);
            Attack(foes, field);
            for (int t = 0; t < 60 * 180; t += 120)
            {
                yield return FastForward(120);
                var near = Units(u => u.Player == them && (u.Position - field).magnitude < 16f);
                if (near.Count > 6) break;
            }
            Attack(all, field + dir.normalized * 4f);
            yield return FastForward(60 * 3);
            e = Elsin(me);
            Lift = 1.5f;
            for (int k = 0; k < 4; k++)
            {
                e = Elsin(me);
                yield return Snap($"army-low-{k}", Look(e.Position, 15f, 14f, heading + 180f + 35f), 20);
            }
            Lift = 0.5f;
            e = Elsin(me);
            yield return Snap("army-classic", Look(e.Position, 40f, GameCamera.ClassicPitch, 0f), 10);
            yield return Snap("army-high", Look(e.Position, 30f, 38f, heading + 150f), 10);
            Lift = 0f;
        }

        // The stills again in three players' colours, a battle for each, so
        // only the colour changes from one to the next.
        IEnumerator ElsinColours()
        {
            foreach (var (colour, name) in new[] { (0, "blue"), (1, "red"), (3, "green") })
            {
                int start = SunStart(ElsinMap);
                var you = new Seat { Side = "ARAMON", Kind = SeatKind.Human, Team = 0, Start = start, Colour = colour };
                var foe = Seat.Ai("TAROS", 1, FarStart(ElsinMap, start));
                foe.Colour = 6;
                yield return Battle(ElsinMap, WeatherChoice.Off, 20000, you, foe);
                int me = PlayerOf(0);
                var spot = Stage(start);
                var e = Elsin(me);
                yield return Approach(e.Handle, spot + Forward(SunYaw) * 6f, spot);
                e = Elsin(me);
                float face = e.Heading + 180f;
                Lift = 2.0f;
                yield return Snap($"colours-{name}-front", Look(e.Position, 8.5f, 10f, face));
                yield return Snap($"colours-{name}-low34", Look(e.Position, 8.5f, 6f, face + 38f));
                yield return Snap($"colours-{name}-behind", Look(e.Position, 8.5f, 12f, face + 160f));
                Lift = 0f;
            }
        }
    }
}
