// MockDestruction.cs - the mock's side of destruction as the engine will
// report it: a blast for every shot, spell and death that bursts, pieces
// thrown by dying units, and with SceneryBreaks on the original's rules for
// scenery (hit points, dead stages, fire that spreads downwind) and its wind.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        // Blasts hurt, kill and burn scenery by the original's rules, and the
        // wind blows. Off by default, as the engine has neither yet.
        public bool SceneryBreaks;
        // The chance a flammable feature catches from a neighbour's sparks.
        public float SpreadChance = 0.4f;
        // How many of each kind of event the rings keep.
        public const int RingSize = 1024;
        // The original's wind when a map gives none (legacy:168998-169000).
        public const float WindMin = 100f, WindMax = 2000f;

        readonly List<BlastEvent> blastRing = new List<BlastEvent>();
        readonly List<FeatureEvent> featureRing = new List<FeatureEvent>();
        readonly List<PieceEvent> pieceRing = new List<PieceEvent>();
        int lastBlast, lastFeatureEvent, lastPieceEvent;

        void ForgetDestruction()
        {
            blastRing.Clear();
            featureRing.Clear();
            pieceRing.Clear();
            windRng = null;
            windHeld = false;
        }

        // ── The rings ─────────────────────────────────────────────────

        public int ReadBlasts(int since, BlastEvent[] into) => ReadRing(blastRing, BlastId, since, into);
        public int ReadFeatureEvents(int since, FeatureEvent[] into) => ReadRing(featureRing, FeatureEventId, since, into);
        public int ReadPieceEvents(int since, PieceEvent[] into) => ReadRing(pieceRing, PieceEventId, since, into);

        static readonly System.Func<BlastEvent, int> BlastId = e => e.Id;
        static readonly System.Func<FeatureEvent, int> FeatureEventId = e => e.Id;
        static readonly System.Func<PieceEvent, int> PieceEventId = e => e.Id;

        static int ReadRing<T>(List<T> ring, System.Func<T, int> id, int since, T[] into)
        {
            int cap = into?.Length ?? 0, lo = 0, hi = ring.Count, n = 0;
            // The ring runs in id order.
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (id(ring[mid]) <= since) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < ring.Count && n < cap; i++) into[n++] = ring[i];
            return n;
        }

        static void Push<T>(List<T> ring, T e)
        {
            ring.Add(e);
            if (ring.Count > RingSize + RingSize / 4) ring.RemoveRange(0, ring.Count - RingSize);
        }

        // ── Weapons ───────────────────────────────────────────────────

        static WeaponInfo Gun(string type, string subtype, string damageKind, string explosion, string water, int areaPx, int damage,
            WeaponFlags flags = WeaponFlags.None) =>
            new WeaponInfo
            {
                Def = -1, Slot = WeaponSlot.None, Type = type, Subtype = subtype, DamageKind = damageKind, ExplosionClass = explosion,
                WaterExplosionClass = water, AreaOfEffect = areaPx / 16f, Damage = damage, Flags = flags,
            };

        const string SmallWater = "small water explosion", MediumWater = "medium water explosion";
        const WeaponFlags Fire = WeaponFlags.FireStarter, Spell = WeaponFlags.Spell, Only = WeaponFlags.UnitsOnly;

        // Each mock weapon as the engine's data would describe its kind, in
        // the data's own words, with round numbers of the mock's.
        static readonly Dictionary<string, WeaponInfo> WeaponData = Weapons(
            ("ARAKING 1", Gun("line of sight", "lightning", "", "lightning explosion", "", 0, 900)),
            ("ARAKING 2", Gun("line of sight", "", "", "fireball explosion", MediumWater, 100, 2000, Spell)),
            ("ARAKING 3", Gun("remote effect", "", "", "", "", 500, 5000, Only | Spell)),
            ("TARNECRO 1", Gun("line of sight", "", "", "flamestrike", "", 100, 800, Fire)),
            ("TARNECRO 2", Gun("guided", "", "", "volcblast", "", 150, 1000, Fire | Spell)),
            ("TARNECRO 3", Gun("remote effect", "", "", "", "", 500, 5000, Only | Spell)),
            ("VERMAGE 1", Gun("line of sight", "", "", "water splash", "", 100, 1500)),
            ("VERMAGE 2", Gun("guided", "", "", "waterballexp", "", 50, 2000, Spell)),
            ("ZONHUNT 1", Gun("line of sight", "lightning", "", "lightning explosion", "", 0, 1000)),
            ("ZONHUNT 2", Gun("guided", "", "", "blue_shockring", "", 60, 1400, Spell)),
            ("TARPRIES 1", Gun("line of sight", "lightning", "", "lightning explosion", "", 0, 750)),
            ("TARPRIES 2", Gun("guided", "", "", "blue_shockring", "", 50, 900, Fire | Spell)),
            ("TARPRIES 3", Gun("guided", "", "", "fireball explosion", "", 80, 800, Fire | Spell)),
            ("VERDRAG 2", Gun("guided", "", "", "tsunamiexp", "", 50, 800, Spell)),
            ("CRECHIE 1", Gun("line of sight", "lightning", "", "", "", 40, 1000, Spell)),
            ("CRECHIE 2", Gun("line of sight", "turntofrozen", "", "lightning explosion", "", 0, 1, Spell)),
            ("CRECHIE 3", Gun("line of sight", "lightning", "paralyzer", "", "", 0, 1000, Spell)),
            ("CREPRIS 1", Gun("line of sight", "lightning", "", "", "", 0, 1000)),
            ("ZONSHAM 1", Gun("line of sight", "lightning", "", "lightning explosion", "", 0, 600)),
            ("ARABOW 1", Gun("ballistic", "", "", "", SmallWater, 0, 450)),
            ("ARABOW 2", Gun("guided", "", "", "teeny explosion", SmallWater, 0, 550, Spell)),
            ("TARARCH 1", Gun("ballistic", "", "", "teeny explosion", SmallWater, 0, 350, Fire)),
            ("VERARCH 1", Gun("line of sight", "", "", "", SmallWater, 0, 300)),
            ("CREGATL 1", Gun("ballistic", "", "", "medium dust puff", SmallWater, 24, 300)),
            ("VERKNIGH 1", Gun("ballistic", "", "", "small dust puff", SmallWater, 20, 300)),
            ("ZONTER 1", Gun("ballistic", "", "", "", SmallWater, 0, 270)),
            ("ZONGIANT 1", Gun("ballistic", "", "explosion", "large dust puff", MediumWater, 32, 1250)),
            ("ARACAN 1", Gun("ballistic", "", "explosion", "large explosion", MediumWater, 90, 2000)),
            ("VERMUSK 1", Gun("ballistic", "", "", "teeny explosion", SmallWater, 0, 700)),
            ("ARAPULT 1", Gun("ballistic", "", "explosion", "large dust puff", MediumWater, 100, 1250)),
            ("VERPULT 1", Gun("ballistic", "", "explosion", "large dust puff", MediumWater, 50, 1500)),
            ("TARMAGE 3", Gun("remote effect", "hailstorm", "", "fireball explosion", "", 200, 300, Fire | Spell)),
            ("ARAPRIES 2", Gun("remote effect", "hailstorm", "", "iceballexp", MediumWater, 200, 70, Spell)),
            ("ARAPRIES 3", Gun("line of sight", "turntostone", "", "blue_shockring", "", 0, 1, Spell)),
            ("ARADRAG 1", Gun("line of sight", "fire", "fire", "", "", 50, 850, Fire)),
            ("ARADRAG 2", Gun("guided", "", "", "fireball explosion", "", 45, 850, Fire | Spell)),
            ("TARKNIGH 1", Gun("line of sight", "fire", "fire", "", "", 0, 800, Fire)),
            ("TARHEL 1", Gun("line of sight", "", "", "flamestrike", "", 100, 400, Fire)),
            ("TARDRAG 2", Gun("guided", "", "", "fireball explosion", "", 60, 800, Fire | Spell)),
            ("TARMIND 1", Gun("line of sight", "mindcontrol", "", "mind control", "", 0, 1, Only | Spell)),
            ("TARWITCH 1", Gun("wandering", "", "", "", "", 30, 50, Spell)),
            ("ZONSPIDE 1", Gun("line of sight", "", "paralyzer", "green_shockring", "", 0, 200)),
            ("MOCK ARROW", Gun("ballistic", "", "", "", SmallWater, 0, 200)),
            ("MOCK FIREBALL", Gun("ballistic", "", "", "teeny explosion", "", 24, 300, Fire)),
            ("MOCK FIREBALL SPELL", Gun("guided", "", "", "fireball explosion", "", 64, 600, Fire | Spell)),
            ("MOCK FROST SPELL", Gun("line of sight", "", "", "iceballexp", "", 24, 400, Spell)),
            ("MOCK SWORD", Gun("melee", "", "", "", "", 0, 180)),
            ("MOCK DEATH BLAST", Gun("", "", "explosion", "large explosion", MediumWater, 96, 1000)));

        static Dictionary<string, WeaponInfo> Weapons(params (string name, WeaponInfo w)[] rows)
        {
            var d = new Dictionary<string, WeaponInfo>();
            foreach (var (name, w) in rows)
            {
                w.Name = name;
                d[name] = w;
            }
            return d;
        }

        static WeaponInfo Copy(WeaponInfo w) => new WeaponInfo
        {
            Def = w.Def, Slot = w.Slot, Name = w.Name, Type = w.Type, Subtype = w.Subtype, DamageKind = w.DamageKind,
            ExplosionClass = w.ExplosionClass, WaterExplosionClass = w.WaterExplosionClass, AreaOfEffect = w.AreaOfEffect,
            Damage = w.Damage, Flags = w.Flags, Light = w.Light, Shake = w.Shake, ShakeSeconds = w.ShakeSeconds,
        };

        static WeaponInfo Describe(FxWeapon fx)
        {
            var w = WeaponData.TryGetValue(fx.Name, out var data) ? Copy(data) : new WeaponInfo { Name = fx.Name, Def = -1, Slot = WeaponSlot.None };
            w.Light = fx.Light;
            return w;
        }

        readonly Dictionary<(int def, int slot), WeaponInfo> unitWeapons = new Dictionary<(int def, int slot), WeaponInfo>();

        public WeaponInfo Weapon(int def, int slot)
        {
            if (def < 0 || def >= unitDefs.Count) return null;
            if (unitWeapons.TryGetValue((def, slot), out var known)) return known;
            WeaponInfo w = null;
            string name = WeaponName(def, slot);
            if (name != null && WeaponData.TryGetValue(name, out var data))
            {
                w = Copy(data);
                w.Def = def;
                w.Slot = slot;
                w.Light = FxWeaponNamed(name)?.Light ?? FxLight.None;
            }
            unitWeapons[(def, slot)] = w;
            return w;
        }

        // The mock's fighters: an archer's arrow, a mage's fireball and two
        // spells, a knight's or flyer's blow, and a death blast for
        // buildings and monarchs.
        string WeaponName(int def, int slot)
        {
            var role = RoleOf(def);
            if (slot == WeaponSlot.Death) return unitDefs[def].IsBuilding || role == Role.Monarch ? "MOCK DEATH BLAST" : null;
            switch (role)
            {
                case Role.Archer: return slot == 0 ? "MOCK ARROW" : null;
                case Role.Mage: return slot == 0 ? "MOCK FIREBALL" : slot == 1 ? "MOCK FIREBALL SPELL" : slot == 2 ? "MOCK FROST SPELL" : null;
                case Role.Knight: case Role.Flyer: return slot == 0 ? "MOCK SWORD" : null;
                default: return null;
            }
        }

        // ── Blasts ────────────────────────────────────────────────────

        void Burst(FxShot s, Vector3 at, Vector3 dir, int struck) =>
            Burst(BlastCause.Weapon, (s.Slot != WeaponSlot.None ? Weapon(s.Def, s.Slot) : null) ?? s.W.Info,
                s.Def, s.Slot, s.Shooter, s.Player, at, dir, struck);

        void Burst(Arrow a, Vector3 at, int struck) =>
            Burst(BlastCause.Weapon, Weapon(a.Def, 0) ?? (a.Look ?? FxWeaponNamed("MOCK ARROW")).Info,
                a.Def, 0, a.From, IdOf(a.Player), at, a.Vel, struck);

        // A blast reported where it burst, and with SceneryBreaks run over the
        // scenery in reach. Units take what the mock always gave them.
        int Burst(BlastCause cause, WeaponInfo w, int def, int slot, int shooter, int player, Vector3 at, Vector3 dir, int struck)
        {
            float area = w?.AreaOfEffect ?? 0f;
            var flags = BlastFlags.None;
            if (w != null && (w.Flags & WeaponFlags.FireStarter) != 0) flags |= BlastFlags.FireStarter;
            if (w != null && (w.Flags & WeaponFlags.UnitsOnly) != 0) flags |= BlastFlags.UnitsOnly;
            if (struck < 0 && Terrain != null && Terrain.SeaLevel >= 0f && GroundHeight(at.x, at.z) < Terrain.SeaLevel) flags |= BlastFlags.Water;
            // The original's direct hit: a unit struck by a shot of under 17 pixels of area.
            if (struck >= 0 && area * 16f < 17f) flags |= BlastFlags.DirectHit;
            if (!InSight(at)) flags |= BlastFlags.OutOfSight;
            var b = new BlastEvent
            {
                Id = ++lastBlast, Tick = Tick, Cause = cause, Def = def, Slot = slot, Weapon = w, Player = player,
                Shooter = shooter >= 0 && byHandle.ContainsKey(shooter) ? shooter : -1,
                Position = at, Direction = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero,
                Radius = area * 0.5f, Damage = w?.Damage ?? 0, Flags = flags, Unit = struck,
                Feature = struck >= 0 ? -1 : FeatureAt(at),
            };
            Push(blastRing, b);
            if (SceneryBreaks) HitScenery(b);
            return b.Id;
        }

        // Whether the local player sees a point now, as ReadFog's clear.
        bool InSight(Vector3 p)
        {
            if (Terrain == null) return false;
            if (SeesAll || setup != null && setup.MapRevealed) return true;
            int w = Terrain.HeightsW, h = Terrain.HeightsH;
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x / Terrain.CellSize), 0, w - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(-p.z / Terrain.CellSize), 0, h - 1);
            int i = y * w + x;
            if (sight != null && i < sight.Length && sight[i]) return true;
            // With line of sight off, ground once seen stays clear.
            return setup != null && !setup.LineOfSight && i < fogSeen.Length && fogSeen[i] != 0;
        }

        // ── Deaths ────────────────────────────────────────────────────

        // The pieces a dying unit's script would throw, by piece name.
        static readonly Dictionary<string, PieceExplode> Thrown = new Dictionary<string, PieceExplode>
        {
            ["head"] = PieceExplode.Fall | PieceExplode.Smoke,
            ["rarm"] = PieceExplode.Fall,
            ["roof"] = PieceExplode.Shatter,
            ["flag"] = PieceExplode.Fall | PieceExplode.Fire | PieceExplode.Smoke,
            ["wingl2"] = PieceExplode.Fall | PieceExplode.Smoke,
            ["wingr2"] = PieceExplode.Fall,
            ["mast"] = PieceExplode.Fall | PieceExplode.Fire,
        };

        PiecePose[] thrownPose = new PiecePose[64];

        void Died(Unit u)
        {
            var m = GetModel(u.Model);
            if (m != null)
            {
                if (thrownPose.Length < m.Pieces.Length) thrownPose = new PiecePose[m.Pieces.Length];
                int n = Mathf.Min(ReadUnitPose(u.Handle, thrownPose), m.Pieces.Length);
                bool unseen = !InSight(u.Pos);
                for (int i = 0; i < n; i++)
                    if (Thrown.TryGetValue(m.Pieces[i].Name, out var how))
                        Push(pieceRing, new PieceEvent
                        {
                            Id = ++lastPieceEvent, Tick = Tick, Unit = u.Handle, Def = u.Def, Player = IdOf(u.Player), Model = u.Model,
                            Piece = i, How = how, Pose = thrownPose[i].Matrix, OutOfSight = unseen,
                        });
            }
            var death = Weapon(u.Def, WeaponSlot.Death);
            if (death != null) Burst(BlastCause.Death, death, u.Def, WeaponSlot.Death, u.Handle, IdOf(u.Player), GroundAt(u.Pos), Vector3.zero, -1);
        }

        // ── Scenery ───────────────────────────────────────────────────

        // Each feature def's death animation and burn, in ticks.
        readonly List<(int die, int burn)> featureTimes = new List<(int die, int burn)>();

        void FeatureKind(string name, string obj, string seq, string category, Vector2Int footprint, float height, int hitPoints,
            bool flammable = false, int dead = -1, int burnt = -1, int die = 0, int burn = 0)
        {
            featureDefs.Add(new FeatureDef
            {
                Id = featureDefs.Count, Name = name, ObjectName = obj, SequenceName = seq, Category = category, Footprint = footprint,
                Height = height, HitPoints = hitPoints, Indestructible = hitPoints <= 0, Flammable = flammable, DeadDef = dead, BurntDef = burnt,
            });
            featureTimes.Add((die, burn));
        }

        // The first three keep their ids. The rest are the stages scenery
        // breaks into, and a wall, a hut and a body turned to stone.
        void AddFeatureDefs()
        {
            var one = new Vector2Int(1, 1);
            FeatureKind("mock_tree", "mocktree", "", "trees", one, 3f, 400, true, dead: 3, burnt: 4, die: 45, burn: 240);
            FeatureKind("mock_rock", "", "mockrock", "rocks", new Vector2Int(2, 2), 1.5f, 0);
            FeatureKind("mock_bush", "", "mockbush", "plants", one, 1f, 200, true, die: 20, burn: 120);
            FeatureKind("mock_tree_dead", "mocktreedead", "", "trees", one, 2.4f, 300, true, dead: 5, burnt: 4, die: 30, burn: 180);
            FeatureKind("mock_tree_burnt", "mocktreeburnt", "", "trees", one, 1.5f, 300, dead: 5, die: 30);
            FeatureKind("mock_stump", "mockstump", "", "trees", one, 0.3f, 0);
            FeatureKind("mock_wall", "mockwall", "", "walls", new Vector2Int(4, 1), 2f, 6000, dead: 7, die: 30);
            FeatureKind("mock_wall_a", "mockwalla", "", "walls", new Vector2Int(4, 1), 1f, 4000, dead: 8, die: 30);
            FeatureKind("mock_rubble", "mockrubble", "", "walls", new Vector2Int(4, 1), 0.4f, 0);
            FeatureKind("mock_hut", "mockhut", "", "huts", new Vector2Int(2, 2), 1.8f, 800, true, dead: 10, burnt: 10, die: 45, burn: 360);
            FeatureKind("mock_hut_wreck", "mockhutwreck", "", "huts", new Vector2Int(2, 2), 0.5f, 0);
            FeatureKind("mock_stone_body", "mockstonebody", "", "corpses", new Vector2Int(1, 2), 0.4f, 300, die: 15);
        }

        void LookOf(int def, out int model, out int sprite)
        {
            var d = featureDefs[def];
            model = string.IsNullOrEmpty(d.ObjectName) ? -1 : LoadModel(d.ObjectName, 0);
            sprite = model >= 0 ? -1 : d.SequenceName == "mockbush" ? 1 : 0;
        }

        // The squared distance from a point to a feature's footprint, 0 on it.
        float Reach(Feature f, Vector3 p)
        {
            var d = featureDefs[f.Def];
            float cell = Terrain != null ? Terrain.CellSize : 1f;
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - f.Pos.x) - Mathf.Max(1, d.Footprint.x) * 0.5f * cell);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - f.Pos.z) - Mathf.Max(1, d.Footprint.y) * 0.5f * cell);
            return dx * dx + dz * dz;
        }

        int FeatureAt(Vector3 p)
        {
            for (int i = 0; i < features.Count; i++) if (Reach(features[i], p) <= 0f) return i;
            return -1;
        }

        // The original's pass: every feature in reach, or under the blast,
        // takes its full damage once. A fire starter sets flammable ones
        // burning instead.
        void HitScenery(in BlastEvent b)
        {
            if ((b.Flags & (BlastFlags.UnitsOnly | BlastFlags.DirectHit)) != 0) return;
            float r2 = b.Radius * b.Radius;
            for (int i = 0; i < features.Count; i++)
                if (Reach(features[i], b.Position) <= r2) HitFeature(i, features[i], b);
        }

        void HitFeature(int index, Feature f, in BlastEvent b)
        {
            var d = featureDefs[f.Def];
            if (f.DyingLeft > 0) return;
            if (!d.Breakable) { NoteFeature(FeatureEventKind.Hit, index, f, -1, b.Id, b.Position); return; }
            if ((b.Flags & BlastFlags.FireStarter) != 0 && d.Flammable)
            {
                if (f.BurnLeft <= 0) Ignite(index, f, b.Id, b.Position);
                return;
            }
            f.Damage += b.Damage;
            int left = Mathf.Max(0, d.HitPoints - f.Damage);
            NoteFeature(FeatureEventKind.Hit, index, f, -1, b.Id, b.Position, b.Damage, left);
            if (left > 0) return;
            f.BurnLeft = 0;
            f.DyingLeft = Mathf.Max(1, featureTimes[f.Def].die);
            f.Blast = b.Id;
            f.From = b.Position;
            NoteFeature(FeatureEventKind.Dying, index, f, -1, b.Id, b.Position, ticks: f.DyingLeft);
        }

        void Ignite(int index, Feature f, int blast, Vector3 from)
        {
            f.BurnLeft = Mathf.Max(1, featureTimes[f.Def].burn);
            // It throws sparks once, 2.5 to 5 seconds after it catches.
            f.SparkIn = Mathf.Min(f.BurnLeft, Mathf.RoundToInt(Tps * (2.5f + 2.5f * Hash01(Seed(f) + (int)Tick))));
            f.Blast = blast;
            f.From = from;
            NoteFeature(FeatureEventKind.Burning, index, f, -1, blast, from, ticks: f.BurnLeft);
        }

        static int Seed(Feature f) => Mathf.RoundToInt(f.Pos.x * 64f) * 73856093 ^ Mathf.RoundToInt(f.Pos.z * 64f) * 19349663;

        void StepScenery()
        {
            if (!SceneryBreaks) return;
            StepWind();
            for (int i = features.Count - 1; i >= 0; i--)
            {
                var f = features[i];
                if (f.DyingLeft > 0)
                {
                    if (--f.DyingLeft == 0) Become(i, f, FeatureEventKind.Dead, featureDefs[f.Def].DeadDef);
                    continue;
                }
                if (f.BurnLeft <= 0) continue;
                if (f.SparkIn > 0 && --f.SparkIn == 0) Spark(f);
                if (--f.BurnLeft == 0) Become(i, f, FeatureEventKind.Burnt, featureDefs[f.Def].BurntDef);
            }
        }

        // The stage takes the feature's place, or it goes.
        void Become(int index, Feature f, FeatureEventKind kind, int newDef)
        {
            if (newDef >= featureDefs.Count) newDef = -1;
            NoteFeature(kind, index, f, newDef, f.Blast, f.From);
            if (newDef < 0) { features.RemoveAt(index); return; }
            f.Def = newDef;
            f.Damage = f.DyingLeft = f.BurnLeft = f.SparkIn = 0;
            LookOf(newDef, out f.Model, out f.Sprite);
        }

        // Each flammable feature within 3 cells may catch, and so may one
        // cell at each of five steps downwind (legacy:128021-128088).
        void Spark(Feature f)
        {
            var toward = WindNow().Toward;
            float cell = Terrain != null ? Terrain.CellSize : 1f;
            for (int j = 0; j < features.Count; j++)
            {
                var g = features[j];
                if (g == f || g.BurnLeft > 0 || g.DyingLeft > 0) continue;
                var d = featureDefs[g.Def];
                if (!d.Flammable || !d.Breakable) continue;
                float dx = g.Pos.x - f.Pos.x, dz = g.Pos.z - f.Pos.z;
                bool reached = dx * dx + dz * dz <= 9f * cell * cell;
                for (int k = 1; k <= 5 && !reached; k++) reached = Reach(g, f.Pos + toward * (k * cell)) <= 0f;
                if (reached && (SpreadChance >= 1f || Hash01(Seed(g) * 31 + (int)Tick) < SpreadChance)) Ignite(j, g, 0, f.Pos);
            }
        }

        void NoteFeature(FeatureEventKind kind, int index, Feature f, int newDef, int blast, Vector3 from, int damage = 0, int health = 0, int ticks = 0) =>
            Push(featureRing, new FeatureEvent
            {
                Id = ++lastFeatureEvent, Tick = Tick, Kind = kind, Feature = index, Def = f.Def, NewDef = newDef, Position = f.Pos,
                Blast = blast, From = blast != 0 ? from : f.Pos, Damage = damage, Health = health, Ticks = ticks,
            });

        // ── Wind ──────────────────────────────────────────────────────

        float windHeading, windSpeed;
        uint windNext;
        System.Random windRng;
        bool windHeld;

        // A speed between the least and the most, redrawn every 10 to 20
        // seconds with the heading turning up to 45 degrees (legacy:241670-241700).
        void StepWind()
        {
            if (windRng == null)
            {
                windRng = new System.Random((int)((setup?.Seed ?? 1u) ^ 0x5eed5u));
                windHeading = (float)windRng.NextDouble() * 360f;
                windSpeed = Mathf.Lerp(WindMin, WindMax, (float)windRng.NextDouble());
                windNext = Tick + Gust();
                return;
            }
            if (windHeld || Tick < windNext) return;
            windHeading = Mathf.Repeat(windHeading + ((float)windRng.NextDouble() * 2f - 1f) * 45f, 360f);
            windSpeed = Mathf.Lerp(WindMin, WindMax, (float)windRng.NextDouble());
            windNext = Tick + Gust();
        }

        uint Gust() => (uint)(Tps * (10 + windRng.Next(11)));

        Wind WindNow()
        {
            if (windRng == null) StepWind();
            return new Wind { Heading = windHeading, Speed = windSpeed, MaxSpeed = WindMax };
        }

        public bool ReadWind(out Wind wind)
        {
            wind = default;
            if (!SceneryBreaks || Terrain == null) return false;
            wind = WindNow();
            return true;
        }

        // Holds the wind at a heading and speed, for tests and staged scenes.
        public void SetWind(float heading, float speed)
        {
            if (windRng == null) StepWind();
            windHeading = Mathf.Repeat(heading, 360f);
            windSpeed = Mathf.Clamp(speed, 0f, WindMax);
            windHeld = true;
        }

        // ── Staged scenes ─────────────────────────────────────────────

        // Scenery breaking at the map's middle, with SceneryBreaks on.
        // "break" stands a tree, a wall, a hut and a stone body in a row,
        // each under cannon fire every few seconds. "fire" plants a grove
        // that a flaming arrow sets alight from the west, the wind held east.
        public bool StageBreak(string scene = "break", float everySeconds = 3f)
        {
            if (Terrain == null || players.Count == 0 || scene != "break" && scene != "fire") return false;
            SceneryBreaks = true;
            fxRepeats.Clear();
            foreach (int h in staged)
                if (byHandle.TryGetValue(h, out var old)) { units.Remove(old); byHandle.Remove(h); }
            staged.Clear();
            var c = StageCentre;
            if (scene == "break")
            {
                string[] kinds = { "mock_tree", "mock_wall", "mock_hut", "mock_stone_body" };
                for (int i = 0; i < kinds.Length; i++)
                {
                    var at = new Vector3(c.x + (i - 1.5f) * 6f, 0f, c.z);
                    StageFeature(kinds[i], at);
                    StageShooter("ARACAN 1", at + new Vector3(0f, 0f, -12f), at, everySeconds);
                }
                return true;
            }
            SetWind(90f, WindMax);
            for (int gx = 0; gx < 6; gx++)
                for (int gz = -1; gz <= 1; gz++)
                    StageFeature("mock_tree", new Vector3(c.x + gx * 1.5f, 0f, c.z + gz * 1.5f));
            StageShooter("TARARCH 1", new Vector3(c.x - 10f, 0f, c.z), new Vector3(c.x, 0f, c.z), 1000f);
            return true;
        }

        void StageFeature(string kind, Vector3 at)
        {
            int def = featureDefs.FindIndex(d => d.Name == kind);
            if (def >= 0) PutFeature(new Feature { Def = def, Pos = GroundAt(at), Heading = 0 });
        }

        void StageShooter(string weapon, Vector3 from, Vector3 to, float everySeconds)
        {
            var shooter = Spawn(DefFor(Role.Mage), 0, new Vector2(from.x, from.z));
            shooter.Heading = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            staged.Add(shooter.Handle);
            fxRepeats.Add(new FxRepeat
            {
                W = FxWeaponNamed(weapon), Shooter = shooter.Handle, Target = -1, From = from, To = to,
                Every = Mathf.Max(1, Mathf.RoundToInt(everySeconds * Tps)), Next = (int)Tick + 2,
            });
        }
    }
}
