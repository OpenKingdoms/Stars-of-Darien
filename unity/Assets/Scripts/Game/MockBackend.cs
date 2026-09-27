// MockBackend.cs - a fake engine behind IGameBackend: procedural maps with
// hills and sea, boxy units that wander, fight and die, arrows in flight,
// a small mana economy, and victory or defeat. Everything the presentation
// does can run on it today, with no engine and no game files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed class MockBackend : IGameBackend
    {
        public const int Tps = 30;

        // Seconds each loading stage lasts at least, so a person sees the
        // loading screen. Tests set 0.
        public float StageSeconds = 0.2f;
        // Scales unit damage, so a long test can keep everyone alive.
        public float DamageScale = 1f;

        public string Name => "Mock";
        public IReadOnlyList<MapInfo> Maps => maps;
        public IReadOnlyList<SideInfo> Sides => sides;
        public IReadOnlyList<UnitDef> UnitDefs => unitDefs;
        public IReadOnlyList<FeatureDef> FeatureDefs => featureDefs;
        public GameStatus Status { get; private set; }
        public int LocalPlayer => 0;
        public IReadOnlyList<PlayerInfo> Players => players;
        public int TicksPerSecond => Tps;
        public uint Tick { get; private set; }
        public MapTerrain Terrain { get; private set; }

        public static readonly Color32[] Palette =
        {
            new Color32(40, 90, 230, 255), new Color32(210, 40, 30, 255),
            new Color32(235, 235, 225, 255), new Color32(40, 40, 45, 255),
            new Color32(40, 160, 60, 255), new Color32(230, 200, 40, 255),
            new Color32(140, 60, 180, 255), new Color32(235, 130, 30, 255),
        };

        readonly List<MapInfo> maps = new List<MapInfo>
        {
            new MapInfo { Id = "mock_isles", Name = "Twin Isles", Description = "Two green islands across a shallow strait.", MaxPlayers = 4, Size = new Vector2(128, 128), Climate = "grass" },
            new MapInfo { Id = "mock_highlands", Name = "Highland Road", Description = "Rolling hills with a lake at the centre.", MaxPlayers = 4, Size = new Vector2(160, 128), Climate = "grass" },
            new MapInfo { Id = "mock_frost", Name = "Frost Pass", Description = "A cold valley between two ridges.", MaxPlayers = 2, Size = new Vector2(96, 96), Climate = "snow" },
            new MapInfo { Id = "mock_dunes", Name = "Red Dunes", Description = "Dry land and an oasis.", MaxPlayers = 4, Size = new Vector2(128, 96), Climate = "desert" },
        };

        readonly List<SideInfo> sides = new List<SideInfo>
        {
            new SideInfo { Id = "ARAMON", Name = "Aramon", Description = "Knights and archers of the western kingdom." },
            new SideInfo { Id = "VERUNA", Name = "Veruna", Description = "Seafarers and engineers." },
            new SideInfo { Id = "TAROS", Name = "Taros", Description = "Sorcerers and dark beasts." },
            new SideInfo { Id = "ZHON", Name = "Zhon", Description = "Beasts of the eastern wilds." },
        };

        readonly List<UnitDef> unitDefs = new List<UnitDef>();
        readonly List<FeatureDef> featureDefs = new List<FeatureDef>();
        readonly List<PlayerInfo> players = new List<PlayerInfo>();
        readonly List<ModelData> models = new List<ModelData>();
        readonly Dictionary<string, int> modelIds = new Dictionary<string, int>();
        readonly List<RgbaImage> textures = new List<RgbaImage>();
        readonly List<RgbaImage> sprites = new List<RgbaImage>();
        readonly Dictionary<int, RgbaImage> chunks = new Dictionary<int, RgbaImage>();

        readonly List<Unit> units = new List<Unit>();
        readonly Dictionary<int, Unit> byHandle = new Dictionary<int, Unit>();
        readonly List<Feature> features = new List<Feature>();
        readonly List<Arrow> arrows = new List<Arrow>();
        readonly List<Economy> economy = new List<Economy>();

        SkirmishSetup setup;
        MapInfo map;
        System.Random rng = new System.Random(1);
        int loadStage;
        readonly Stopwatch stageClock = new Stopwatch();
        int nextHandle = 1, nextArrow = 1;

        const int DefBuilder = 0, DefKnight = 1, DefArcher = 2, DefLodge = 3;

        sealed class Unit
        {
            public int Handle, Def, Player, Health, MaxHealth;
            public Vector3 Pos;
            public Vector2 Home;
            public float Heading, Cooldown, Built = 1f, DyingFor, WalkPhase, AttackPhase;
            public Vector2? Goal;
            public int Target = -1;
            public bool Dying, Moving, Attacking;
            public int BuildDef = -1;
            public float BuildLeft;
            public int Model;
        }

        sealed class Feature
        {
            public int Def, Model, Sprite;
            public Vector3 Pos;
            public float Heading;
        }

        sealed class Arrow
        {
            public int Id, Player, Target, Damage;
            public Vector3 Pos, Vel;
        }

        public MockBackend()
        {
            string[] anims = { "idle", "walk", "attack" };
            foreach (var s in sides)
            {
                string p = s.Id.ToLowerInvariant();
                AddDef(p + "_monarch", p + "monarch", s.Id, "builder", "Monarch", 900, 0, false, anims);
                AddDef(p + "_knight", p + "knight", s.Id, "infantry", "Knight", 220, 120, false, anims);
                AddDef(p + "_archer", p + "archer", s.Id, "ranged", "Archer", 140, 100, false, anims);
                AddDef(p + "_lodge", p + "lodge", s.Id, "building", "Lodge", 1200, 400, true, new[] { "idle" });
            }
            for (int i = 0; i < unitDefs.Count; i += 4)
            {
                unitDefs[i].BuildOptions = new[] { i + 1, i + 2, i + 3 };
                unitDefs[i + 3].BuildOptions = new[] { i + 1, i + 2 };
            }
            featureDefs.Add(new FeatureDef { Id = 0, Name = "mock_tree", ObjectName = "mocktree", SequenceName = "", Category = "trees", Footprint = new Vector2Int(1, 1), Height = 3f });
            featureDefs.Add(new FeatureDef { Id = 1, Name = "mock_rock", ObjectName = "", SequenceName = "mockrock", Category = "rocks", Footprint = new Vector2Int(2, 2), Height = 1.5f });
            featureDefs.Add(new FeatureDef { Id = 2, Name = "mock_bush", ObjectName = "", SequenceName = "mockbush", Category = "plants", Footprint = new Vector2Int(1, 1), Height = 1f });
            textures.Add(StoneTexture());
            sprites.Add(BlobSprite(new Color32(130, 125, 118, 255), 0.9f, 11));
            sprites.Add(BlobSprite(new Color32(50, 120, 45, 255), 0.7f, 23));
        }

        void AddDef(string name, string obj, string side, string cat, string title, int hp, int cost, bool building, string[] anims)
        {
            unitDefs.Add(new UnitDef
            {
                Id = unitDefs.Count, Name = name, ObjectName = obj, Side = side, Category = cat,
                Description = title, MaxHealth = hp, ManaCost = cost, IsBuilding = building,
                Footprint = building ? new Vector2Int(3, 3) : new Vector2Int(1, 1), Animations = anims,
            });
        }

        public void Dispose() => EndGame();

        // ---- Catalogue ----

        public RgbaImage MapPreview(string mapId, int maxSize)
        {
            var m = maps.Find(x => x.Id == mapId);
            if (m == null) return null;
            float scale = Mathf.Max(m.Size.x, m.Size.y) / Mathf.Max(1, maxSize);
            int w = Mathf.Max(1, Mathf.RoundToInt(m.Size.x / scale)), h = Mathf.Max(1, Mathf.RoundToInt(m.Size.y / scale));
            var img = new RgbaImage(w, h);
            var gen = new MockTerrainGen(m);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = (x + 0.5f) * scale, wz = -(y + 0.5f) * scale;
                    float ground = gen.Height(wx, wz);
                    Color c = gen.Colour(wx, wz, ground);
                    // The preview shows the sea, as the game draws it.
                    if (gen.SeaLevel > 0 && ground < gen.SeaLevel)
                        c = Color.Lerp(c, new Color(0.08f, 0.3f, 0.4f), Mathf.Clamp01(0.55f + (gen.SeaLevel - ground) * 0.25f));
                    Put(img, x, y, c);
                }
            return img;
        }

        // ---- Game ----

        public void StartSkirmish(SkirmishSetup s)
        {
            EndGame();
            setup = s;
            map = maps.Find(x => x.Id == s.MapId) ?? maps[0];
            rng = new System.Random((int)s.Seed);
            loadStage = 0;
            stageClock.Restart();
            Status = GameStatus.Loading;
        }

        static readonly string[] Stages = { "Reading the map", "Raising the land", "Planting the woods", "Mustering the armies" };

        public LoadProgress PumpLoading()
        {
            if (Status != GameStatus.Loading)
                return new LoadProgress { Fraction = 1, Done = Status == GameStatus.Running, Failed = Status == GameStatus.Failed, Stage = "" };
            if (stageClock.Elapsed.TotalSeconds >= StageSeconds)
            {
                switch (loadStage)
                {
                    case 0: MakePlayers(); break;
                    case 1: Terrain = new MockTerrainGen(map).Build(); break;
                    case 2: PlantFeatures(); break;
                    case 3: SpawnArmies(); break;
                }
                loadStage++;
                stageClock.Restart();
                if (loadStage >= Stages.Length) Status = GameStatus.Running;
            }
            bool done = Status == GameStatus.Running;
            return new LoadProgress
            {
                Fraction = (float)loadStage / Stages.Length,
                Stage = done ? "Ready" : Stages[loadStage],
                Done = done,
            };
        }

        public void EndGame()
        {
            units.Clear();
            byHandle.Clear();
            features.Clear();
            arrows.Clear();
            players.Clear();
            economy.Clear();
            chunks.Clear();
            Terrain = null;
            Tick = 0;
            Status = GameStatus.Idle;
        }

        void MakePlayers()
        {
            int seat = 0;
            foreach (var s in setup.Seats)
            {
                if (s.Kind == SeatKind.Closed) continue;
                string side = string.IsNullOrEmpty(s.Side) ? sides[rng.Next(sides.Count)].Id : s.Side;
                players.Add(new PlayerInfo
                {
                    Index = players.Count, Name = seat == 0 ? "You" : "Computer " + seat, Side = side,
                    Colour = s.Colour, Tint = Palette[Mathf.Abs(s.Colour) % Palette.Length], Team = s.Team,
                    IsLocal = players.Count == 0, IsComputer = s.Kind == SeatKind.Computer, Alive = true,
                });
                economy.Add(new Economy { Mana = setup.StartMana, Storage = Mathf.Max(1000, setup.StartMana), Income = 5, Expense = 0 });
                seat++;
            }
        }

        Vector2 StartOf(int player)
        {
            var sz = map.Size;
            Vector2[] corners =
            {
                new Vector2(0.18f, 0.82f), new Vector2(0.82f, 0.18f), new Vector2(0.82f, 0.82f), new Vector2(0.18f, 0.18f),
            };
            var c = corners[player % corners.Length];
            return new Vector2(c.x * sz.x, -c.y * sz.y);
        }

        void PlantFeatures()
        {
            var sz = Terrain.Size;
            for (int i = 0; i < 90; i++)
            {
                float x = (float)rng.NextDouble() * sz.x, z = -(float)rng.NextDouble() * sz.y;
                float h = Terrain.Sample(x, z);
                if (h < Terrain.SeaLevel + 0.4f) continue;
                bool nearStart = false;
                for (int p = 0; p < 4; p++) nearStart |= (StartOf(p) - new Vector2(x, z)).magnitude < 12;
                if (nearStart) continue;
                int def = i % 5 < 3 ? 0 : (i % 5 == 3 ? 1 : 2);
                features.Add(new Feature
                {
                    Def = def, Pos = new Vector3(x, h, z), Heading = (float)rng.NextDouble() * 360f,
                    Model = def == 0 ? LoadModel("mocktree", 0) : -1, Sprite = def == 0 ? -1 : def - 1,
                });
            }
        }

        void SpawnArmies()
        {
            foreach (var p in players)
            {
                var home = StartOf(p.Index);
                int side = Mathf.Max(0, sides.FindIndex(s => s.Id == p.Side)) * 4;
                Spawn(side + DefLodge, p.Index, home + new Vector2(-4, 4));
                Spawn(side + DefBuilder, p.Index, home);
                for (int i = 0; i < 4; i++) Spawn(side + DefKnight, p.Index, home + new Vector2(3 + i * 1.5f, -2));
                for (int i = 0; i < 3; i++) Spawn(side + DefArcher, p.Index, home + new Vector2(3 + i * 1.5f, -4));
            }
        }

        Unit Spawn(int def, int player, Vector2 at, float built = 1f)
        {
            var d = unitDefs[def];
            var u = new Unit
            {
                Handle = nextHandle++, Def = def, Player = player, MaxHealth = d.MaxHealth,
                Health = built >= 1f ? d.MaxHealth : 1, Built = built, Home = at,
                Pos = new Vector3(at.x, Terrain.Sample(at.x, at.y), at.y),
                Heading = (float)rng.NextDouble() * 360f, WalkPhase = (float)rng.NextDouble() * 6f,
                Model = LoadModel(d.ObjectName, players[player].Colour),
            };
            units.Add(u);
            byHandle[u.Handle] = u;
            return u;
        }

        public int Advance(int n)
        {
            if (Status != GameStatus.Running) return 0;
            for (int i = 0; i < n; i++) Step();
            return n;
        }

        void Step()
        {
            const float dt = 1f / Tps;
            Tick++;
            for (int i = 0; i < economy.Count; i++)
            {
                var e = economy[i];
                e.Mana = Mathf.Min(e.Storage, e.Mana + e.Income * dt);
                economy[i] = e;
            }

            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Dying)
                {
                    u.DyingFor += dt;
                    if (u.DyingFor > 1.5f) { units.RemoveAt(i); byHandle.Remove(u.Handle); }
                    continue;
                }
                if (u.Built < 1f) { u.Built = Mathf.Min(1f, u.Built + dt / 4f); u.Health = Mathf.Max(u.Health, (int)(u.MaxHealth * u.Built)); continue; }
                Think(u, dt);
            }

            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var a = arrows[i];
                a.Vel += Vector3.down * 9.8f * dt;
                a.Pos += a.Vel * dt;
                if (byHandle.TryGetValue(a.Target, out var t) && !t.Dying && (t.Pos + Vector3.up - a.Pos).sqrMagnitude < 1f)
                {
                    Hurt(t, a.Damage);
                    arrows.RemoveAt(i);
                }
                else if (a.Pos.y < GroundHeight(a.Pos.x, a.Pos.z) - 0.2f) arrows.RemoveAt(i);
            }

            if (Tick % Tps == 0) CheckOutcome();
        }

        void Think(Unit u, float dt)
        {
            var d = unitDefs[u.Def];
            u.Moving = false;
            u.Attacking = false;
            if (d.IsBuilding) { TickBuild(u, dt); return; }
            TickBuild(u, dt);

            bool ranged = u.Def % 4 == DefArcher;
            float range = ranged ? 8f : 1.4f;
            if (u.Target >= 0 && (!byHandle.TryGetValue(u.Target, out var tgt) || tgt.Dying)) u.Target = -1;
            if (u.Target < 0 && u.Def % 4 != DefBuilder) u.Target = NearestEnemy(u, u.Goal == null ? 9f : 4f);

            Vector2 pos = new Vector2(u.Pos.x, u.Pos.z);
            Vector2? moveTo = u.Goal;
            if (u.Target >= 0)
            {
                var t = byHandle[u.Target];
                var tp = new Vector2(t.Pos.x, t.Pos.z);
                float dist = (tp - pos).magnitude;
                if (dist <= range)
                {
                    moveTo = null;
                    u.Attacking = true;
                    Face(u, tp - pos, dt);
                    u.AttackPhase += dt;
                    u.Cooldown -= dt;
                    if (u.Cooldown <= 0)
                    {
                        u.Cooldown = ranged ? 1.6f : 1.1f;
                        if (ranged) Shoot(u, t);
                        else Hurt(t, (int)(18 * DamageScale));
                    }
                }
                else moveTo = tp;
            }
            else if (u.Goal == null && players[u.Player].IsComputer && Tick > Tps * 20 && rng.NextDouble() < 0.002)
            {
                // Now and then a computer unit sets off at the nearest enemy.
                int e = NearestEnemy(u, 1000f);
                if (e >= 0) u.Goal = new Vector2(byHandle[e].Pos.x, byHandle[e].Pos.z);
            }
            else if (u.Goal == null && rng.NextDouble() < 0.01)
            {
                u.Goal = u.Home + new Vector2((float)rng.NextDouble() * 8 - 4, (float)rng.NextDouble() * 8 - 4);
            }

            if (moveTo is Vector2 goal)
            {
                var to = goal - pos;
                float speed = u.Def % 4 == DefKnight ? 3.2f : 2.4f;
                if (to.magnitude < 0.3f) { if (u.Goal != null && (u.Goal.Value - pos).magnitude < 0.3f) u.Goal = null; }
                else
                {
                    Face(u, to, dt);
                    var step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
                    var next = pos + step;
                    var sz = Terrain.Size;
                    next.x = Mathf.Clamp(next.x, 1, sz.x - 1);
                    next.y = Mathf.Clamp(next.y, -sz.y + 1, -1);
                    if (Terrain.Sample(next.x, next.y) < Terrain.SeaLevel - 0.3f) u.Goal = null;
                    else
                    {
                        u.Pos = new Vector3(next.x, Terrain.Sample(next.x, next.y), next.y);
                        u.Moving = true;
                        u.WalkPhase += dt * speed * 2.5f;
                    }
                }
            }
        }

        void TickBuild(Unit u, float dt)
        {
            if (u.BuildDef < 0) return;
            var e = economy[u.Player];
            float rate = unitDefs[u.BuildDef].ManaCost / 5f;
            if (e.Mana < rate * dt) return;
            e.Mana -= rate * dt;
            economy[u.Player] = e;
            u.BuildLeft -= dt;
            if (u.BuildLeft > 0) return;
            var h = Quaternion.Euler(0, u.Heading, 0) * Vector3.forward * 2.5f;
            var made = Spawn(u.BuildDef, u.Player, new Vector2(u.Pos.x + h.x, u.Pos.z + h.z));
            made.Home = u.Home;
            u.BuildDef = -1;
        }

        void Face(Unit u, Vector2 dir, float dt)
        {
            float want = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            u.Heading = Mathf.MoveTowardsAngle(u.Heading, want, 360f * dt);
        }

        int NearestEnemy(Unit u, float within)
        {
            int best = -1;
            float bestD = within * within;
            int team = players[u.Player].Team;
            foreach (var o in units)
            {
                if (o.Dying || players[o.Player].Team == team) continue;
                float d = (o.Pos - u.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o.Handle; }
            }
            return best;
        }

        void Shoot(Unit u, Unit t)
        {
            var from = u.Pos + Vector3.up * 1.2f;
            var to = t.Pos + Vector3.up * 0.8f;
            float time = Mathf.Max(0.3f, (to - from).magnitude / 18f);
            var vel = (to - from) / time + Vector3.up * 0.5f * 9.8f * time;
            arrows.Add(new Arrow { Id = nextArrow++, Player = u.Player, Target = t.Handle, Damage = (int)(14 * DamageScale), Pos = from, Vel = vel });
        }

        void Hurt(Unit t, int dmg)
        {
            if (t.Dying || dmg <= 0) return;
            t.Health -= dmg;
            if (t.Health <= 0) { t.Health = 0; t.Dying = true; }
        }

        void CheckOutcome()
        {
            foreach (var p in players)
            {
                bool any = false;
                foreach (var u in units) any |= u.Player == p.Index && !u.Dying;
                p.Alive = any;
            }
            int myTeam = players[LocalPlayer].Team;
            bool mine = false, theirs = false;
            foreach (var p in players)
            {
                if (!p.Alive) continue;
                if (p.Team == myTeam) mine = true; else theirs = true;
            }
            if (!mine) Status = GameStatus.Defeat;
            else if (!theirs) Status = GameStatus.Victory;
        }

        // ---- World ----

        public float GroundHeight(float x, float z) => Terrain != null ? Terrain.Sample(x, z) : 0f;

        public RgbaImage TerrainChunk(int chunk)
        {
            if (Terrain == null || chunk < 0 || chunk >= Terrain.ChunkCount) return null;
            if (!chunks.TryGetValue(chunk, out var img))
                chunks[chunk] = img = new MockTerrainGen(map).Chunk(Terrain, chunk);
            return img;
        }

        public int ReadUnits(UnitState[] into)
        {
            int n = 0;
            foreach (var u in units)
            {
                if (n >= into.Length) break;
                var f = UnitFlags.Active;
                if (u.Dying) f = UnitFlags.Dying;
                if (u.Built < 1f) f |= UnitFlags.Building;
                if (u.Moving) f |= UnitFlags.Moving;
                if (u.Attacking) f |= UnitFlags.Attacking;
                into[n++] = new UnitState
                {
                    Handle = u.Handle, StableId = (uint)u.Handle, Def = u.Def, Player = u.Player, Flags = f,
                    Position = u.Pos, Heading = u.Heading, Roll = u.Dying ? Mathf.Min(90f, u.DyingFor * 120f) : 0f,
                    Health = u.Health, MaxHealth = u.MaxHealth, BuildProgress = u.Built, Model = u.Model,
                };
            }
            return n;
        }

        public int ReadFeatures(FeatureState[] into)
        {
            int n = 0;
            for (int i = 0; i < features.Count && n < into.Length; i++)
            {
                var f = features[i];
                var def = featureDefs[f.Def];
                into[n++] = new FeatureState
                {
                    Index = i, Def = f.Def, Position = f.Pos, Heading = f.Heading, Model = f.Model, Sprite = f.Sprite,
                    SpriteBottom = 0, SpriteTop = def.Height, SpriteWidth = def.Footprint.x * 1.2f,
                    SpriteOffsetX = def.Footprint.x * 0.6f,
                };
            }
            return n;
        }

        public int ReadProjectiles(ProjectileState[] into)
        {
            int n = 0;
            foreach (var a in arrows)
            {
                if (n >= into.Length) break;
                into[n++] = new ProjectileState { Id = a.Id, Player = a.Player, Kind = 0, Position = a.Pos, Velocity = a.Vel, Model = -1 };
            }
            return n;
        }

        public int ReadUnitPose(int handle, PiecePose[] into)
        {
            if (!byHandle.TryGetValue(handle, out var u)) return 0;
            var world = Matrix4x4.TRS(u.Pos, Quaternion.Euler(0, u.Heading, u.Dying ? Mathf.Min(90f, u.DyingFor * 120f) : 0), Vector3.one);
            string anim = u.Attacking ? "attack" : u.Moving ? "walk" : "idle";
            float t = u.Attacking ? u.AttackPhase : u.WalkPhase;
            int n = PoseModel(u.Model, anim, t, into);
            for (int i = 0; i < n; i++) into[i].Matrix = world * into[i].Matrix;
            return n;
        }

        public int ReadFeaturePose(int index, PiecePose[] into)
        {
            if (index < 0 || index >= features.Count) return 0;
            var f = features[index];
            var m = GetModel(f.Model);
            if (m == null) return 0;
            var world = Matrix4x4.TRS(f.Pos, Quaternion.Euler(0, f.Heading, 0), Vector3.one);
            int n = Mathf.Min(into.Length, m.Pieces.Length);
            for (int i = 0; i < n; i++) into[i] = new PiecePose { Matrix = world * RestMatrix(m, i) };
            return n;
        }

        // ---- Art ----

        public int LoadModel(string objectName, int colour)
        {
            string key = objectName + "#" + colour;
            if (modelIds.TryGetValue(key, out int id)) return id;
            var m = MockModels.Build(objectName, Palette[Mathf.Abs(colour) % Palette.Length]);
            if (m == null) return -1;
            models.Add(m);
            modelIds[key] = models.Count - 1;
            return models.Count - 1;
        }

        public ModelData GetModel(int model) => model >= 0 && model < models.Count ? models[model] : null;
        public RgbaImage Texture(int texture) => texture >= 0 && texture < textures.Count ? textures[texture] : null;
        public RgbaImage Sprite(int sprite) => sprite >= 0 && sprite < sprites.Count ? sprites[sprite] : null;

        public int PoseModel(int model, string animation, float seconds, PiecePose[] into)
        {
            var m = GetModel(model);
            if (m == null) return 0;
            int n = Mathf.Min(into.Length, m.Pieces.Length);
            var local = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                var p = m.Pieces[i];
                var rot = MockModels.Animate(p.Name, animation, seconds);
                local[i] = Matrix4x4.TRS(p.Offset, rot, Vector3.one);
                if (p.Parent >= 0 && p.Parent < i) local[i] = local[p.Parent] * local[i];
                into[i] = new PiecePose { Matrix = local[i] };
            }
            return n;
        }

        static Matrix4x4 RestMatrix(ModelData m, int i)
        {
            var mat = Matrix4x4.Translate(m.Pieces[i].Offset);
            for (int p = m.Pieces[i].Parent; p >= 0; p = m.Pieces[p].Parent)
                mat = Matrix4x4.Translate(m.Pieces[p].Offset) * mat;
            return mat;
        }

        // ---- Orders and economy ----

        public bool Command(in GameCommand c)
        {
            if (Status != GameStatus.Running || !byHandle.TryGetValue(c.Unit, out var u) || u.Dying) return false;
            var def = unitDefs[u.Def];
            switch (c.Kind)
            {
                case CommandKind.Move:
                case CommandKind.Patrol:
                    if (def.IsBuilding) return false;
                    u.Goal = new Vector2(c.Target.x, c.Target.z);
                    u.Home = u.Goal.Value;
                    u.Target = -1;
                    return true;
                case CommandKind.Attack:
                    if (def.IsBuilding || !byHandle.ContainsKey(c.TargetUnit)) return false;
                    u.Target = c.TargetUnit;
                    u.Goal = null;
                    return true;
                case CommandKind.Stop:
                    u.Goal = null;
                    u.Target = -1;
                    u.BuildDef = -1;
                    u.Home = new Vector2(u.Pos.x, u.Pos.z);
                    return true;
                case CommandKind.Build:
                    if (Array.IndexOf(def.BuildOptions, c.BuildDef) < 0) return false;
                    u.BuildDef = c.BuildDef;
                    u.BuildLeft = 5f;
                    return true;
                default:
                    return false;
            }
        }

        // The mock is silent.
        public bool SetAudio(float volume, bool music) => false;
        public void SetView(Vector3 centre, float width, float depth) { }

        public Economy ReadEconomy(int player) => player >= 0 && player < economy.Count ? economy[player] : default;

        public int ReadEffects(EffectState[] into) => 0;
        public RgbaImage UnitPicture(int def) => null;

        // ---- The game's own controls, simply ----

        readonly List<int> mockSelection = new List<int>();
        readonly Dictionary<int, List<int>> mockGroups = new Dictionary<int, List<int>>();
        CommandKind mockArmed;
        int mockArmedDef = -1;
        bool mockIsArmed;

        public void Select(int[] handles, bool add)
        {
            if (!add) mockSelection.Clear();
            if (handles == null) return;
            foreach (int h in handles)
                if (byHandle.TryGetValue(h, out var u) && !u.Dying && u.Player == LocalPlayer && !mockSelection.Contains(h))
                    mockSelection.Add(h);
        }

        public int ReadSelection(int[] into)
        {
            mockSelection.RemoveAll(h => !byHandle.TryGetValue(h, out var u) || u.Dying);
            for (int i = 0; into != null && i < mockSelection.Count && i < into.Length; i++) into[i] = mockSelection[i];
            return mockSelection.Count;
        }

        public void Click(Vector3 at, int unit, bool shift)
        {
            bool friend = unit >= 0 && byHandle.TryGetValue(unit, out var hit) && hit.Player == LocalPlayer;
            if (mockIsArmed)
            {
                mockIsArmed = false;
                foreach (int h in mockSelection.ToArray())
                    Command(new GameCommand { Kind = mockArmed, Unit = h, Target = at, TargetUnit = unit, BuildDef = mockArmedDef });
                return;
            }
            if (friend)
            {
                if (!shift) mockSelection.Clear();
                if (shift && mockSelection.Contains(unit)) mockSelection.Remove(unit);
                else if (!mockSelection.Contains(unit)) mockSelection.Add(unit);
                return;
            }
            foreach (int h in mockSelection.ToArray())
            {
                if (unit >= 0) Command(new GameCommand { Kind = CommandKind.Attack, Unit = h, TargetUnit = unit, BuildDef = -1 });
                else Command(GameCommand.To(CommandKind.Move, h, at));
            }
        }

        public void Cancel()
        {
            if (mockIsArmed) mockIsArmed = false;
            else mockSelection.Clear();
        }

        public void Arm(CommandKind kind, int buildDef = -1)
        {
            mockArmed = kind;
            mockArmedDef = buildDef;
            mockIsArmed = true;
        }

        public bool OrderSelection(CommandKind kind, int arg = 0)
        {
            bool any = false;
            foreach (int h in mockSelection.ToArray())
                any |= Command(new GameCommand { Kind = kind, Unit = h, TargetUnit = -1, BuildDef = -1, Arg = arg });
            return any;
        }

        public void AssignGroup(int group) => mockGroups[group] = new List<int>(mockSelection);

        public int RecallGroup(int group)
        {
            mockSelection.Clear();
            if (mockGroups.TryGetValue(group, out var g))
                foreach (int h in g)
                    if (byHandle.TryGetValue(h, out var u) && !u.Dying) mockSelection.Add(h);
            return mockSelection.Count;
        }

        // The mock's saves remember the setup and the tick only, and a load
        // starts that skirmish afresh, which is enough to drive the screens.
        [Serializable]
        sealed class MockSave
        {
            public string map;
            public uint seed, tick;
            public long savedAt;
            public List<SeatSetup> seats;
        }

        public bool SaveGame(string path)
        {
            if (Status != GameStatus.Running || setup == null) return false;
            try
            {
                var save = new MockSave { map = setup.MapId, seed = setup.Seed, tick = Tick, savedAt = DateTime.UtcNow.Ticks, seats = setup.Seats };
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(save));
                return true;
            }
            catch (Exception) { return false; }
        }

        public bool LoadGame(string path)
        {
            var save = ReadSave(path);
            if (save == null || maps.Find(m => m.Id == save.map) == null) return false;
            StartSkirmish(new SkirmishSetup { MapId = save.map, Seed = save.seed, Seats = save.seats ?? new List<SeatSetup>() });
            return true;
        }

        static MockSave ReadSave(string path)
        {
            try { return JsonUtility.FromJson<MockSave>(System.IO.File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        public bool SaveInfo(string path, out string map, out uint tick, out DateTime savedAt)
        {
            var save = ReadSave(path);
            map = save?.map;
            tick = save?.tick ?? 0;
            savedAt = save != null ? new DateTime(save.savedAt, DateTimeKind.Utc).ToLocalTime() : default;
            return save != null && !string.IsNullOrEmpty(save.map);
        }
        public RgbaImage EffectStrip(int strip) => null;

        // ---- HUD helpers ----

        public bool CanBuildAt(int def, Vector3 at, out Vector3 snapped)
        {
            snapped = new Vector3(Mathf.Floor(at.x) + 0.5f, 0f, Mathf.Floor(at.z) + 0.5f);
            if (Terrain == null) return false;
            snapped.y = Terrain.Sample(snapped.x, snapped.z);
            if (Terrain.SeaLevel > 0 && snapped.y < Terrain.SeaLevel + 0.2f) return false;
            foreach (var u in units)
                if (!u.Dying && new Vector2(u.Pos.x - snapped.x, u.Pos.z - snapped.z).magnitude < 1.5f) return false;
            return true;
        }

        public int QueuedCount(int factory, int def)
        {
            if (!byHandle.TryGetValue(factory, out var u) || u.BuildDef < 0) return 0;
            return def < 0 || u.BuildDef == def ? 1 : 0;
        }

        public UnitOrder ReadOrder(int handle)
        {
            var none = new UnitOrder { Kind = OrderKind.None, TargetUnit = -1, Building = -1 };
            if (!byHandle.TryGetValue(handle, out var u) || u.Dying) return none;
            if (u.BuildDef >= 0) return new UnitOrder { Kind = OrderKind.Build, TargetUnit = -1, Target = u.Pos, Building = -1 };
            if (u.Target >= 0 && byHandle.TryGetValue(u.Target, out var t))
                return new UnitOrder { Kind = OrderKind.Attack, TargetUnit = u.Target, Target = t.Pos, Building = -1 };
            if (u.Goal != null)
            {
                var g = new Vector3(u.Goal.Value.x, 0f, u.Goal.Value.y);
                if (Terrain != null) g.y = Terrain.Sample(g.x, g.z);
                return new UnitOrder { Kind = OrderKind.Move, TargetUnit = -1, Target = g, Building = -1 };
            }
            return none;
        }

        byte[] fogSeen = Array.Empty<byte>();

        public int ReadFog(byte[] into, out int width, out int height)
        {
            width = Terrain != null ? Terrain.HeightsW : 0;
            height = Terrain != null ? Terrain.HeightsH : 0;
            int need = width * height;
            if (into == null || into.Length < need || need == 0) return need;
            if (fogSeen.Length != need) fogSeen = new byte[need];
            bool revealed = setup != null && setup.MapRevealed;
            float cell = Terrain.CellSize;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    bool inSight = revealed;
                    if (!inSight)
                    {
                        var p = new Vector2(x * cell, -y * cell);
                        foreach (var u in units)
                        {
                            if (u.Dying || u.Player != LocalPlayer) continue;
                            if ((new Vector2(u.Pos.x, u.Pos.z) - p).sqrMagnitude < 100f) { inSight = true; break; }
                        }
                    }
                    if (inSight) fogSeen[i] = 1;
                    into[i] = inSight ? (byte)2 : fogSeen[i];
                }
            return need;
        }

        // ---- Pictures ----

        internal static void Put(RgbaImage img, int x, int y, Color32 c)
        {
            int o = (y * img.Width + x) * 4;
            img.Pixels[o] = c.r; img.Pixels[o + 1] = c.g; img.Pixels[o + 2] = c.b; img.Pixels[o + 3] = c.a;
        }

        static RgbaImage StoneTexture()
        {
            var img = new RgbaImage(64, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool mortar = y % 16 == 0 || (x + (y / 16 % 2) * 8) % 16 == 0;
                    byte v = (byte)(mortar ? 90 : 150 + MockNoise.Hash(x, y, 5) * 40);
                    Put(img, x, y, new Color32(v, (byte)(v - 5), (byte)(v - 12), 255));
                }
            return img;
        }

        static RgbaImage BlobSprite(Color32 c, float fill, int seed)
        {
            const int S = 64;
            var img = new RgbaImage(S, S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S / 2f) / (S / 2f), dy = (y - S * 0.6f) / (S * 0.45f);
                    float r = fill + (MockNoise.Value(x * 0.15f, y * 0.15f, seed) - 0.5f) * 0.3f;
                    bool inside = dx * dx + dy * dy < r * r && y < S - 2;
                    float shade = 0.7f + 0.5f * (1f - (float)y / S);
                    Put(img, x, y, inside ? new Color32((byte)Mathf.Min(255, c.r * shade), (byte)Mathf.Min(255, c.g * shade), (byte)Mathf.Min(255, c.b * shade), 255) : new Color32(0, 0, 0, 0));
                }
            return img;
        }
    }
}
