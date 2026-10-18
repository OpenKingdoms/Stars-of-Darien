// ThirshaScenes.cs - stills of Thirsha's remastered model on the real
// engine, for review: beside the original from the front, a low
// three-quarter, behind, her face and hair close, the classic camera, a
// turntable, in flight, and in three players' colours. ThirshaCaptures.cs is the entry
// point. Snap, Both, Approach and Stage are in ElsinScenes.cs.
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
        public (string Name, Func<TrailerDirector, IEnumerator> Run)[] ThirshaScenes => new (string, Func<TrailerDirector, IEnumerator>)[]
        {
            ("stills", d => d.ThirshaStills()),
            ("flight", d => d.ThirshaFlight()),
            ("colours", d => d.ThirshaColours()),
        };

        const string ThirshaMap = "into the woods";

        UnitState Thirsha(int player) =>
            Units(u => u.Player == player && string.Equals(B.UnitDefs[u.Def].ObjectName, "ZONHUNT", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        // How high a unit is over the ground under it, so a shot frames her where she is.
        float Above(UnitState u) => Mathf.Max(0f, u.Position.y - B.GroundHeight(u.Position.x, u.Position.z));

        // She lands when she stops; waits for that, up to `seconds`.
        IEnumerator Settle(int player, int seconds = 20)
        {
            for (int t = 0; t < seconds * 2; t++)
            {
                if (Above(Thirsha(player)) < 0.15f) yield break;
                yield return FastForward(30);
            }
        }

        IEnumerator ThirshaBattle(int colour = 0)
        {
            int start = SunStart(ThirshaMap);
            var you = Seat.You("ZHON", start);
            you.Colour = colour;
            var foe = Seat.Ai("ARAMON", 1, FarStart(ThirshaMap, start));
            foe.Colour = colour == 6 ? 5 : 6;
            yield return Battle(ThirshaMap, WeatherChoice.Off, 20000, you, foe);
            var spot = Stage(start);
            int me = PlayerOf(0);
            yield return Approach(Thirsha(me).Handle, spot + Forward(SunYaw) * 6f, spot);
            yield return Settle(me);
        }

        IEnumerator ThirshaStills()
        {
            yield return ThirshaBattle();
            int me = PlayerOf(0);
            var e = Thirsha(me);
            var at = e.Position;
            float face = e.Heading + 180f, up = Above(e);
            Note($"Thirsha stands at {at} facing {e.Heading:0}, {up:0.00} over the ground, the sun's yaw {SunYaw:0}");
            Lift = up + 2.3f;
            yield return Both("thirsha-front", e, Look(at, 12f, 10f, face));
            yield return Both("thirsha-low34", e, Look(at, 12f, 6f, face + 38f));
            yield return Both("thirsha-behind", e, Look(at, 12f, 12f, face + 160f));
            Lift = up + 3.42f;
            yield return Both("thirsha-face", e, Look(at, 2.4f, 6f, face + 18f));
            yield return Snap("thirsha-face-front", Look(at, 2.1f, 4f, face));
            yield return Snap("thirsha-face-side", Look(at, 2.3f, 6f, face + 70f));
            yield return Snap("thirsha-hair-back34", Look(at, 2.6f, 10f, face + 135f));
            yield return Snap("thirsha-hair-behind", Look(at, 2.6f, 14f, face + 180f));
            Lift = up + 2.3f;
            for (int k = 0; k < 8; k++)
                yield return Snap($"thirsha-turn-{k}", Look(at, 11f, 8f, face + k * 45f));
            // The classic camera looks north, so she turns to face south for it.
            yield return Approach(e.Handle, at + new Vector3(0f, 0f, 6f), at);
            yield return Settle(me);
            e = Thirsha(me);
            Lift = Above(e) + 1f;
            yield return Both("thirsha-classic", e, Look(e.Position, 18f, GameCamera.ClassicPitch, 0f));
            yield return Both("thirsha-classic-far", e, Look(e.Position, 34f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        // In the air: flying across the view, the camera keeping level with her.
        IEnumerator ThirshaFlight()
        {
            yield return ThirshaBattle();
            int me = PlayerOf(0);
            var e = Thirsha(me);
            var at = e.Position;
            float face = e.Heading + 180f;
            var side = Forward(e.Heading + 90f);
            B.Command(GameCommand.To(CommandKind.Move, e.Handle, at + side * 30f));
            yield return FastForward(60 * 3);
            for (int k = 0; k < 6; k++)
            {
                var now = Thirsha(me);
                Lift = Above(now) + 2.3f;
                Note($"flight {k}: {Above(now):0.00} over the ground");
                yield return Snap($"flight-low-{k}", Look(now.Position, 12f, 8f, face + 40f), 5);
            }
            var u = Thirsha(me);
            Lift = Above(u) + 1f;
            yield return Both("flight-classic", u, Look(u.Position, 22f, GameCamera.ClassicPitch, 0f));
            Lift = 0f;
        }

        // The stills again in three players' colours, a battle for each.
        IEnumerator ThirshaColours()
        {
            foreach (var (colour, name) in new[] { (0, "blue"), (1, "red"), (3, "green") })
            {
                yield return ThirshaBattle(colour);
                var e = Thirsha(PlayerOf(0));
                float face = e.Heading + 180f;
                Lift = Above(e) + 2.3f;
                yield return Snap($"colours-{name}-front", Look(e.Position, 12f, 10f, face));
                yield return Snap($"colours-{name}-low34", Look(e.Position, 12f, 6f, face + 38f));
                yield return Snap($"colours-{name}-behind", Look(e.Position, 12f, 12f, face + 160f));
                Lift = 0f;
            }
        }
    }
}
