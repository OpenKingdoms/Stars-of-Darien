// TrailerScenes.cs - the trailer's scenes, in the order they are filmed.
// Each sets up its battle through the engine, stages it with placed
// armies and orders, and films one or more shots. The camera never orbits:
// shots hold still, pan slowly or push in, with eased starts and stops.
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
        public static readonly (string Name, Func<TrailerDirector, IEnumerator> Run)[] Scenes =
        {
            ("survey", d => d.SurveyScene()),
            ("probe", d => d.Probe()),
            ("menus", d => d.Menus()),
            ("aramon", d => d.Kingdom("aramon", "into the woods", WeatherChoice.Off, "ARAMON",
                new[] { ("ARAPAL", 6), ("ARAKNIGH", 5), ("ARASWORD", 8), ("ARAARCH", 6), ("ARAPRIES", 2), ("ARADRAG", 1) })),
            ("zhon", d => d.Kingdom("zhon", "harsh dunes", WeatherChoice.Off, "ZHON",
                new[] { ("ZONTROLL", 4), ("ZONGIANT", 2), ("ZONORC", 8), ("ZONBASIL", 2), ("ZONSHAM", 2), ("ZONROC", 2) }, -1, 19f)),
            ("taros", d => d.Kingdom("taros", "tarosian plain", WeatherChoice.Fog, "TAROS",
                new[] { ("TARBLACK", 6), ("TARZOM", 8), ("TARARCH", 6), ("TARMAGE", 3), ("TARDEMON", 2), ("TARGARG", 3) }, 0)),
            ("veruna", d => d.Kingdom("veruna", "evergreen ridge", WeatherChoice.Rain, "VERUNA",
                new[] { ("VERKNIGH", 6), ("VERSWORD", 8), ("VERMUSK", 6), ("VERARCH", 4), ("VERLIEGE", 2), ("VERBALL", 1) })),
            ("creon", d => d.Kingdom("creon", "icy peaks", WeatherChoice.Snow, "CREON",
                new[] { ("CRESHOC", 8), ("CREAUTO", 4), ("CRETORT", 2), ("CREBEAS", 5), ("CRESAGE", 2), ("CREBARN", 2) })),
            ("dragons", d => d.Dragons()),
            ("formation", d => d.Formation()),
            ("build", d => d.Build()),
            ("sea", d => d.Sea()),
            ("battle", d => d.Finale()),
        };

        IEnumerator SurveyScene()
        {
            Survey(Path.Combine(OutDir, "survey"));
            yield break;
        }

        // ---- Helpers for staging ----

        public Vector3 MapCentre => Ground(Map.Size.x * 0.5f, -Map.Size.y * 0.5f);

        public static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // The two starts nearest each other but at least `min` apart.
        public (int a, int b) NearStarts(string map, float min)
        {
            var m = FindMap(map);
            int ba = 0, bb = 1;
            float best = float.MaxValue;
            for (int i = 0; i < m.Starts.Length; i++)
                for (int j = 0; j < m.Starts.Length; j++)
                {
                    if (i == j) continue;
                    float d = (m.Starts[i] - m.Starts[j]).magnitude;
                    if (d >= min && d < best) { best = d; ba = i; bb = j; }
                }
            return (ba, bb);
        }

        // The start farthest from a given one.
        public int FarStart(string map, int from)
        {
            var m = FindMap(map);
            int best = from == 0 ? 1 : 0;
            for (int i = 0; i < m.Starts.Length; i++)
                if (i != from && (m.Starts[i] - m.Starts[from]).sqrMagnitude > (m.Starts[best] - m.Starts[from]).sqrMagnitude) best = i;
            return best;
        }

        readonly FeatureState[] featBuf = new FeatureState[32768];

        // Which features a map has, for the log.
        public void NoteFeatures()
        {
            int n = B.ReadFeatures(featBuf);
            var count = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                var f = featBuf[i];
                if (f.Def < 0 || f.Def >= B.FeatureDefs.Count) continue;
                string k = B.FeatureDefs[f.Def].Name;
                count[k] = count.TryGetValue(k, out int c) ? c + 1 : 1;
            }
            Note("features: " + string.Join(", ", count.OrderByDescending(kv => kv.Value).Take(25).Select(kv => $"{kv.Key} {kv.Value}")));
        }

        // The nearest mana site to a point, if one is within reach.
        public bool ManaSite(Vector3 near, float reach, out Vector3 at)
        {
            at = default;
            int n = B.ReadFeatures(featBuf);
            float best = reach * reach;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var f = featBuf[i];
                if (f.Def < 0 || f.Def >= B.FeatureDefs.Count) continue;
                if (B.FeatureDefs[f.Def].Name.IndexOf("mana", StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (f.Position - near).sqrMagnitude;
                if (d < best) { best = d; at = f.Position; found = true; }
            }
            return found;
        }

        // The player's monarch: their builder with the most to build.
        public UnitState Monarch(int player) =>
            Units(u => u.Player == player && !B.UnitDefs[u.Def].IsBuilding)
                .OrderByDescending(u => B.UnitDefs[u.Def].BuildOptions.Length).FirstOrDefault();

        // Where the most units stand within 10 of each other.
        public static Vector3 Thick(List<UnitState> units, Vector3 fallback)
        {
            var best = fallback;
            int most = 0;
            foreach (var u in units)
            {
                int c = 0;
                foreach (var v in units) if ((v.Position - u.Position).sqrMagnitude < 100f) c++;
                if (c > most) { most = c; best = u.Position; }
            }
            return best;
        }

        // An order to raise def near `at` by `builder`, on the first clear site in rings.
        public bool BuildNear(int builder, int def, Vector3 at, float from = 0f, float to = 40f)
        {
            if (def < 0) return false;
            int turns = B.CanRotate(def) ? 2 : 1;
            for (float ring = from; ring < to; ring += 2f)
                for (int k = 0; k < 16; k++)
                    for (int facing = 0; facing < turns; facing++)
                    {
                        float a = k * Mathf.PI / 8f;
                        var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring;
                        if (!B.CanBuildAt(def, p, facing, out var site)) continue;
                        if (B.Command(new GameCommand { Kind = CommandKind.Build, Unit = builder, Target = site, TargetUnit = -1, BuildDef = def, Facing = facing }))
                            return true;
                    }
            return false;
        }

        // A lodestone on the mana site nearest the player's start, raised by
        // its monarch and finished before filming, so it breathes its light.
        public IEnumerator Lodestone(int player, string side, Vector3 near, Action<Vector3> built = null)
        {
            var m = Monarch(player);
            string prefix = side == "ZHON" ? "ZON" : side.Substring(0, 3);
            int lode = Def(prefix + "LODE");
            if (m.MaxHealth == 0 || lode < 0 || !ManaSite(near, 60f, out var site)) { Note($"no lodestone for {side}"); yield break; }
            if (!BuildNear(m.Handle, lode, site, 0f, 3f)) { Note($"{side} lodestone would not stand at {site}"); yield break; }
            for (int t = 0; t < 60 * 240; t += 600)
            {
                yield return FastForward(600);
                var up = Units(u => u.Def == lode && u.Player == player && u.BuildProgress >= 1f);
                if (up.Count > 0)
                {
                    Note($"{side} lodestone up at {up[0].Position} after {t + 600} ticks");
                    built?.Invoke(up[0].Position);
                    yield break;
                }
            }
            Note($"{side} lodestone not finished");
        }

        public IEnumerator WaitArrive(IList<int> handles, Vector3 at, float within, int maxTicks)
        {
            for (int t = 0; t < maxTicks; t += 120)
            {
                var us = Of(handles);
                if (us.Count == 0) yield break;
                if ((Centre(us) - at).magnitude < within) yield break;
                yield return FastForward(120);
            }
        }

        // ---- Scenes ----

        // A short shot of everything the pipeline does, to check it end to end.
        IEnumerator Probe()
        {
            yield return MenuShot("probe-menu", 1.5f);
            yield return Battle("two castles", WeatherChoice.Off, 5000, Seat.You("ARAMON", 0), Seat.Ai("TAROS", 1, 1));
            int me = PlayerOf(0), foe = PlayerOf(1);
            var mine = Place(me, "ARAKNIGH", 8).Concat(Place(me, "ARAARCH", 8)).ToList();
            var theirs = Place(foe, "TARZOM", 8).Concat(Place(foe, "TARARCH", 6)).ToList();
            var a = Centre(Of(mine));
            var t = Centre(Of(theirs));
            float heading = YawOf(t - a);
            March(mine, Vector3.Lerp(a, t, 0.35f), heading, 8, 1.6f);
            yield return FastForward(60 * 8);
            var now = Centre(Of(mine));
            yield return Shot("probe-low", 3f, TrailerKit.Move(Look(now, 26f, 14f, heading + 150f), Look(now, 22f, 14f, heading + 150f)),
                f => { if (f == 30) Attack(mine, t); });
            yield return Shot("probe-hud", 1f, TrailerKit.Hold(Look(Centre(Of(mine)), 30f, 50f, 0f)), null, true);
        }

        // The main menu and the skirmish lobby with all five kingdoms seated.
        IEnumerator Menus()
        {
            if (Root.Flow.State == FlowState.Playing) Root.Flow.Fire(FlowEvent.Pause);
            if (Root.Flow.State != FlowState.MainMenu) Root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            yield return MenuShot("menu-main", 4f);
            Root.Flow.Fire(FlowEvent.OpenSkirmish);
            var map = FindMap("tarosian plain");
            var s = Root.Setup;
            s.MapId = map.Id;
            var sides = new[] { "ARAMON", "VERUNA", "TAROS", "ZHON", "CREON" };
            s.Seats.Clear();
            for (int i = 0; i < sides.Length; i++)
                s.Seats.Add(new SeatSetup { Kind = i == 0 ? SeatKind.Human : SeatKind.Computer, Side = sides[i], Colour = i, Team = i, Start = -1, Difficulty = AiDifficulty.Normal });
            Root.Screens.Show(FlowState.Skirmish);
            yield return MenuShot("menu-lobby", 4f);
            Root.Flow.Fire(FlowEvent.Back);
            yield return null;
        }

        // A kingdom's column marching toward the camera through its own
        // country, past a lodestone where one stands, in a weather of its own.
        IEnumerator Kingdom(string name, string map, WeatherChoice weather, string side, (string unit, int count)[] army, int start = -1, float pitch = 14f)
        {
            if (start < 0) start = SunStart(map);
            int far = FarStart(map, start);
            string foe = side == "TAROS" ? "ARAMON" : "TAROS";
            yield return Battle(map, weather, 20000, Seat.You(side, start), Seat.Ai(foe, 1, far));
            NoteFeatures();
            int me = PlayerOf(0);
            var home = Start(start);
            Vector3? lode = null;
            yield return Lodestone(me, side, home, p => lode = p);
            var handles = new List<int>();
            foreach (var (unit, count) in army) handles.AddRange(Place(me, unit, count));
            yield return FastForward(30);
            var near = lode ?? Vector3.Lerp(home, MapCentre, 0.15f);
            var pose = OpenView(near, 30f, 24f, pitch, 30f, name, lode);
            var fwd = Forward(pose.Yaw);
            var right = Vector3.Cross(Vector3.up, fwd);
            float heading = pose.Yaw + 180f;
            var gather = pose.Focus + fwd * 13f + right * 2f;
            var end = pose.Focus - fwd * 7f;
            March(handles, gather, heading, 6, 1.9f);
            yield return WaitArrive(handles, gather, 3f, 60 * 90);
            yield return FastForward(60 * 3);
            var back = new ShotPose(pose.Focus + fwd * 2f, pose.Distance + 3f, pose.Pitch + 1f, pose.Yaw - 3f);
            var close = new ShotPose(pose.Focus, pose.Distance - 3f, pose.Pitch - 1f, pose.Yaw + 3f);
            // The order as a player gives it, with the column's answer.
            int voice = Of(handles).Select(u => u.Def).FirstOrDefault();
            yield return Shot(name, 6f, TrailerKit.Move(back, close), f =>
            {
                if (f == 0) March(handles, end, heading, 6, 1.9f);
                if (f == 45) Tap?.Ack(f, voice, "move");
            });
        }

        // Dragons of four kingdoms fall on an enemy's base as it grows,
        // breath from the head.
        IEnumerator Dragons()
        {
            const string map = "two castles";
            const int away = 1;
            yield return Battle(map, WeatherChoice.Off, 20000, Seat.You("TAROS", 0), Seat.Ai("VERUNA", 1, away, AiDifficulty.Easy));
            int me = PlayerOf(0), foe = PlayerOf(1);
            var dragons = new List<int>();
            foreach (var d in new[] { "TARDRAG", "ARADRAG", "ZONDRAG", "CREDRAG", "TARDRAG" }) dragons.AddRange(Place(me, d, 1));
            Aggro(dragons, 0);
            foreach (var (u, c) in new[] { ("VERSWORD", 8), ("VERARCH", 8), ("VERMUSK", 6) }) Place(foe, u, c);
            // The computer builds for a minute and a half first.
            yield return FastForward(60 * 90);
            var site = Start(away);
            var built = Units(u => u.Player == foe && B.UnitDefs[u.Def].IsBuilding);
            var target = built.Count > 0 ? Centre(built) : site;
            var dir = target - Centre(Of(dragons));
            dir.y = 0;
            dir.Normalize();
            var wait = target - dir * 44f;
            March(dragons, wait, YawOf(dir), 5, 4f);
            yield return WaitArrive(dragons, wait, 5f, 60 * 120);
            yield return FastForward(60);
            Note($"dragons: {Of(dragons).Count} dragons over {wait}, {built.Count} buildings at {target}");
            // From the side of their path: the base on one hand, the dragons
            // coming in from the other, the camera low so they fly in frame.
            // Looking a little down on the base, the frame raised to where the dragons fly.
            Lift = 6f;
            var pose = BestPose(Vector3.Lerp(target, wait, 0.35f), 48f, 12f, Across(dir), 6f, true, "dragons", target, wait);
            yield return Shot("dragons", 7f, TrailerKit.Move(pose, new ShotPose(pose.Focus + dir * 5f, pose.Distance - 6f, pose.Pitch + 1f, pose.Yaw)), f =>
            {
                if (f != 0) return;
                Aggro(dragons, 2);
                var targets = Units(u => u.Player == foe && (u.Position - target).sqrMagnitude < 900f);
                for (int i = 0; i < dragons.Count; i++)
                {
                    if (targets.Count == 0) { B.Command(GameCommand.To(CommandKind.Patrol, dragons[i], target)); continue; }
                    var t = targets[i % targets.Count];
                    B.Command(new GameCommand { Kind = CommandKind.Attack, Unit = dragons[i], Target = t.Position, TargetUnit = t.Handle, BuildDef = -1 });
                }
            });
            Lift = 0f;
        }

        // Fire orders for a group: 0 holds fire, 1 returns it, 2 fires at will.
        public void Aggro(IEnumerable<int> handles, int arg)
        {
            foreach (int h in handles)
                B.Command(new GameCommand { Kind = CommandKind.SetAggro, Unit = h, TargetUnit = -1, BuildDef = -1, Arg = arg });
        }

        // The formation drag: the line drawn on the ground, the preview of
        // every place, then the army fanning out into its ranks.
        IEnumerator Formation()
        {
            const string map = "edmont's field";
            yield return Battle(map, WeatherChoice.Off, 20000, Seat.You("ARAMON", 0), Seat.Ai("TAROS", 1, FarStart(map, 0)));
            int me = PlayerOf(0);
            var army = new List<int>();
            foreach (var (u, c) in new[] { ("ARASWORD", 12), ("ARAPAL", 8), ("ARAARCH", 8), ("ARAKNIGH", 6), ("ARAPRIES", 2) })
                army.AddRange(Place(me, u, c));
            var home = Start(0);
            // Flat open ground, the army facing across the sunlight so the
            // camera behind it and the one before it both see it lit.
            var ground = FlatGround(Vector3.Lerp(home, MapCentre, 0.2f), 36f, 18f);
            float sun = SunYaw;
            var inland = MapCentre - ground;
            float heading = Vector3.Dot(Forward(sun + 90f), inland) >= 0 ? sun + 90f : sun - 90f;
            var dir = Forward(heading);
            var gather = ground - dir * 7f;
            March(army, gather, heading, 9, 2.2f);
            yield return WaitArrive(army, gather, 3f, 60 * 90);
            yield return FastForward(60 * 4);
            var c0 = Centre(Of(army));
            var right = Vector3.Cross(Vector3.up, dir);
            var lineMid = c0 + dir * 14f;
            var l0 = lineMid - right * 14f;
            var l1 = lineMid + right * 14f;

            var orders = Root.Orders;
            orders.Frozen = false;
            orders.Classic = false;
            var fi = orders.Formation;
            fi.Shape = FormationShape.Line;
            fi.Pace = true;
            var frame = new PointerFrame { Focused = true, Dpi = 96 };
            fi.Source = () =>
            {
                var r = frame;
                frame.RightDown = frame.RightUp = false;
                return r;
            };
            B.Select(army.ToArray(), false);
            orders.Selected.Clear();
            orders.Selected.UnionWith(army);
            var cam = Cam;
            Vector2 ScreenOf(Vector3 g) => cam.WorldToScreenPoint(Ground(g.x, g.z));
            const int pressAt = 40, dragFrames = 75, releaseAt = 175;
            var pose = new ShotPose(Vector3.Lerp(c0, lineMid, 0.5f), 42f, 50f, heading);
            yield return Shot("formation-drag", 4.4f, TrailerKit.Move(pose, new ShotPose(pose.Focus + dir * 1.5f, 39f, 49f, heading)), f =>
            {
                if (f == pressAt)
                {
                    frame.Screen = ScreenOf(l0);
                    frame.RightDown = frame.RightHeld = true;
                }
                else if (f > pressAt && f <= pressAt + dragFrames)
                    frame.Screen = ScreenOf(Vector3.Lerp(l0, l1, TrailerKit.Ease((f - pressAt) / (float)dragFrames)));
                else if (f == releaseAt)
                {
                    frame.RightHeld = false;
                    frame.RightUp = true;
                    var first = Of(army).FirstOrDefault();
                    Tap?.Ack(f, first.MaxHealth > 0 ? first.Def : -1, "move");
                }
            });
            fi.Source = null;
            orders.Frozen = true;
            Note($"formation: {army.Count} units, line {l0} to {l1}, first order {B.ReadOrder(army[0]).Kind}");
            // No rings for the close look: the ranks as they stand.
            orders.Selected.Clear();
            B.Select(Array.Empty<int>(), false);
            // They walk most of the way unseen, and the shot finds them
            // taking their places, seen from the front they march toward.
            var before = Centre(Of(army));
            yield return FastForward(60 * 3);
            var walk = Centre(Of(army)) - before;
            walk.y = 0;
            float front = walk.sqrMagnitude > 0.5f ? YawOf(-walk) : heading + 180f;
            var ranks = BestPose(lineMid, 26f, 11f, new[] { front + 30f, front - 30f, front + 45f, front - 45f, front }, 4f, true, "formation ranks", lineMid, Centre(Of(army)));
            yield return Shot("formation-ranks", 6f, TrailerKit.Move(ranks, new ShotPose(ranks.Focus, ranks.Distance - 3f, ranks.Pitch, ranks.Yaw + 4f)));
        }

        // Buildings rising from the ground as builders raise them.
        IEnumerator Build()
        {
            const string map = "evergreen ridge";
            yield return Battle(map, WeatherChoice.Off, 50000, Seat.You("ARAMON", 0), Seat.Ai("TAROS", 1, FarStart(map, 0)));
            int me = PlayerOf(0);
            var home = Start(0);
            var builders = Place(me, "ARABUILD", 4);
            var m = Monarch(me);
            var dir = MapCentre - home;
            dir.y = 0;
            dir.Normalize();
            var site = home + dir * 12f;
            var wanted = new[] { "ARACASTL", "ARAKEEP", "ARAAT", "ARAKEEP", "ARAAT" };
            var crew = new List<int> { m.Handle };
            crew.AddRange(builders);
            for (int i = 0; i < crew.Count && i < wanted.Length; i++)
                if (!BuildNear(crew[i], Def(wanted[i]), site + Quaternion.Euler(0, i * 72f, 0) * Vector3.forward * (i == 0 ? 0f : 9f), 0f, 20f))
                    Note($"build: {wanted[i]} found no site");
            // Until the first walls show, a few per cent up.
            List<UnitState> rising = new List<UnitState>();
            for (int t = 0; t < 60 * 90; t += 30)
            {
                yield return FastForward(30);
                rising = Units(u => u.Player == me && B.UnitDefs[u.Def].IsBuilding && u.BuildProgress > 0f && u.BuildProgress < 1f);
                if (rising.Count >= 2 && rising.Min(u => u.BuildProgress) > 0.02f || rising.Count > 0 && rising.Max(u => u.BuildProgress) > 0.3f) break;
            }
            Note("build: " + string.Join(", ", rising.Select(u => $"{B.UnitDefs[u.Def].Name} {u.BuildProgress:P0}")));
            var c = rising.Count > 0 ? Centre(rising) : site;
            float sun = SunYaw;
            var pose = BestPose(c, 32f, 24f, new[] { sun, sun + 25f, sun - 25f, sun + 50f, sun - 50f }, 3f, true, "build");
            Root.Options.GameSpeed = 5;
            // The crew answering as a player picks them out and sets them to work.
            int builderDef = Def("ARABUILD");
            yield return Shot("build", 7f, TrailerKit.Move(pose, new ShotPose(pose.Focus, pose.Distance - 5f, pose.Pitch - 2f, pose.Yaw + 4f)), f =>
            {
                if (f == 20) Tap?.Ack(f, m.Def, "select");
                if (f == 150) Tap?.Ack(f, builderDef, "move");
            });
            Root.Options.GameSpeed = 1;
            rising = Of(rising.Select(u => u.Handle));
            Note("build after: " + string.Join(", ", rising.Select(u => $"{B.UnitDefs[u.Def].Name} {u.BuildProgress:P0}")));
        }

        // Ships at sea, firing on each other, their wakes on the new water.
        IEnumerator Sea()
        {
            const string map = "islands of the mer warrior";
            yield return Battle(map, WeatherChoice.Off, 30000, Seat.You("VERUNA", 0), Seat.Ai("TAROS", 1, 2, AiDifficulty.Easy));
            int me = PlayerOf(0), foe = PlayerOf(1);
            var fleet = Place(me, "VERMAN", 3).Concat(Place(me, "VERHARP", 3)).Concat(Place(me, "VERFLAG", 1)).Concat(Place(me, "VERDRAG", 1)).ToList();
            var enemy = Place(foe, "TARSHIP", 4).Concat(Place(foe, "ARAWAR", 2)).Concat(Place(foe, "CREIRON", 2)).ToList();
            yield return FastForward(30);
            var us = Centre(Of(fleet));
            var them = Centre(Of(enemy));
            Note($"sea: fleet {fleet.Count} at {us}, enemy {enemy.Count} at {them}");
            var dir = them - us;
            dir.y = 0;
            dir.Normalize();
            // Out to open water short of the enemy, holding fire on the way.
            var meet = OpenWater(them - dir * 36f, 30f);
            Aggro(fleet, 0);
            March(fleet, meet, YawOf(dir), 4, 3.5f);
            yield return WaitArrive(fleet, meet, 6f, 60 * 400);
            Aggro(fleet, 2);
            Attack(fleet, Centre(Of(enemy)));
            // Until the two lines are within cannon shot of each other.
            for (int t = 0; t < 60 * 60; t += 60)
            {
                if (Of(enemy).Count == 0 || (Centre(Of(fleet)) - Centre(Of(enemy))).magnitude < 40f) break;
                yield return FastForward(60);
            }
            Note($"sea: lines {(Centre(Of(fleet)) - Centre(Of(enemy))).magnitude:0} apart, {Of(fleet).Count} against {Of(enemy).Count}");
            // Over the fleet's shoulder toward the enemy line.
            var mid = Vector3.Lerp(Centre(Of(fleet)), Centre(Of(enemy)), 0.6f);
            mid.y = B.Terrain.SeaLevel;
            float ahead = YawOf(Centre(Of(enemy)) - Centre(Of(fleet)));
            var wide = BestPose(mid, 40f, 13f, new[] { ahead, ahead + 20f, ahead - 20f, ahead + 35f, ahead - 35f }, 6f, false, "sea wide", Centre(Of(fleet)), Centre(Of(enemy)));
            yield return Shot("sea-1", 6f, TrailerKit.Move(wide, new ShotPose(wide.Focus, wide.Distance - 4f, wide.Pitch - 1f, wide.Yaw + 5f)));
            var near = Centre(Of(fleet));
            near.y = B.Terrain.SeaLevel;
            var close = BestPose(near, 20f, 7f, Across(dir), 5f, false, "sea close", near);
            yield return Shot("sea-2", 5f, TrailerKit.Move(close, new ShotPose(close.Focus, close.Distance - 2f, close.Pitch + 1f, close.Yaw - 5f)));
        }

        // The clash: armies of every kingdom, dragons overhead, and a look at
        // the battle HUD.
        IEnumerator Finale()
        {
            const string map = "into the woods";
            var (a, b) = NearStarts(map, 90f);
            yield return Battle(map, WeatherChoice.Off, 30000, Seat.You("ARAMON", a), Seat.Ai("TAROS", 1, b, AiDifficulty.Normal));
            int me = PlayerOf(0), foe = PlayerOf(1);
            var army = new List<int>();
            foreach (var (u, c) in new[] { ("ARAPAL", 8), ("ARAKNIGH", 8), ("ARASWORD", 10), ("ARAARCH", 8), ("ARAPRIES", 3), ("ARABOW", 3), ("ZONTROLL", 3), ("VERMUSK", 6), ("CREAUTO", 3), ("ARADRAG", 2) })
                army.AddRange(Place(me, u, c));
            var foes = new List<int>();
            foreach (var (u, c) in new[] { ("TARBLACK", 8), ("TARZOM", 12), ("TARARCH", 10), ("TARMAGE", 4), ("TARDEMON", 3), ("TARGARG", 4), ("TARDRAG", 2), ("ZONGIANT", 2) })
                foes.AddRange(Place(foe, u, c));
            yield return FastForward(30);
            var us = Centre(Of(army));
            var them = Centre(Of(foes));
            var dir = them - us;
            dir.y = 0;
            float dist = dir.magnitude;
            dir.Normalize();
            var line = us + dir * Mathf.Max(0f, dist - 26f);
            March(army, line, YawOf(dir), 12, 2f);
            yield return WaitArrive(army, line, 4f, 60 * 90);
            Attack(army, them);
            yield return FastForward(60 * 3);
            var front = Vector3.Lerp(Centre(Of(army)), Centre(Of(foes)), 0.5f);
            Note($"finale: {army.Count} against {foes.Count}, front at {front}");
            var low = BestPose(front, 24f, 16f, Across(dir), 5f, true, "battle low", Centre(Of(army)), Centre(Of(foes)));
            yield return Shot("battle-low", 7f, TrailerKit.Move(low, new ShotPose(low.Focus, low.Distance - 4f, low.Pitch + 2f, low.Yaw + 6f)));
            front = Vector3.Lerp(Centre(Of(army)), Centre(Of(foes)), 0.5f);
            yield return Shot("battle-wide", 6f, TrailerKit.Move(Look(front - dir * 6f, 60f, 32f, YawOf(dir) + 30f), Look(front, 52f, 30f, YawOf(dir) + 20f)));
            // The battle as a player sees it, with the HUD and a few selected.
            var picked = Of(army).Take(6).Select(u => u.Handle).ToArray();
            B.Select(picked, false);
            Root.World.Entities.Selected.Clear();
            Root.World.Entities.Selected.UnionWith(picked);
            front = Thick(Of(army).Concat(Of(foes)).ToList(), front);
            yield return Shot("battle-hud", 4f, TrailerKit.Move(Look(front, 27f, GameCamera.ClassicPitch, 0f), Look(front + Vector3.forward * 1.5f, 25f, GameCamera.ClassicPitch, 0f)), null, true);
        }
    }
}
