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
            ("siege-ruins", d => d.SiegeRuins()),
            ("creon-ruins", d => d.CreonRuins()),
            ("spells", d => d.Spells()),
            ("dragon-breath", d => d.DragonBreath()),
            ("craters", d => d.Craters()),
            ("rain", d => d.ForestFire(RainMap, WeatherChoice.Rain, "rain")),
            ("snow", d => d.Snow()),
            ("perf-ulasem", d => d.Perf("ulasem arena")),
            ("perf-forest", d => d.Perf(ForestMap)),
        };

        // The maps, chosen from the census.
        public const string ForestMap = "into the woods", RainMap = "evergreen ridge", SiegeMap = "walls of carin vel",
            CreonMap = "lieber's project", SpellMap = "evergreen ridge", CraterMap = "edmont's field", SnowMap = "icy peaks";

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

        public int NearestStart(Vector3 at)
        {
            int best = 0;
            for (int i = 1; i < Map.Starts.Length; i++)
                if ((Start(i) - at).sqrMagnitude < (Start(best) - at).sqrMagnitude) best = i;
            return best;
        }

        // A skirmish on map with the local seat at the start nearest the
        // densest scenery of a kind, which `found` is given.
        IEnumerator BattleNear(string map, WeatherChoice weather, string side, string foe, Func<FeatureDef, bool> keep, float cell, Action<Patch> found)
        {
            Root.Options.EffectsQuality = Quality;
            yield return Battle(map, weather, 60000, Seat.You(side, 0), Seat.Ai(foe, 1, FarStart(map, 0)));
            var p = Densest(keep, cell, MapCentre, 100000f);
            int s = NearestStart(p.At);
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
            var a = B.SelectionActions().FirstOrDefault(x => x.Kind == ActionKind.Spell && x.Arg == slot);
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
            Patch forest = default;
            yield return BattleNear(map, weather, "TAROS", "ARAMON", d => IsTree(d) && d.Flammable, 32f, p => forest = p);
            StartWatch(name);
            // The wood at peace before anyone comes.
            var calm = Sunny(forest.At, 46f, 16f, name + " calm", forest.At);
            yield return Shot($"{name}-calm", 6f, TrailerKit.Move(calm, Toward(calm, 0.9f, 3f)));
            int me = PlayerOf(0);
            var army = Army(me, ("TARMAGE", 4), ("TARFIRE", 2), ("TARARCH", 6));
            yield return FastForward(30);
            var wind = Wind(out float strength);
            Note($"{name}: wind toward {wind} at {strength:0.00}, forest of {forest.Count} at {forest.At}");
            // Lit on its upwind edge, so the fire runs on through it.
            var light = Ground(forest.At.x - wind.x * 8f, forest.At.z - wind.z * 8f);
            Vector3 stood = default;
            yield return StageBefore(army, light, 20f, s => stood = s);
            var catchPose = Sunny(Vector3.Lerp(light, stood, 0.25f), 34f, 22f, name + " catch", light, stood);
            AttackGround(army, light, 3f);
            // The shot opens on the first volley.
            for (int t = 0; t < B.TicksPerSecond * 30 && Seen(FeatureEventKind.Burning) == 0; t += 5) yield return FastForward(5, 5);
            yield return Shot($"{name}-catch", 8f, TrailerKit.Move(catchPose, Toward(catchPose, 0.85f, 4f)));
            // More of the wood lit at once, so it burns wide even where fire does not spread on its own.
            var stand = forest.Members.OrderBy(f => (f.Position - stood).sqrMagnitude).ToList();
            for (int i = 0; i < army.Count && stand.Count > 0; i++)
                B.Command(GameCommand.To(CommandKind.AttackGround, army[i], stand[(i * 3) % stand.Count].Position));
            Note($"{name}: {Seen(FeatureEventKind.Burning)} caught after the first shot, {Root.World.Fire.Burning} burning");
            // It spreads on its own.
            yield return FastForward(B.TicksPerSecond * 18);
            var burning = new List<Vector3>();
            Root.World.Fire.BurningPlaces(burning);
            var front = burning.Count > 0 ? Ground(burning.Average(p => p.x), burning.Average(p => p.z)) : forest.At;
            var across = Across(wind);
            var widePose = BestPose(front, 62f, 34f, across, 6f, true, name + " spread", front);
            yield return Shot($"{name}-spread", 10f, TrailerKit.Move(widePose, Toward(widePose, 0.9f, 3f)));
            Halt(army);
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
            yield return BattleNear(SiegeMap, WeatherChoice.Off, "ARAMON", "TAROS", d => IsWall(d) || IsHouse(d), 40f, p => walls = p);
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
            // The walls standing, before the guns come up.
            var calm = Sunny(walls.At, 48f, 18f, "siege calm", walls.At);
            yield return Shot("siege-calm", 6f, TrailerKit.Move(calm, Toward(calm, 0.9f, 3f)));
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

        // The whole village under the guns at once, then its ruins from above
        // and the smoke drifting off them.
        IEnumerator SiegeRuins()
        {
            Patch village = default;
            yield return BattleNear(SiegeMap, WeatherChoice.Off, "ARAMON", "TAROS", d => IsWall(d) || IsHouse(d), 40f, p => village = p);
            StartWatch("siege-ruins");
            if (village.Count == 0) { Note("siege-ruins: no village"); EndWatch(); yield break; }
            int me = PlayerOf(0);
            var army = Army(me, ("ARAPULT", 3), ("ARATRE", 2), ("ARACAN", 6), ("VERMORT", 2), ("VERBAL", 2));
            yield return FastForward(30);
            Vector3 stood = default;
            yield return StageBefore(army, village.At, 30f, s => stood = s);
            var homes = Densest(IsHouse, 48f, village.At, 90f).Members.Select(f => f.Position).ToList();
            if (homes.Count == 0) homes.Add(village.At);
            Note($"siege-ruins: {homes.Count} houses under the guns");
            for (int i = 0; i < army.Count; i++)
                B.Command(GameCommand.To(CommandKind.AttackGround, army[i], homes[i % homes.Count]));
            var bpose = Sunny(village.At, 34f, 22f, "siege barrage", village.At);
            yield return Shot("siege-barrage", 10f, TrailerKit.Move(bpose, Toward(bpose, 0.88f, 3f)));
            yield return FastForward(B.TicksPerSecond * 20);
            var rpose = Sunny(village.At, 56f, 32f, "siege ruins", village.At);
            yield return Shot("siege-ruins", 9f, TrailerKit.Move(rpose, Toward(rpose, 0.9f, 3f)));
            Halt(army);
            yield return FastForward(B.TicksPerSecond * 6);
            var apose = Sunny(village.At, 40f, 24f, "siege smoke", village.At);
            yield return Shot("siege-smoke", 7f, TrailerKit.Move(apose, Toward(apose, 0.92f, -3f)));
            EndWatch();
        }

        // ---- Creon's town ----

        // A Creon town falls, most of its stages drawn from what stood before
        // them until their own models are made.
        IEnumerator CreonRuins()
        {
            Patch town = default;
            yield return BattleNear(CreonMap, WeatherChoice.Off, "CREON", "TAROS", IsCreon, 28f, p => town = p);
            StartWatch("creon-ruins");
            if (town.Count == 0) { Note("creon-ruins: no Creon scenery"); EndWatch(); yield break; }
            int me = PlayerOf(0);
            var army = Army(me, ("CRETORT", 3), ("ARACAN", 8), ("ARAPULT", 2), ("CREFIRE", 2));
            yield return FastForward(30);
            Vector3 stood = default;
            yield return StageBefore(army, town.At, 26f, s => stood = s);
            var pose = Sunny(town.At, 36f, 24f, "creon town", town.At);
            // Each gun on its own building, house, tree or fence.
            var targets = town.Members.OrderBy(f => (f.Position - stood).sqrMagnitude).Take(army.Count).ToList();
            for (int i = 0; i < army.Count && targets.Count > 0; i++)
                B.Command(GameCommand.To(CommandKind.AttackGround, army[i], targets[i % targets.Count].Position));
            yield return Shot("creon-ruins", 12f, TrailerKit.Move(pose, Toward(pose, 0.88f, 4f)));
            yield return FastForward(B.TicksPerSecond * 20);
            var after = Sunny(town.At, 26f, 30f, "creon after", town.At);
            yield return Shot("creon-after", 6f, TrailerKit.Move(after, Toward(after, 0.9f, -3f)));
            Halt(army);
            EndWatch();
        }

        // ---- Magic ----

        static readonly (BlastKind kind, string name, string[] prefer)[] SpellKinds =
        {
            (BlastKind.Frost, "frost", new[] { "ARAPRIES", "TARWITCH", "CREGOD" }),
            (BlastKind.Dark, "dark", new[] { "TARGOD", "TARLICH" }),
            (BlastKind.Lightning, "lightning", new[] { "ZONSHAM", "TARWITCH", "ARAKING", "CREPRIS" }),
            (BlastKind.Earth, "earth", new[] { "ARAGOD", "ARAPRIES", "CREGOD" }),
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
                var (def, slot) = Caster(kind, prefer);
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
                int before = Root.World.Magic.Marked[(int)kind];
                bool cast = false;
                yield return Shot($"spell-{name}", 7f, TrailerKit.Move(pose, Toward(pose, 0.9f, 3f)), f =>
                {
                    if (f == 0) cast = Cast(unit, slot, at);
                });
                int marked = Root.World.Magic.Marked[(int)kind] - before;
                Note($"spell {name}: cast {cast}, mana {Of(new[] { unit }).FirstOrDefault().Mana}, {marked} pieces of scenery marked");
                if (marked == 0) problems.Add($"{name} magic marked no scenery");
            }
            EndWatch();
        }

        // ---- Dragons ----

        IEnumerator DragonBreath()
        {
            Patch wood = default;
            yield return BattleNear(ForestMap, WeatherChoice.Off, "TAROS", "VERUNA", d => IsTree(d) && d.Flammable, 20f, p => wood = p);
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
                AttackGround(mages, trees.At, 3f);
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
            yield return FastForward(B.TicksPerSecond * 75);
            // Where the fighting is thickest.
            var fighting = Thick(Units(u => !B.UnitDefs[u.Def].IsBuilding), middle);
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
                if (n == "DrawOpaqueObjects" || n == "DrawTransparentObjects" || n.Contains("Shadow") || n == "DepthPrepass" || n == "DepthNormalPrepass")
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
            var passOn = new double[recs.Count];
            var passOff = new double[recs.Count];
            int frames = 360, mostChunks = 0, mostDrawn = 0, mostCalls = 0, mostBurning = 0, mostParticles = 0, mostLights = 0;
            var ents = Root.World.Entities;
            var fx = Root.World.Effects;
            double worstScar = 0;
            ScarMap.Current?.ResetWorst();
            try
            {
                for (int f = 0; f < frames && Playing; f++)
                {
                    bool on = f % 2 == 0;
                    ents.DrawBreaking = on;
                    yield return null;
                    WatchFrame();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    cam.Render();
                    AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (f < 30) continue;
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
                          $"2560x1440 frame drawn and waited for: {Median(gpuOn):0.00} ms with chunks, {Median(gpuOff):0.00} without, {Median(gpuOn) - Median(gpuOff):0.00} for them; passes with/without {passes}; " +
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
