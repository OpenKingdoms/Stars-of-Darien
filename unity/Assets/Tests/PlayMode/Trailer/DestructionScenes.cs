// DestructionScenes.cs - the destruction work on the real engine with real
// maps: forests burning, walls breached stage by stage, Creon towns falling
// to stages with no model of their own, each kind of magic on scenery,
// dragon breath, a long battle's craters, and rain and snow. The same
// scenes serve the destruction trailer. The census finds the scenery each
// candidate map holds, and the perf scenes measure a big battle's cost.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        public static readonly (string Name, Func<TrailerDirector, IEnumerator> Run)[] DestructionScenes =
        {
            ("census", d => d.Census()),
            ("forest-fire", d => d.ForestFire(ForestMap, WeatherChoice.Off, "fire")),
            ("siege", d => d.Siege()),
            ("creon-ruins", d => d.CreonRuins()),
            ("spells", d => d.Spells()),
            ("dragon-breath", d => d.DragonBreath()),
            ("craters", d => d.Craters()),
            ("rain", d => d.ForestFire(RainMap, WeatherChoice.Rain, "rain")),
            ("snow", d => d.Snow()),
            ("perf-ulasem", d => d.Perf("ulasem arena")),
            ("perf-forest", d => d.Perf(BigForestMap)),
        };

        // The maps, chosen from the census and the maps' own feature layers:
        // the two largest groups of trees standing close enough for the
        // original's sparks to carry fire from one to the next, the most
        // walled town, a dark volcanic forest for dragon fire, trees and
        // walls together for the spells and the snow, and open fields.
        // No skirmish map holds Creon scenery, so the Creon town is placed.
        public const string ForestMap = "lake ferrix_jm", RainMap = "new hindigal", SiegeMap = "alchemist glen",
            CreonMap = "edmont's field", SpellMap = "neegam's pass", CraterMap = "edmont's field", SnowMap = "neegam's pass",
            DragonMap = "black forest", BigForestMap = "into the woods";

        public EffectsQuality Quality = EffectsQuality.High;

        // ---- Finding scenery ----

        public struct Patch
        {
            public Vector3 At;
            public int Count;
            public List<FeatureState> Members;
        }

        public static bool IsTree(FeatureDef d) => d != null && d.Breakable && Fracture.KindOf(d) == BreakKind.Tree;
        public static bool IsWall(FeatureDef d) => d != null && d.Breakable && Fracture.KindOf(d) == BreakKind.Wall;
        public static bool IsHouse(FeatureDef d) => d != null && d.Breakable && (Fracture.KindOf(d) == BreakKind.Building || Fracture.KindOf(d) == BreakKind.Hut);
        public static bool IsCreon(FeatureDef d) => d != null && d.Breakable && d.Name.StartsWith("cre", StringComparison.OrdinalIgnoreCase) && Fracture.KindOf(d) != BreakKind.Body;

        FeatureDef FDef(int def) => def >= 0 && def < B.FeatureDefs.Count ? B.FeatureDefs[def] : null;

        // The cell of `cell` units holding the most scenery of a kind, within
        // reach of a point, its members' middle as its place.
        public Patch Densest(Func<FeatureDef, bool> keep, float cell, Vector3 near, float within)
        {
            int n = B.ReadFeatures(featBuf);
            var groups = new Dictionary<(int, int, int), List<FeatureState>>();
            for (int i = 0; i < n; i++)
            {
                var f = featBuf[i];
                if (!keep(FDef(f.Def))) continue;
                var flat = new Vector2(f.Position.x - near.x, f.Position.z - near.z);
                if (flat.magnitude > within) continue;
                // Two grids half a cell apart, so a patch across a cell's edge is still whole.
                for (int off = 0; off < 2; off++)
                {
                    var key = (Mathf.FloorToInt(f.Position.x / cell + off * 0.5f), Mathf.FloorToInt(f.Position.z / cell + off * 0.5f), off);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FeatureState>();
                    list.Add(f);
                }
            }
            var best = new Patch { At = near, Members = new List<FeatureState>() };
            foreach (var g in groups.Values)
                if (g.Count > best.Count)
                {
                    var c = Vector3.zero;
                    foreach (var f in g) c += f.Position;
                    c /= g.Count;
                    best = new Patch { At = Ground(c.x, c.z), Count = g.Count, Members = g };
                }
            return best;
        }

        // The largest group of flammable trees each within six cells of
        // another, as far as a remastered spark reaches, where a fire runs.
        public Patch FireGroup()
        {
            int n = B.ReadFeatures(featBuf);
            var trees = new List<FeatureState>();
            for (int i = 0; i < n; i++)
                if (IsTree(FDef(featBuf[i].Def)) && FDef(featBuf[i].Def).Flammable) trees.Add(featBuf[i]);
            float reach = 6f * (B.Terrain != null ? B.Terrain.CellSize : 1f) + 0.05f;
            var seen = new bool[trees.Count];
            var best = new List<FeatureState>();
            for (int i = 0; i < trees.Count; i++)
            {
                if (seen[i]) continue;
                var group = new List<FeatureState>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    group.Add(trees[k]);
                    for (int j = 0; j < trees.Count; j++)
                        if (!seen[j] && Mathf.Abs(trees[j].Position.x - trees[k].Position.x) <= reach && Mathf.Abs(trees[j].Position.z - trees[k].Position.z) <= reach)
                        {
                            seen[j] = true;
                            stack.Push(j);
                        }
                }
                if (group.Count > best.Count) best = group;
            }
            if (best.Count == 0) return new Patch { At = MapCentre, Members = best };
            var c = Vector3.zero;
            foreach (var f in best) c += f.Position;
            c /= best.Count;
            return new Patch { At = Ground(c.x, c.z), Count = best.Count, Members = best };
        }

        public int NearestStart(Vector3 at)
        {
            int best = 0;
            for (int i = 1; i < Map.Starts.Length; i++)
                if ((Start(i) - at).sqrMagnitude < (Start(best) - at).sqrMagnitude) best = i;
            return best;
        }

        // A skirmish on map with the local seat at the start nearest the
        // densest scenery of a kind, which `found` is given.
        // far: the player starts as far from it as the map allows instead,
        // with the computer nearest, for a siege of its own walls.
        IEnumerator BattleNear(string map, WeatherChoice weather, string side, string foe, Func<FeatureDef, bool> keep, float cell, Action<Patch> found, bool far = false)
        {
            Root.Options.EffectsQuality = Quality;
            yield return Battle(map, weather, 60000, Seat.You(side, 0), Seat.Ai(foe, 1, FarStart(map, 0)));
            var p = Densest(keep, cell, MapCentre, 100000f);
            int s = NearestStart(p.At);
            if (far) s = FarStart(map, s);
            if (s != 0)
            {
                yield return Battle(map, weather, 60000, Seat.You(side, s), Seat.Ai(foe, 1, FarStart(map, s)));
                p = Densest(keep, cell, MapCentre, 100000f);
            }
            Note($"{map}: {p.Count} in the densest patch at {p.At}, start {s} at {Start(s)}");
            found(p);
        }

        public List<int> Army(int player, params (string unit, int count)[] list)
        {
            var all = new List<int>();
            foreach (var (u, c) in list) all.AddRange(Place(player, u, c));
            return all;
        }

        // Marches the army to open ground `away` units short of a target, facing it.
        IEnumerator StageBefore(List<int> army, Vector3 target, float away, Action<Vector3> stood = null)
        {
            var from = Centre(Of(army));
            var dir = target - from;
            dir.y = 0f;
            float dist = dir.magnitude;
            dir = dist > 0.1f ? dir / dist : Vector3.forward;
            var spot = FlatGround(target - dir * away, 10f, 5f);
            Aggro(army, 0);
            March(army, spot, YawOf(target - spot), 6, 1.8f);
            yield return WaitArrive(army, spot, 4f, B.TicksPerSecond * 150);
            Aggro(army, 2);
            stood?.Invoke(spot);
        }

        public void AttackGround(IEnumerable<int> handles, Vector3 at, float spread = 0f, int seed = 1)
        {
            var rng = new System.Random(seed);
            foreach (int h in handles)
            {
                var p = at + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * spread * 2f;
                B.Command(GameCommand.To(CommandKind.AttackGround, h, p));
            }
        }

        public void Halt(IEnumerable<int> handles)
        {
            foreach (int h in handles) B.Command(GameCommand.To(CommandKind.Stop, h, Vector3.zero));
        }

        // A spell cast as the HUD casts it: chosen, then aimed at a point.
        public bool Cast(int caster, int slot, Vector3 at)
        {
            B.Select(new[] { caster }, false);
            var actions = B.SelectionActions();
            var a = actions.FirstOrDefault(x => x.Kind == ActionKind.Spell && x.Arg == slot);
            if (a == null || !a.Enabled)
                Note($"cast: slot {slot} of {B.UnitDefs[Of(new[] { caster }).FirstOrDefault().Def].Name}: " +
                     string.Join(", ", actions.Select(x => $"{x.Id} {x.Kind} arg {x.Arg} {(x.Enabled ? "on" : "off " + x.Why)} cost {x.ManaCost}")));
            bool ok = a != null && B.DoAction(a.Id, at, -1, default, false);
            if (!ok)
            {
                B.Command(new GameCommand { Kind = CommandKind.SetWeapon, Unit = caster, TargetUnit = -1, BuildDef = -1, Arg = slot });
                ok = B.Command(GameCommand.To(CommandKind.AttackGround, caster, at));
            }
            B.Select(Array.Empty<int>(), false);
            return ok;
        }

        // Runs on until a feature event of a kind is seen, or the ticks run out.
        IEnumerator Until(FeatureEventKind kind, int atLeast, int maxTicks, int perFrame = 15)
        {
            int start = eventCounts.TryGetValue(kind, out int c0) ? c0 : 0;
            for (int t = 0; t < maxTicks && Playing; t += perFrame)
            {
                if ((eventCounts.TryGetValue(kind, out int c) ? c : 0) - start >= atLeast) yield break;
                yield return FastForward(perFrame, perFrame);
            }
        }

        int Seen(FeatureEventKind kind) => eventCounts.TryGetValue(kind, out int c) ? c : 0;

        // A pose with the sun behind the camera where the place allows.
        ShotPose Sunny(Vector3 focus, float distance, float pitch, string label, params Vector3[] subjects)
        {
            float sun = SunYaw;
            return BestPose(focus, distance, pitch, new[] { sun, sun + 25f, sun - 25f, sun + 50f, sun - 50f, sun + 80f, sun - 80f }, 4f, true, label, subjects);
        }

        static ShotPose Toward(ShotPose a, float closer, float turn) =>
            new ShotPose(a.Focus, a.Distance * closer, a.Pitch, a.Yaw + turn);

        Vector3 Wind(out float strength)
        {
            strength = 0f;
            if (!B.ReadWind(out var w)) return Vector3.right;
            strength = w.Strength;
            return w.Toward;
        }

        // ---- The census ----

        static readonly string[] CensusMaps =
        {
            "into the woods", "whispering wood", "yew wood", "inclined forest", "black forest", "gaerion wood", "brandon's woods",
            "the sea wood", "thorn boscage", "treehenge", "black heart jungle", "peasant grove", "evergreen ridge", "twisted grove",
            "walls of carin vel", "fortresses of laingen", "two castles", "castle", "crusader's keep", "spectre keep", "caer conlin",
            "kandran moat", "valysia city", "lantern district", "urban riverside", "clan village", "highland village", "manorhome",
            "lieber's project", "human menagerie", "sewers of elam", "terror hamlet", "crash barrens", "overseer island", "the vault",
            "streets of pain", "adamantine gate", "greenwater", "icy peaks", "edmont's field", "fallow fields", "ulasem arena",
        };

        IEnumerator Census()
        {
            string dir = Path.Combine(OutDir, "survey");
            Directory.CreateDirectory(dir);
            var lines = new List<string> { "map\tclimate\tsize\tseats\tfeatures\tbreakable\tflammable\ttrees\twalls\thouses\tcreon\tno model stages\ttree patch\twall patch\thouse patch\tcreon patch" };
            bool weapons = false;
            foreach (var name in CensusMaps)
            {
                var m = FindMap(name);
                if (m == null) { Note($"census: no map {name}"); continue; }
                Root.Options.EffectsQuality = Quality;
                yield return Battle(name, WeatherChoice.Off, 1000, Seat.You("ARAMON", 0), Seat.Ai("TAROS", 1, FarStart(name, 0)));
                if (!weapons) { WriteWeapons(Path.Combine(dir, "weapons.tsv")); weapons = true; }
                int n = B.ReadFeatures(featBuf);
                int breakable = 0, flammable = 0, trees = 0, walls = 0, houses = 0, creon = 0, stages = 0;
                var none = new HashSet<string>(Root.World.Entities.StagesWithoutModels());
                for (int i = 0; i < n; i++)
                {
                    var d = FDef(featBuf[i].Def);
                    if (d == null || !d.Breakable) continue;
                    breakable++;
                    if (d.Flammable) flammable++;
                    if (IsTree(d)) trees++;
                    if (IsWall(d)) walls++;
                    if (IsHouse(d)) houses++;
                    if (IsCreon(d)) creon++;
                    var dd = FDef(d.DeadDef);
                    var bd = FDef(d.BurntDef);
                    if (dd != null && none.Contains(dd.Name) || bd != null && none.Contains(bd.Name)) stages++;
                }
                string P(Func<FeatureDef, bool> keep)
                {
                    var p = Densest(keep, 16f, MapCentre, 100000f);
                    if (p.Count == 0) return "-";
                    int s = NearestStart(p.At);
                    return $"{p.Count} at ({p.At.x:0},{p.At.z:0}) {(Start(s) - p.At).magnitude:0} from start {s}";
                }
                lines.Add($"{m.Name}\t{m.Climate}\t{m.Size.x:0}x{m.Size.y:0}\t{m.MaxPlayers}\t{n}\t{breakable}\t{flammable}\t{trees}\t{walls}\t{houses}\t{creon}\t{stages}\t{P(IsTree)}\t{P(IsWall)}\t{P(IsHouse)}\t{P(IsCreon)}");
                File.WriteAllLines(Path.Combine(dir, "scenery.tsv"), lines);
                NoteFeatures();
            }
        }

        void WriteWeapons(string path)
        {
            var lines = new List<string> { "unit\ttitle\tside\tslot\tweapon\ttype\tsubtype\tdamage kind\tclass\tarea\tdamage\tflags\tkind" };
            foreach (var d in B.UnitDefs)
                foreach (int slot in new[] { 0, 1, 2, WeaponSlot.Death })
                {
                    var w = B.Weapon(d.Id, slot);
                    if (w == null) continue;
                    lines.Add($"{d.Name}\t{d.Title}\t{d.Side}\t{slot}\t{w.Name}\t{w.Type}\t{w.Subtype}\t{w.DamageKind}\t{w.ExplosionClass}\t{w.AreaOfEffect:0.#}\t{w.Damage}\t{w.Flags}\t{FxKinds.Of(w, d.Name)}");
                }
            File.WriteAllLines(path, lines);
            Note($"weapons: {lines.Count - 1} in {path}");
        }

        // The first unit and slot whose weapon is of a kind and reaches
        // scenery, from those named first, then any.
        public (int def, int slot) Caster(BlastKind kind, params string[] prefer)
        {
            IEnumerable<UnitDef> Order()
            {
                foreach (var p in prefer) { int id = Def(p); if (id >= 0) yield return B.UnitDefs[id]; }
                foreach (var d in B.UnitDefs) yield return d;
            }
            foreach (var d in Order())
                for (int slot = 0; slot < 3; slot++)
                {
                    var w = B.Weapon(d.Id, slot);
                    if (w == null || (w.Flags & WeaponFlags.UnitsOnly) != 0) continue;
                    if (FxKinds.Of(w, d.Name) == kind) return (d.Id, slot);
                }
            return (-1, -1);
        }

        // ---- Fire ----

        // Fire starters set the upwind side of a forest alight and it burns
        // downwind, chars, and leaves its burnt stages.
        IEnumerator ForestFire(string map, WeatherChoice weather, string name)
        {
            Root.Options.EffectsQuality = Quality;
            yield return Battle(map, weather, 60000, Seat.You("TAROS", 0), Seat.Ai("ARAMON", 1, FarStart(map, 0)));
            var forest = FireGroup();
            int s0 = NearestStart(forest.At);
            if (s0 != 0)
            {
                yield return Battle(map, weather, 60000, Seat.You("TAROS", s0), Seat.Ai("ARAMON", 1, FarStart(map, s0)));
                forest = FireGroup();
            }
            StartWatch(name);
            int me = PlayerOf(0);
            var army = Army(me, ("TARMAGE", 4), ("TARFIRE", 2), ("TARARCH", 6));
            yield return FastForward(30);
            var wind = Wind(out float strength);
            Note($"{name}: wind toward {wind} at {strength:0.00}, {forest.Count} trees close enough for fire to run, at {forest.At}");
            // Lit on its upwind edge, so the fire runs on through it, each fire
            // starter aimed at a tree, as a shot between trees lights nothing.
            var upwind = forest.Members.OrderBy(f => Vector3.Dot(f.Position - forest.At, wind)).Take(3).Select(f => f.Position).ToList();
            if (upwind.Count == 0) upwind.Add(forest.At);
            var light = upwind[0];
            Vector3 stood = default;
            yield return StageBefore(army, light, 20f, s => stood = s);
            var catchPose = Sunny(Vector3.Lerp(light, stood, 0.25f), 34f, 22f, name + " catch", light, stood);
            for (int i = 0; i < army.Count; i++) B.Command(GameCommand.To(CommandKind.AttackGround, army[i], upwind[i % upwind.Count]));
            // The shot opens on the first volley.
            for (int t = 0; t < B.TicksPerSecond * 30 && Seen(FeatureEventKind.Burning) == 0; t += 5) yield return FastForward(5, 5);
            yield return Shot($"{name}-catch", 8f, TrailerKit.Move(catchPose, Toward(catchPose, 0.85f, 4f)));
            Halt(army);
            Note($"{name}: {Seen(FeatureEventKind.Burning)} caught after the first shot, {Root.World.Fire.Burning} burning");
            // It spreads on its own.
            yield return FastForward(B.TicksPerSecond * 8);
            var burning = new List<Vector3>();
            Root.World.Fire.BurningPlaces(burning);
            var front = burning.Count > 0 ? Ground(burning.Average(p => p.x), burning.Average(p => p.z)) : forest.At;
            var across = Across(wind);
            var widePose = BestPose(front, 44f, 30f, across, 6f, true, name + " spread", front);
            yield return Shot($"{name}-spread", 10f, TrailerKit.Move(widePose, Toward(widePose, 0.9f, 3f)));
            Note($"{name}: {Seen(FeatureEventKind.Burning)} caught, {Root.World.Fire.Burning} burning in {Root.World.Fire.Patches} patches");
            // What fire leaves: char and the burnt stages.
            yield return Until(FeatureEventKind.Burnt, 6, B.TicksPerSecond * 90, 30);
            burning.Clear();
            Root.World.Fire.BurningPlaces(burning);
            var charred = Ground(light.x + wind.x * 4f, light.z + wind.z * 4f);
            var charPose = Sunny(charred, 24f, 26f, name + " char", charred);
            yield return Shot($"{name}-char", 7f, TrailerKit.Move(charPose, Toward(charPose, 0.88f, -4f)));
            Note($"{name}: {Seen(FeatureEventKind.Burnt)} burnt out");
            EndWatch();
        }

        // ---- Siege ----

        // Catapults, trebuchets and cannon take a wall down stage by stage
        // to rubble, then cave in the houses behind it.
        IEnumerator Siege()
        {
            Patch walls = default;
            yield return BattleNear(SiegeMap, WeatherChoice.Off, "ARAMON", "TAROS", IsWall, 24f, p => walls = p, true);
            StartWatch("siege");
            if (walls.Count == 0) { Note("siege: no walls"); EndWatch(); yield break; }
            int me = PlayerOf(0);
            var army = Army(me, ("ARAPULT", 3), ("ARATRE", 2), ("ARACAN", 6), ("VERMORT", 2), ("VERBAL", 2));
            yield return FastForward(30);
            // The wall nearest the army's side of the patch.
            var home = Centre(Of(army));
            var target = walls.Members.OrderBy(f => (f.Position - home).sqrMagnitude).First();
            var wall = target.Position;
            Note($"siege: {walls.Count} walls, first {FDef(target.Def).Name} at {wall}");
            Vector3 stood = default;
            yield return StageBefore(army, wall, 26f, s => stood = s);
            var side = Vector3.Cross(Vector3.up, (wall - stood).normalized);
            var pose = BestPose(wall, 30f, 16f, new[] { YawOf(side), YawOf(-side), YawOf(side) + 30f, YawOf(-side) - 30f, YawOf(wall - stood) }, 3f, true, "siege wall", wall);
            AttackGround(army, wall, 1.2f);
            yield return Shot("siege-wall", 12f, TrailerKit.Move(pose, Toward(pose, 0.9f, 3f)));
            yield return Until(FeatureEventKind.Dead, 2, B.TicksPerSecond * 40, 30);
            yield return Shot("siege-rubble", 8f, TrailerKit.Move(Toward(pose, 0.9f, 3f), Toward(pose, 0.8f, 6f)));
            // The houses inside.
            var houses = Densest(IsHouse, 24f, wall, 70f);
            if (houses.Count > 0)
            {
                var house = houses.Members.OrderBy(f => (f.Position - stood).sqrMagnitude).First().Position;
                Note($"siege: {houses.Count} houses near the wall, first at {house}");
                AttackGround(army, house, 1.5f, 7);
                yield return FastForward(B.TicksPerSecond * 4);
                var hpose = Sunny(house, 28f, 20f, "siege house", house);
                yield return Shot("siege-cavein", 10f, TrailerKit.Move(hpose, Toward(hpose, 0.88f, 3f)));
            }
            Halt(army);
            EndWatch();
        }

        // ---- Creon's town ----

        // A Creon town set down on open ground, as no skirmish map has one,
        // then shelled and burnt: most of its stages have no model of their
        // own yet and are drawn from what stood before them.
        IEnumerator CreonRuins()
        {
            Root.Options.EffectsQuality = Quality;
            yield return Battle(CreonMap, WeatherChoice.Off, 60000, Seat.You("CREON", 0), Seat.Ai("TAROS", 1, FarStart(CreonMap, 0)));
            StartWatch("creon-ruins");
            int me = PlayerOf(0);
            var defs = B.FeatureDefs;
            var stage = new bool[defs.Count];
            foreach (var d in defs)
            {
                if (d.DeadDef >= 0 && d.DeadDef < stage.Length) stage[d.DeadDef] = true;
                if (d.BurntDef >= 0 && d.BurntDef < stage.Length) stage[d.BurntDef] = true;
            }
            var kinds = defs.Where(d => IsCreon(d) && !stage[d.Id] && (d.DeadDef >= 0 || d.BurntDef >= 0)).OrderBy(d => d.Name).ToList();
            var site = FlatGround(Vector3.Lerp(Start(0), MapCentre, 0.4f), 24f, 16f);
            float cell = B.Terrain != null ? B.Terrain.CellSize : 1f;
            var town = new List<Vector3>();
            const int perRow = 7;
            for (int k = 0; k < kinds.Count; k++)
            {
                var d = kinds[k];
                float step = 5.5f;
                var at = site + new Vector3((k % perRow - (perRow - 1) * 0.5f) * step, 0f, (k / perRow - 1.5f) * step);
                int idx = B.PlaceFeature(d.Id, Mathf.RoundToInt(at.x / cell), Mathf.RoundToInt(-at.z / cell));
                if (idx >= 0) town.Add(Ground(at.x, at.z));
                else Note($"creon-ruins: {d.Name} would not stand at {at}");
            }
            Note($"creon-ruins: {town.Count} of {kinds.Count} Creon kinds set down round {site}: {string.Join(" ", kinds.Select(d => d.Name))}");
            if (town.Count == 0) { EndWatch(); yield break; }
            // Their splits are made a slice a frame before the guns open.
            for (int f = 0; f < 1800 && Root.World.Entities.Fractures.Waiting > 0; f++) yield return null;
            Note($"creon-ruins: {Root.World.Entities.Fractures.Ready} kinds split, {Root.World.Entities.Fractures.Waiting} waiting");
            var centre = Ground(town.Average(p => p.x), town.Average(p => p.z));
            var army = Army(me, ("CRETORT", 3), ("ARACAN", 8), ("ARAPULT", 2), ("CREFIRE", 2));
            yield return FastForward(30);
            Vector3 stood = default;
            yield return StageBefore(army, centre, 28f, s2 => stood = s2);
            var pose = Sunny(centre, 40f, 26f, "creon town", centre);
            // Each gun on its own building, house, tree or fence.
            var targets = town.OrderBy(p => (p - stood).sqrMagnitude).ToList();
            for (int i = 0; i < army.Count; i++)
                B.Command(GameCommand.To(CommandKind.AttackGround, army[i], targets[i % targets.Count]));
            yield return Shot("creon-ruins", 12f, TrailerKit.Move(pose, Toward(pose, 0.88f, 4f)));
            // On to the rest, until most have fallen.
            for (int round = 0; round < 4; round++)
            {
                for (int i = 0; i < army.Count; i++)
                    B.Command(GameCommand.To(CommandKind.AttackGround, army[i], targets[(i + round * army.Count) % targets.Count]));
                yield return FastForward(B.TicksPerSecond * 8);
            }
            Halt(army);
            yield return FastForward(B.TicksPerSecond * 6);
            var after = Sunny(centre, 30f, 32f, "creon after", centre);
            yield return Shot("creon-after", 6f, TrailerKit.Move(after, Toward(after, 0.9f, -3f)));
            EndWatch();
        }

        // ---- Magic ----

        static readonly (BlastKind kind, string name, string[] prefer)[] SpellKinds =
        {
            (BlastKind.Frost, "frost", new[] { "ARAPRIES", "TARWITCH", "CREGOD" }),
            (BlastKind.Dark, "dark", new[] { "TARGOD", "TARLICH" }),
            (BlastKind.Lightning, "lightning", new[] { "ZONSHAM", "TARWITCH", "ARAKING", "CREPRIS" }),
            (BlastKind.Holy, "holy", new[] { "VERLIHR", "ARAPRIES", "ARAGOD", "VERGOD" }),
            (BlastKind.Earth, "earth", new[] { "ARAGOD", "ARAPRIES", "CREGOD" }),
            (BlastKind.Water, "water", new[] { "VERMAGE", "VERDRAG", "VERGOD" }),
            (BlastKind.Wind, "wind", new[] { "TARWITCH" }),
        };

        // Each side's magic on a wood, one kind at a time, each on its own trees.
        IEnumerator Spells()
        {
            Patch wood = default;
            yield return BattleNear(SpellMap, WeatherChoice.Off, "ARAMON", "TAROS", IsTree, 40f, p => wood = p);
            StartWatch("spells");
            int me = PlayerOf(0);
            var casters = new List<(BlastKind kind, string name, int unit, int slot)>();
            foreach (var (kind, name, prefer) in SpellKinds)
            {
                // Holy light is a divine caster's, here the Priest of Lihr's water ball,
                // since the only holy spell of its own turns a unit to stone.
                var (def, slot) = kind == BlastKind.Holy && Def("VERLIHR") >= 0 ? (Def("VERLIHR"), 0) : Caster(kind, prefer);
                if (def < 0) { Note($"spells: no caster for {name}"); continue; }
                var placed = Place(me, B.UnitDefs[def].Name, 1);
                if (placed.Count == 0) continue;
                casters.Add((kind, name, placed[0], slot));
                Note($"spells: {name} by {B.UnitDefs[def].Name} slot {slot}, {B.Weapon(def, slot)?.Name}");
            }
            yield return FastForward(30);
            var all = casters.Select(c => c.unit).ToList();
            Vector3 stood = default;
            yield return StageBefore(all, wood.At, 16f, s => stood = s);
            // Trees spread over the wood, one stand for each kind.
            var trees = wood.Members.Select(f => f.Position).ToList();
            var spots = new List<Vector3>();
            foreach (var t in trees.OrderBy(t => (t - stood).sqrMagnitude))
                if (spots.All(s => (s - t).magnitude > 9f)) spots.Add(t);
            for (int i = 0; i < casters.Count; i++)
            {
                var (kind, name, unit, slot) = casters[i];
                var at = spots.Count > 0 ? spots[i % spots.Count] : wood.At;
                var pose = Sunny(at, 22f, 20f, "spell " + name, at);
                // A spell waits until its caster's own mana covers it, as the original's does.
                int cost = SpellCost(unit, slot);
                for (int t = 0; t < B.TicksPerSecond * 120 && Of(new[] { unit }).FirstOrDefault().Mana < cost; t += 60) yield return FastForward(60);
                int before = Root.World.Magic.Marked[(int)kind];
                int fromBlast = LatestBlast();
                int manaBefore = Of(new[] { unit }).FirstOrDefault().Mana;
                bool cast = false;
                string order = "";
                yield return Shot($"spell-{name}", 7f, TrailerKit.Move(pose, Toward(pose, 0.9f, 3f)), f =>
                {
                    if (f == 0) cast = Cast(unit, slot, at);
                    if (f == 30) order = B.ReadOrder(unit).Kind.ToString();
                });
                int marked = Root.World.Magic.Marked[(int)kind] - before;
                string blasts = BlastsSince(fromBlast, at);
                Note($"spell {name}: cast {cast}, order {order}, mana {manaBefore} to {Of(new[] { unit }).FirstOrDefault().Mana}, {marked} pieces of scenery marked; blasts {blasts}");
                // The engine's Tornado flies as a plain shot that never bursts, so it reports nothing to mark by.
                string casterName = B.UnitDefs[Of(new[] { unit }).FirstOrDefault().Def].Name;
                bool burst = blasts.Contains($" {casterName}/{slot} ");
                if (marked == 0 && burst) problems.Add($"{name} magic marked no scenery");
                else if (marked == 0) Note($"spell {name}: the engine reported no blast from {casterName}'s spell, so nothing could be marked");
            }
            EndWatch();
        }

        readonly BlastEvent[] blastBuf = new BlastEvent[512];

        int SpellCost(int caster, int slot)
        {
            B.Select(new[] { caster }, false);
            var a = B.SelectionActions().FirstOrDefault(x => x.Kind == ActionKind.Spell && x.Arg == slot);
            B.Select(Array.Empty<int>(), false);
            return a != null ? a.ManaCost : 0;
        }

        int LatestBlast()
        {
            int id = 0, n;
            while ((n = B.ReadBlasts(id, blastBuf)) > 0) { id = blastBuf[n - 1].Id; if (n < blastBuf.Length) break; }
            return id;
        }

        // What blasts came since an id, by weapon, kind and reach, for the log.
        string BlastsSince(int id, Vector3 at)
        {
            var sums = new Dictionary<string, (int n, float r, float d)>();
            int n;
            while ((n = B.ReadBlasts(id, blastBuf)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    var b = blastBuf[i];
                    string defName = b.Def >= 0 && b.Def < B.UnitDefs.Count ? B.UnitDefs[b.Def].Name : "-";
                    string key = $"{b.Cause} {defName}/{b.Slot} {b.Weapon?.Name ?? "no weapon"} {FxKinds.Of(b, defName)} {b.Flags}";
                    sums.TryGetValue(key, out var s);
                    sums[key] = (s.n + 1, Mathf.Max(s.r, b.Radius), s.d + new Vector2(b.Position.x - at.x, b.Position.z - at.z).magnitude);
                }
                id = blastBuf[n - 1].Id;
                if (n < blastBuf.Length) break;
            }
            return sums.Count == 0 ? "none" : string.Join("; ", sums.Select(kv => $"{kv.Value.n} x {kv.Key} radius {kv.Value.r:0.0} at {kv.Value.d / kv.Value.n:0.0} from the aim"));
        }

        // ---- Dragons ----

        IEnumerator DragonBreath()
        {
            Patch wood = default;
            yield return BattleNear(DragonMap, WeatherChoice.Off, "TAROS", "VERUNA", d => IsTree(d) && d.Flammable, 20f, p => wood = p);
            StartWatch("dragon-breath");
            int me = PlayerOf(0);
            var dragons = Army(me, ("TARDRAG", 2), ("ARADRAG", 1), ("ZONDRAG", 1), ("CREDRAG", 1), ("VERDRAG", 1));
            yield return FastForward(30);
            Vector3 stood = default;
            yield return StageBefore(dragons, wood.At, 30f, s => stood = s);
            Lift = 5f;
            var dir = (wood.At - stood).normalized;
            var pose = BestPose(Vector3.Lerp(wood.At, stood, 0.4f), 40f, 14f, Across(dir), 5f, true, "dragon breath", wood.At, stood);
            yield return Shot("dragon-breath", 9f, TrailerKit.Move(pose, Toward(pose, 0.85f, 4f)), f =>
            {
                if (f == 0) AttackGround(dragons, wood.At, 6f, 3);
            });
            Lift = 0f;
            yield return FastForward(B.TicksPerSecond * 6);
            var after = Sunny(wood.At, 30f, 30f, "breath after", wood.At);
            yield return Shot("dragon-after", 6f, TrailerKit.Move(after, Toward(after, 0.9f, 3f)));
            EndWatch();
        }

        // ---- A long battle's craters ----

        IEnumerator Craters()
        {
            var (a, b) = NearStarts(CraterMap, 70f);
            Root.Options.EffectsQuality = Quality;
            yield return Battle(CraterMap, WeatherChoice.Off, 60000, Seat.You("ARAMON", a), Seat.Ai("VERUNA", 1, b, AiDifficulty.Normal));
            StartWatch("craters");
            int me = PlayerOf(0), foe = PlayerOf(1);
            var mine = new List<int>();
            var theirs = new List<int>();
            void Reinforce()
            {
                mine.AddRange(Army(me, ("ARACAN", 8), ("ARAPULT", 2), ("ARATRE", 1), ("ARAGREN", 4), ("ARASWORD", 6)));
                theirs.AddRange(Army(foe, ("VERMUSK", 8), ("VERMORT", 3), ("VERBAL", 1), ("VERSWORD", 6)));
            }
            Reinforce();
            yield return FastForward(30);
            var field = Vector3.Lerp(Start(a), Start(b), 0.5f);
            field = FlatGround(field, 24f, 10f);
            // Three waves over three minutes.
            for (int wave = 0; wave < 3; wave++)
            {
                Attack(Of(mine).Select(u => u.Handle), field);
                foreach (var u in Of(theirs)) B.Command(GameCommand.To(CommandKind.Patrol, u.Handle, field));
                yield return FastForward(B.TicksPerSecond * 60);
                if (wave < 2) Reinforce();
            }
            var scars = ScarMap.Current;
            Note($"craters: {scars?.Stamped} stamps after three minutes, {Of(mine).Count} of ours and {Of(theirs).Count} of theirs standing");
            var wide = Sunny(field, 70f, 52f, "craters wide", field);
            yield return Shot("craters-field", 8f, TrailerKit.Move(wide, Toward(wide, 0.92f, 4f)));
            // Close on units standing in the dents, a few chosen to show their rings.
            var standing = Of(mine).Concat(Of(theirs)).Where(u => !B.UnitDefs[u.Def].CanFly).ToList();
            var dented = standing.Where(u => ScarMap.GroundOffset(u.Position.x, u.Position.z) < -0.08f).ToList();
            var focus = dented.Count > 0 ? Thick(dented, field) : field;
            var picked = dented.Where(u => u.Player == me).OrderBy(u => (u.Position - focus).sqrMagnitude).Take(6).Select(u => u.Handle).ToArray();
            B.Select(picked, false);
            Root.World.Entities.Selected.Clear();
            Root.World.Entities.Selected.UnionWith(picked);
            CheckCraterUnits(standing.Select(u => u.Handle));
            var close = Sunny(focus, 18f, 24f, "craters close", focus);
            yield return Shot("craters-close", 7f, TrailerKit.Move(close, Toward(close, 0.9f, 4f)));
            B.Select(Array.Empty<int>(), false);
            Root.World.Entities.Selected.Clear();
            EndWatch();
        }

        // ---- Snow ----

        // Cannon on snow: fresh craters dark against it, older ones buried.
        IEnumerator Snow()
        {
            var (a, b) = NearStarts(SnowMap, 70f);
            Root.Options.EffectsQuality = Quality;
            yield return Battle(SnowMap, WeatherChoice.Snow, 60000, Seat.You("ARAMON", a), Seat.Ai("TAROS", 1, b));
            StartWatch("snow");
            int me = PlayerOf(0);
            var guns = Army(me, ("ARACAN", 8), ("ARAPULT", 2), ("TARMAGE", 2));
            yield return FastForward(30);
            var field = FlatGround(Vector3.Lerp(Start(a), MapCentre, 0.3f), 20f, 8f);
            Vector3 stood = default;
            yield return StageBefore(guns, field, 22f, s => stood = s);
            AttackGround(guns, field, 5f, 11);
            yield return FastForward(B.TicksPerSecond * 20);
            Halt(guns);
            // Two minutes on the first craters are under the snow, then fresh ones beside them.
            yield return FastForward(B.TicksPerSecond * 130);
            var fresh = field + (field - stood).normalized * 7f;
            var pose = Sunny(Vector3.Lerp(field, fresh, 0.5f), 30f, 30f, "snow craters", field, fresh);
            yield return Shot("snow-craters", 10f, TrailerKit.Move(pose, Toward(pose, 0.9f, 3f)), f =>
            {
                if (f == 0) AttackGround(guns, fresh, 4f, 13);
            });
            Halt(guns);
            // Fire in the snow.
            var trees = Densest(d => IsTree(d) && d.Flammable, 20f, stood, 90f);
            if (trees.Count > 0)
            {
                var mages = Of(guns).Where(u => B.UnitDefs[u.Def].Name == "TARMAGE").Select(u => u.Handle).ToList();
                for (int i = 0; i < mages.Count; i++) B.Command(GameCommand.To(CommandKind.AttackGround, mages[i], trees.Members[i % trees.Members.Count].Position));
                yield return Until(FeatureEventKind.Burning, 3, B.TicksPerSecond * 40, 10);
                yield return FastForward(B.TicksPerSecond * 4);
                var fp = Sunny(trees.At, 26f, 22f, "snow fire", trees.At);
                yield return Shot("snow-fire", 8f, TrailerKit.Move(fp, Toward(fp, 0.88f, 3f)));
            }
            EndWatch();
        }

        // ---- Measures ----

        // Eight seats, every army placed and sent at the middle, then what
        // destruction costs at High and at Low on a 2560 by 1440 frame.
        IEnumerator Perf(string map)
        {
            foreach (var level in new[] { EffectsQuality.High, EffectsQuality.Low })
                yield return PerfAt(map, level);
        }

        static readonly string[] PerfSides = { "ARAMON", "TAROS", "VERUNA", "ZHON", "CREON", "ARAMON", "TAROS", "VERUNA" };

        static (string, int)[] PerfArmy(string side)
        {
            switch (side)
            {
                case "ARAMON": return new[] { ("ARACAN", 8), ("ARAPULT", 3), ("ARATRE", 2), ("ARAGREN", 4), ("ARASWORD", 8), ("ARAPRIES", 2), ("ARADRAG", 1) };
                case "TAROS": return new[] { ("TARMAGE", 4), ("TARFIRE", 2), ("TARARCH", 8), ("TARBLACK", 6), ("TARWITCH", 2), ("TARDRAG", 1) };
                case "VERUNA": return new[] { ("VERMUSK", 8), ("VERMORT", 3), ("VERBAL", 2), ("VERSWORD", 8), ("VERLIHR", 2), ("VERDRAG", 1) };
                case "ZHON": return new[] { ("ZONGIANT", 3), ("ZONTROLL", 6), ("ZONORC", 8), ("ZONSHAM", 3), ("ZONDRAG", 1) };
                default: return new[] { ("CRETORT", 3), ("CRESHOC", 8), ("CREAUTO", 6), ("CREFIRE", 2), ("CREDRAG", 1) };
            }
        }

        IEnumerator PerfAt(string map, EffectsQuality level)
        {
            var m = FindMap(map);
            int seats = Mathf.Min(8, m.Starts.Length);
            var list = new Seat[seats];
            for (int i = 0; i < seats; i++)
                list[i] = i == 0 ? Seat.You(PerfSides[i], i) : Seat.Ai(PerfSides[i], i % 2, i, AiDifficulty.Normal);
            Root.Options.EffectsQuality = level;
            yield return Battle(map, WeatherChoice.Off, 60000, list);
            StartWatch($"perf {map} {level}");
            var armies = new List<int>();
            for (int i = 0; i < seats; i++)
                foreach (var (u, c) in PerfArmy(PerfSides[i])) armies.AddRange(Place(PlayerOf(i), u, c));
            var middle = MapCentre;
            int obeyed = 0;
            foreach (var u in Of(armies)) if (B.Command(GameCommand.To(CommandKind.Patrol, u.Handle, middle))) obeyed++;
            Note($"perf {map} {level}: {armies.Count} placed for {seats} seats, {obeyed} took the order");
            yield return FastForward(B.TicksPerSecond * 110);
            // Where the most blasts land over twenty seconds, or failing that the thickest crowd.
            int since = LatestBlast();
            yield return FastForward(B.TicksPerSecond * 20);
            var landed = new List<UnitState>();
            int nb;
            while ((nb = B.ReadBlasts(since, blastBuf)) > 0)
            {
                for (int i = 0; i < nb; i++) landed.Add(new UnitState { Position = blastBuf[i].Position });
                since = blastBuf[nb - 1].Id;
                if (nb < blastBuf.Length) break;
            }
            var fighting = landed.Count > 0 ? Thick(landed, middle) : Thick(Units(u => !B.UnitDefs[u.Def].IsBuilding), middle);
            Note($"perf {map} {level}: {landed.Count} blasts in twenty seconds, filming at {fighting}");
            var pose = new ShotPose(fighting, 55f, 42f, SunYaw);
            Pose(pose);
            yield return null;
            var cam = Cam;
            var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.DefaultHDR) { name = "Perf frame" };
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.enabled = false;
            var names = new List<string>();
            UnityEngine.Profiling.Sampler.GetNames(names);
            var recs = new List<(string, UnityEngine.Profiling.Recorder)>();
            foreach (var n in names)
                if (n == "DrawOpaqueObjects" || n == "DrawTransparentObjects")
                {
                    var r = UnityEngine.Profiling.Recorder.Get(n);
                    r.enabled = true;
                    recs.Add((n, r));
                }
            var cpu = new List<double>();
            var breakMs = new List<double>();
            var scarMs = new List<double>();
            var fireMs = new List<double>();
            var fxMs = new List<double>();
            var gpuOn = new List<double>();
            var gpuOff = new List<double>();
            var gpuAll = new List<double>();
            var passOn = new double[recs.Count];
            var passOff = new double[recs.Count];
            int frames = 540, mostChunks = 0, mostDrawn = 0, mostCalls = 0, mostBurning = 0, mostParticles = 0, mostLights = 0, mostAll = 0;
            var ents = Root.World.Entities;
            var fx = Root.World.Effects;
            double worstScar = 0;
            ScarMap.Current?.ResetWorst();
            try
            {
                for (int f = 0; f < frames && Playing; f++)
                {
                    // Chunks as the setting caps them, none, and every one in view with its shadow.
                    bool on = f % 3 != 1, capped = f % 3 == 0;
                    ents.DrawBreaking = on;
                    Debris.Capped = capped;
                    yield return null;
                    WatchFrame();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    cam.Render();
                    AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (f < 30) continue;
                    if (on && !capped) { gpuAll.Add(ms); mostAll = Mathf.Max(mostAll, ents.Debris.Drawn); continue; }
                    (on ? gpuOn : gpuOff).Add(ms);
                    for (int k = 0; k < recs.Count; k++) (on ? passOn : passOff)[k] += recs[k].Item2.gpuElapsedNanoseconds / 1e6;
                    if (!on) continue;
                    double scar = ScarMap.Current != null ? ScarMap.Current.LastMs : 0;
                    double fire = Root.World.Fire.Ms + Root.World.Magic.Ms;
                    breakMs.Add(ents.BreakMs);
                    scarMs.Add(scar);
                    fireMs.Add(fire);
                    fxMs.Add(fx.BlastMs);
                    cpu.Add(ents.BreakMs + scar + fire + fx.BlastMs);
                    worstScar = Math.Max(worstScar, scar);
                    mostChunks = Mathf.Max(mostChunks, ents.Debris.Active);
                    mostDrawn = Mathf.Max(mostDrawn, ents.Debris.Drawn);
                    mostCalls = Mathf.Max(mostCalls, ents.ChunkDrawCalls);
                    mostBurning = Mathf.Max(mostBurning, Root.World.Fire.Burning);
                    mostParticles = Mathf.Max(mostParticles, fx.ParticleCount);
                    mostLights = Mathf.Max(mostLights, fx.LightsLit);
                }
            }
            finally
            {
                ents.DrawBreaking = true;
                Debris.Capped = true;
                cam.targetTexture = old;
                cam.enabled = true;
                rt.Release();
                Object.Destroy(rt);
            }
            int half = Mathf.Max(1, gpuOn.Count);
            var passes = string.Join(", ", recs.Select((r, k) => $"{r.Item1} {passOn[k] / half:0.00}/{passOff[k] / Mathf.Max(1, gpuOff.Count):0.00}"));
            var s = ScarMap.Current;
            string line = $"PERF {map} {level}: {Units().Count} units; destruction main thread median {Median(cpu):0.00} ms, 95th {Pct(cpu, 0.95):0.00}, worst {Pct(cpu, 1f):0.00} " +
                          $"(breaking {Median(breakMs):0.00}/{Pct(breakMs, 0.95):0.00}, scars {Median(scarMs):0.00}/{Pct(scarMs, 0.95):0.00} worst {worstScar:0.00} [{s?.WorstParts}], fire and magic {Median(fireMs):0.00}/{Pct(fireMs, 0.95):0.00}, effects {Median(fxMs):0.00}/{Pct(fxMs, 0.95):0.00}); " +
                          $"2560x1440 frame drawn and waited for: {Median(gpuOn):0.00} ms with chunks, {Median(gpuOff):0.00} without, {Median(gpuOn) - Median(gpuOff):0.00} for them, " +
                          $"every chunk in view with its shadow as before the cap {Median(gpuAll):0.00} ({Median(gpuAll) - Median(gpuOff):0.00} for {mostAll} chunks); passes with/without {passes}; " +
                          $"chunks at most {mostChunks} in play, {mostDrawn} drawn in {mostCalls} calls; {mostBurning} burning, {mostParticles} particles, {mostLights} lights; {s?.Stamped} stamps";
            Note(line);
            File.AppendAllLines(Path.Combine(OutDir, "perf.log"), new[] { line });
            EndWatch();
        }

        static double Median(List<double> v) => Pct(v, 0.5f);

        static double Pct(List<double> v, double p)
        {
            if (v.Count == 0) return 0;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp((int)Math.Ceiling(p * s.Count) - 1, 0, s.Count - 1)];
        }
    }
}
