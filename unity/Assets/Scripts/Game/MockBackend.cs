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
    public sealed partial class MockBackend : IGameBackend
    {
        public const int Tps = 30;
        // The mock flyer's cruise height and top speed, in world units.
        public const float FlyerCruise = 6f, FlyerSpeed = 4f;

        // Seconds each loading stage lasts at least, so a person sees the
        // loading screen. Tests set 0.
        public float StageSeconds = 0.2f;
        // Scales unit damage, so a long test can keep everyone alive.
        public float DamageScale = 1f;
        // A mage, a healer and a wagon beside each army, for the battle HUD.
        public bool Specialists = true;
        // Extra soldiers per side, in ranks behind the first, for load tests.
        public int ExtraSoldiers;

        public string Name => "Mock";
        public IReadOnlyList<MapInfo> Maps => maps;
        public IReadOnlyList<SideInfo> Sides => sides;
        public IReadOnlyList<UnitDef> UnitDefs => unitDefs;
        public IReadOnlyList<FeatureDef> FeatureDefs => featureDefs;
        public GameStatus Status { get; private set; }
        // Ids start at 1, as the engine's do. Inside, the mock keeps list
        // positions and adds one at the contract.
        public int LocalPlayer => players.Count > 0 ? players[0].Index : 1;

        // The mock's list position for a player id, or -1, and back.
        int PosOf(int id) => players.FindIndex(pl => pl.Index == id);
        int IdOf(int pos) => pos >= 0 && pos < players.Count ? players[pos].Index : 0;
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
            new MapInfo { Id = "mock_moat", Name = "Castle Moat", Description = "A high rim around a lake, as on Castle.", MaxPlayers = 4, Size = new Vector2(96, 96), Climate = "grass" },
            new MapInfo { Id = "mock_bay", Name = "Quiet Bay", Description = "A long bay between a sandy beach and a cliff.", MaxPlayers = 2, Size = new Vector2(128, 128), Climate = "grass" },
            // Larger lands for the lobby's browser, in the original's map units of 32.
            new MapInfo { Id = "mock_marches", Name = "Border Marches", Description = "Eight holds along a wide frontier.", MaxPlayers = 8, Size = new Vector2(512, 512), Climate = "grass" },
            new MapInfo { Id = "mock_fens", Name = "Seven Fens", Description = "Reed beds and causeways between black pools.", MaxPlayers = 7, Size = new Vector2(448, 384), Climate = "swamp" },
            new MapInfo { Id = "mock_ridge", Name = "Ember Ridge", Description = "Three camps below a smoking ridge.", MaxPlayers = 3, Size = new Vector2(320, 192), Climate = "volcanic" },
            new MapInfo { Id = "mock_crossing", Name = "Harrow Crossing", Description = "Fords over a winding river.", MaxPlayers = 6, Size = new Vector2(384, 256), Climate = "grass" },
            new MapInfo { Id = "mock_steppe", Name = "Wide Steppe", Description = "Open sand from edge to edge.", MaxPlayers = 8, Size = new Vector2(640, 640), Climate = "desert" },
            new MapInfo { Id = "mock_hollow", Name = "Wren Hollow", Description = "A small green valley for two.", MaxPlayers = 2, Size = new Vector2(192, 192), Climate = "grass" },
            new MapInfo { Id = "mock_spine", Name = "Serpent Spine", Description = "Snowfields either side of a long ridge.", MaxPlayers = 5, Size = new Vector2(480, 480), Climate = "snow" },
            new MapInfo { Id = "mock_saltmarsh", Name = "Saltmarsh Keep", Description = "Four keeps on the edge of a marsh.", MaxPlayers = 4, Size = new Vector2(288, 320), Climate = "swamp" },
        };

        // The first six keep the corners their armies always had, the rest
        // stand round the middle.
        static readonly Vector2[] Corners = { new Vector2(0.18f, 0.82f), new Vector2(0.82f, 0.18f), new Vector2(0.82f, 0.82f), new Vector2(0.18f, 0.18f) };

        void GiveStarts()
        {
            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i];
                var st = new Vector2[m.MaxPlayers];
                for (int k = 0; k < st.Length; k++)
                {
                    Vector2 f = i < 6 ? Corners[k % 4] : new Vector2(0.5f + 0.36f * Mathf.Cos(2.3f + k * 2f * Mathf.PI / st.Length), 0.5f + 0.34f * Mathf.Sin(2.3f + k * 2f * Mathf.PI / st.Length));
                    st[k] = new Vector2(f.x * m.Size.x, f.y * m.Size.y);
                }
                m.Starts = st;
            }
        }

        MockRooms rooms;
        public IGameRooms Rooms => rooms ??= new MockRooms(id => maps.Find(m => m.Id == id), () => maps.Count > 0 ? maps[0].Id : null);

        // A folder of the game's own interface art (its anims folder), from
        // OKU_ART_DIR, or null for none.
        public string ArtDir = System.Environment.GetEnvironmentVariable("OKU_ART_DIR");

        public ArtFrame InterfaceArt(string gaf, string entry, int frame) =>
            string.IsNullOrEmpty(ArtDir) ? null : GafFiles.Read(ArtDir, gaf, entry, frame);

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
        readonly Dictionary<(string, int), int> modelById = new Dictionary<(string, int), int>();
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
            public int Kills;
            public float BuildLeft;
            public int Model;
            public int Facing, BuildFacing;
            public Vector3? BuildAt;
            public float Alt, Speed, FlyTime;
            public bool Flying;
            // Set by an order, so Shift's next one waits behind it.
            public bool Ordered;
            public OrderKind OrderKind = OrderKind.Move;
            // A factory's units still to make after BuildDef, the def it
            // repeats (-1 for none) and where its units go.
            public readonly List<int> Line = new List<int>();
            public int Repeat = -1;
            public Vector2? Rally;
        }

        sealed class Feature
        {
            public int Def, Model, Sprite;
            public Vector3 Pos;
            public float Heading;
            public float Sink;      // units a second, for a corpse
            // With SceneryBreaks: damage taken, ticks left dying and
            // burning, ticks to its sparks, and the blast behind it.
            public int Damage, DyingLeft, BurnLeft, SparkIn, Blast;
            public Vector3 From;
        }

        sealed class Arrow
        {
            public int Id, Player, Target, Damage, From;
            public int Def = -1;        // the shooter's, for the blast it makes
            public Vector3 Pos, Vel;
            public FxWeapon Look;       // how it is drawn, and what it leaves where it lands
            public uint Born;
        }

        public MockBackend()
        {
            GiveStarts();
            string[] anims = { "idle", "walk", "attack" };
            foreach (var s in sides)
            {
                string p = s.Id.ToLowerInvariant(), t = SideToken(s.Id);
                AddDef(p + "_monarch", p + "monarch", s.Id, t + " MONARCH BUILDER", "Monarch", 900, 0, false, anims);
                AddDef(p + "_knight", p + "knight", s.Id, t + " MELEE ATTACK", "Knight", 220, 120, false, anims);
                AddDef(p + "_archer", p + "archer", s.Id, t + " BALLISTIC ATTACK", "Archer", 140, 100, false, anims);
                AddDef(p + "_lodge", p + "lodge", s.Id, t + " BUILDING", "Lodge", 1200, 400, true, new[] { "idle" });
            }
            for (int i = 0; i < unitDefs.Count; i += 4)
            {
                unitDefs[i].BuildOptions = new[] { i + 1, i + 2, i + 3 };
                unitDefs[i + 3].BuildOptions = new[] { i + 1, i + 2 };
            }
            AddSpecialists();
            AddFeatureDefs();
            textures.Add(StoneTexture());
            sprites.Add(BlobSprite(new Color32(130, 125, 118, 255), 0.9f, 11));
            sprites.Add(BlobSprite(new Color32(50, 120, 45, 255), 0.7f, 23));
        }

        // Categories read like the game's: the side's three letters, then tokens.
        static string SideToken(string side) => side == "ZHON" ? "ZON" : side.Substring(0, 3);

        void AddDef(string name, string obj, string side, string cat, string title, int hp, int cost, bool building, string[] anims)
        {
            unitDefs.Add(new UnitDef
            {
                Id = unitDefs.Count, Name = name, ObjectName = obj, Side = side, Category = cat,
                Description = title, MaxHealth = hp, ManaCost = cost, IsBuilding = building,
                Footprint = building ? new Vector2Int(4, 2) : new Vector2Int(1, 1), Animations = anims,
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
            SeesAll = false;
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
                    case 1:
                        if (savedMaps.TryGetValue(map.Id, out var saved))
                        {
                            Terrain = Clone(saved.terrain);
                            foreach (var kv in saved.library) libraryChunk[kv.Key] = kv.Value;
                        }
                        else Terrain = new MockTerrainGen(map).Build();
                        break;
                    case 2:
                        if (savedMaps.TryGetValue(map.Id, out var sf)) features.AddRange(sf.features);
                        else PlantFeatures();
                        break;
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
            ForgetRecord();
            units.Clear();
            byHandle.Clear();
            placed.Clear();
            features.Clear();
            arrows.Clear();
            ClearFx();
            ForgetDestruction();
            players.Clear();
            economy.Clear();
            chunks.Clear();
            libraryChunk.Clear();
            ForgetFormations();
            Terrain = null;
            Tick = 0;
            PlaysOn = false;
            Status = GameStatus.Idle;
        }

        void MakePlayers()
        {
            int seat = 0, position = 0;
            var dealt = StartPositions.Assign(setup.Seats, map.Starts.Length, setup.RandomStarts, setup.Seed);
            homes.Clear();
            foreach (var s in setup.Seats)
            {
                position++;
                if (s.Kind == SeatKind.Closed) continue;
                int start = dealt[position - 1];
                homes.Add(start >= 0 ? new Vector2(map.Starts[start].x, -map.Starts[start].y) : Corner(homes.Count));
                string side = string.IsNullOrEmpty(s.Side) ? sides[rng.Next(sides.Count)].Id : s.Side;
                players.Add(new PlayerInfo
                {
                    Index = position, Name = seat == 0 ? "You" : "Computer " + seat, Side = side,
                    Colour = s.Colour, Tint = Palette[Mathf.Abs(s.Colour) % Palette.Length], Team = s.Team,
                    IsLocal = players.Count == 0, IsComputer = s.Kind == SeatKind.Computer, Alive = true,
                });
                economy.Add(new Economy { Mana = setup.StartMana, Storage = Mathf.Max(1000, setup.StartMana), Income = 5, Expense = 0 });
                seat++;
            }
        }

        // Each player's start by list position, from the seats' dealt starts.
        readonly List<Vector2> homes = new List<Vector2>();

        Vector2 StartOf(int player)
        {
            if (player < homes.Count) return homes[player];
            return Corner(player);
        }

        Vector2 Corner(int player)
        {
            var c = Corners[player % Corners.Length];
            return new Vector2(c.x * map.Size.x, -c.y * map.Size.y);
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
                for (int p = 0; p < Mathf.Max(4, homes.Count); p++) nearStart |= (StartOf(p) - new Vector2(x, z)).magnitude < 12;
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
            for (int pos = 0; pos < players.Count; pos++)
            {
                var p = players[pos];
                var home = StartOf(pos);
                int side = Mathf.Max(0, sides.FindIndex(s => s.Id == p.Side)) * 4;
                Spawn(side + DefLodge, pos, home + new Vector2(-4, 4));
                Spawn(side + DefBuilder, pos, home);
                for (int i = 0; i < 4; i++) Spawn(side + DefKnight, pos, home + new Vector2(3 + i * 1.5f, -2));
                for (int i = 0; i < 3; i++) Spawn(side + DefArcher, pos, home + new Vector2(3 + i * 1.5f, -4));
                if (Specialists) SpawnSpecialists(p, pos, home);
                for (int i = 0; i < ExtraSoldiers; i++)
                    Spawn(side + (i % 2 == 0 ? DefKnight : DefArcher), pos, home + new Vector2(-6 + (i % 12) * 1.3f, -6 - (i / 12) * 1.3f));
            }
        }

        // How long a frame takes to build itself, 0 to hold every build.
        public float BuildSeconds = 4f;

        // A frame of the local player's, so far built. For tests.
        public int SpawnFrame(int def, Vector3 at, float built)
        {
            var u = Spawn(def, 0, new Vector2(at.x, at.z), Mathf.Clamp(built, 0.001f, 1f));
            return u.Handle;
        }

        public void SetBuilt(int handle, float built)
        {
            if (!byHandle.TryGetValue(handle, out var u)) return;
            u.Built = Mathf.Clamp(built, 0.001f, 1f);
            u.Health = Mathf.Max(1, (int)(u.MaxHealth * u.Built));
        }

        Unit Spawn(int def, int player, Vector2 at, float built = 1f, int facing = 0)
        {
            var d = unitDefs[def];
            facing = d.IsBuilding && CanRotate(def) ? ((facing % 4) + 4) % 4 : 0;
            var u = new Unit
            {
                Handle = nextHandle++, Def = def, Player = player, MaxHealth = d.MaxHealth,
                Health = built >= 1f ? d.MaxHealth : 1, Built = built, Home = at,
                Pos = new Vector3(at.x, Terrain.Sample(at.x, at.y), at.y),
                Heading = d.IsBuilding ? facing * 90f : (float)rng.NextDouble() * 360f, WalkPhase = (float)rng.NextDouble() * 6f,
                Model = LoadModel(d.ObjectName, players[player].Colour), Facing = facing,
            };
            units.Add(u);
            byHandle[u.Handle] = u;
            NoteSpawn(u);
            return u;
        }

        public int Advance(int n)
        {
            if (Status != GameStatus.Running && !PlaysOn) return 0;
            for (int i = 0; i < n; i++) Step();
            return n;
        }

        void Step()
        {
            const float dt = 1f / Tps;
            Tick++;
            TickMana(dt);
            SinkFeatures(dt);
            if (Tick % 10 == 0) Look();
            for (int i = 0; i < economy.Count; i++)
            {
                var e = economy[i];
                float was = e.Mana;
                e.Mana = Mathf.Min(e.Storage, e.Mana + e.Income * dt);
                KeptOf(i).Gathered += Mathf.Max(0f, e.Mana - was);
                economy[i] = e;
            }

            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Dying)
                {
                    u.DyingFor += dt;
                    // A flyer falls.
                    if (u.Alt > 0f) { u.Alt = Mathf.Max(0f, u.Alt - 8f * dt); u.Pos.y = Terrain.Sample(u.Pos.x, u.Pos.z) + u.Alt; }
                    if (u.DyingFor > 1.5f) { units.RemoveAt(i); byHandle.Remove(u.Handle); }
                    continue;
                }
                if (aboard.Contains(u.Handle) || staged.Contains(u.Handle) || placed.Contains(u.Handle)) continue;
                if (u.Built < 1f)
                {
                    if (BuildSeconds > 0) u.Built = Mathf.Min(1f, u.Built + dt / BuildSeconds);
                    u.Health = Mathf.Max(u.Health, (int)(u.MaxHealth * u.Built));
                    continue;
                }
                Think(u, dt);
            }

            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var a = arrows[i];
                a.Vel += Vector3.down * 9.8f * dt;
                a.Pos += a.Vel * dt;
                if (byHandle.TryGetValue(a.Target, out var t) && !t.Dying && (t.Pos + Vector3.up - a.Pos).sqrMagnitude < 1f)
                {
                    Hurt(t, a.Damage, a.From);
                    arrows.RemoveAt(i);
                    if (a.Look?.Impact != null) SpawnBlast(FxStripId(a.Look.Impact), GroundAt(a.Pos), struck: t.Handle);
                    Burst(a, a.Pos, t.Handle);
                }
                else if (a.Pos.y < GroundHeight(a.Pos.x, a.Pos.z) - 0.2f)
                {
                    arrows.RemoveAt(i);
                    if (a.Look?.Impact != null) SpawnBlast(FxStripId(a.Look.Impact), GroundAt(a.Pos));
                    Burst(a, GroundAt(a.Pos), -1);
                }
            }
            StepFx(dt);
            StepScenery();

            if (Tick % Tps == 0) { CheckOutcome(); NoteStanding(); }
            NoteSample();
        }

        void Think(Unit u, float dt)
        {
            var d = unitDefs[u.Def];
            u.Moving = false;
            u.Attacking = false;
            if (d.IsBuilding) { TickBuild(u, dt); return; }
            TickBuild(u, dt);

            bool ranged = RoleOf(u.Def) == Role.Archer || RoleOf(u.Def) == Role.Mage;
            float range = ranged ? 8f : 1.4f;
            if (u.Target >= 0 && (!byHandle.TryGetValue(u.Target, out var tgt) || tgt.Dying)) u.Target = -1;
            Next(u);
            // Passive units never pick a fight, defensive ones only close by.
            var stance = StanceOf(u.Handle);
            var role = RoleOf(u.Def);
            bool fights = role != Role.Monarch && role != Role.Healer && role != Role.Wagon && role != Role.Boat && stance != Stance.Passive;
            if (u.Target < 0 && fights) u.Target = NearestEnemy(u, stance == Stance.Defensive ? 3f : u.Goal == null ? 9f : 4f);

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
                        else Hurt(t, (int)(18 * DamageScale), u.Handle);
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
            else if (u.Goal == null && !d.CanFly && !HoldsFormation(u) && rng.NextDouble() < 0.01)
            {
                // Idle soldiers wander. A flyer stays down until it has an order.
                u.Goal = u.Home + new Vector2((float)rng.NextDouble() * 8 - 4, (float)rng.NextDouble() * 8 - 4);
            }

            if (moveTo is Vector2 goal)
            {
                var to = goal - pos;
                float speed = PacedSpeed(u, MockSpeed(u));
                if (to.magnitude < 0.3f) { if (u.Goal != null && (u.Goal.Value - pos).magnitude < 0.3f) { u.Goal = null; Arrived(u); } }
                else
                {
                    Face(u, to, dt);
                    var step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
                    var next = pos + step;
                    var sz = Terrain.Size;
                    next.x = Mathf.Clamp(next.x, 1, sz.x - 1);
                    next.y = Mathf.Clamp(next.y, -sz.y + 1, -1);
                    // Flyers go anywhere, boats keep to water deep enough to float, the rest to land.
                    bool wet = Terrain.Sample(next.x, next.y) < Terrain.SeaLevel - 0.3f;
                    if (!d.CanFly && wet != (role == Role.Boat)) { u.Goal = null; Arrived(u); }
                    else
                    {
                        u.Pos = new Vector3(next.x, Terrain.Sample(next.x, next.y) + u.Alt, next.y);
                        u.Moving = true;
                        u.WalkPhase += dt * speed * 2.5f;
                    }
                }
            }
            HoldFacing(u, dt);
            u.Speed = (new Vector2(u.Pos.x, u.Pos.z) - pos).magnitude / dt;
            if (d.CanFly) TickFlight(u, d, dt);
        }

        // A flyer is in the air while it has somewhere to go or someone to
        // fight, as the engine's flyers are, and climbs and lands at top speed.
        void TickFlight(Unit u, UnitDef d, float dt)
        {
            u.Flying = u.Goal != null || u.Target >= 0;
            u.Alt = Mathf.MoveTowards(u.Alt, u.Flying ? d.CruiseAltitude : 0f, d.MaxSpeed * dt);
            u.Pos.y = Terrain.Sample(u.Pos.x, u.Pos.z) + u.Alt;
            if (u.Alt > 0f) u.FlyTime += dt;
        }

        void TickBuild(Unit u, float dt)
        {
            if (u.BuildDef < 0) return;
            var e = economy[u.Player];
            float rate = unitDefs[u.BuildDef].ManaCost / 5f;
            if (e.Mana < rate * dt) return;
            e.Mana -= rate * dt;
            economy[u.Player] = e;
            KeptOf(u.Player).Spent += rate * dt;
            BuildSparkle(u);
            u.BuildLeft -= dt;
            if (u.BuildLeft > 0) return;
            Unit made;
            if (u.BuildAt is Vector3 site)
                made = Spawn(u.BuildDef, u.Player, new Vector2(site.x, site.z), 1f, u.BuildFacing);
            else
            {
                var h = Quaternion.Euler(0, u.Heading, 0) * Vector3.forward * 2.5f;
                made = Spawn(u.BuildDef, u.Player, new Vector2(u.Pos.x + h.x, u.Pos.z + h.z));
            }
            made.Home = u.Home;
            NoteFinished(made);
            if (u.Rally is Vector2 rally) { made.Goal = rally; made.Home = rally; made.Ordered = true; }
            u.BuildDef = -1;
            u.BuildAt = null;
            if (unitDefs[u.Def].IsBuilding) StartNext(u);
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
                if (o.Dying || o.Player == u.Player || SeatTeam.Allied(players[o.Player].Team, team)) continue;
                float d = (o.Pos - u.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o.Handle; }
            }
            return best;
        }

        void Shoot(Unit u, Unit t)
        {
            if (RoleOf(u.Def) == Role.Mage) NoteCast(u.Player);
            var from = u.Pos + Vector3.up * 1.2f;
            var to = t.Pos + Vector3.up * 0.8f;
            float time = Mathf.Max(0.3f, (to - from).magnitude / 18f);
            var vel = (to - from) / time + Vector3.up * 0.5f * 9.8f * time;
            var look = FxWeaponNamed(RoleOf(u.Def) == Role.Mage ? "MOCK FIREBALL" : "MOCK ARROW");
            arrows.Add(new Arrow { Id = nextArrow++, Player = u.Player, Target = t.Handle, Damage = (int)(14 * DamageScale), Pos = from, Vel = vel, From = u.Handle, Look = look, Born = Tick, Def = u.Def });
        }

        void Hurt(Unit t, int dmg, int by = -1)
        {
            if (t.Dying || dmg <= 0) return;
            Unit killer = null;
            if (by >= 0) byHandle.TryGetValue(by, out killer);
            NoteHit(killer, t, Mathf.Min(dmg, t.Health));
            t.Health -= dmg;
            if (t.Health > 0) return;
            t.Health = 0;
            t.Dying = true;
            if (killer != null && killer.Player != t.Player) killer.Kills++;
            NoteDeath(killer, t);
            Died(t);
        }

        // Kills as the mock counts them, and a rank at three and at ten.
        public bool UnitRecord(int handle, out int kills, out int rank)
        {
            kills = byHandle.TryGetValue(handle, out var u) ? u.Kills : 0;
            rank = kills >= 10 ? 2 : kills >= 3 ? 1 : 0;
            return u != null;
        }

        // For tests: set a unit's kills.
        public void SetKills(int handle, int kills)
        {
            if (byHandle.TryGetValue(handle, out var u)) u.Kills = kills;
        }

        void CheckOutcome()
        {
            for (int pos = 0; pos < players.Count; pos++)
            {
                var p = players[pos];
                bool any = false;
                foreach (var u in units) any |= u.Player == pos && !u.Dying;
                p.Alive = any;
            }
            // A lost battle played on keeps its defeat and stops with the war.
            if (PlaysOn) { PlaysOn = SidesAtWar(); return; }
            bool mine = false, theirs = false;
            for (int pos = 0; pos < players.Count; pos++)
            {
                if (!players[pos].Alive) continue;
                if (pos == 0 || SeatTeam.Allied(players[pos].Team, players[0].Team)) mine = true; else theirs = true;
            }
            if (!mine) Status = GameStatus.Defeat;
            else if (!theirs) Status = GameStatus.Victory;
        }

        // ---- World ----

        public float GroundHeight(float x, float z) => Terrain != null ? Terrain.Sample(x, z) : 0f;

        // ---- Map editing ----

        // The mock stores a height as ten bytes a unit.
        const float BytesPerUnit = 10f;
        static readonly uint[] Library = { 9001, 9002, 9003, 9004 };
        readonly Dictionary<uint, int> libraryChunk = new Dictionary<uint, int>();
        readonly Dictionary<string, (MapTerrain terrain, List<Feature> features, Dictionary<uint, int> library)> savedMaps =
            new Dictionary<string, (MapTerrain, List<Feature>, Dictionary<uint, int>)>();

        public int ReadCells(byte[] into, out int width, out int height)
        {
            width = height = 0;
            if (Terrain == null) return 0;
            width = Terrain.HeightsW;
            height = Terrain.HeightsH;
            int n = width * height;
            if (into == null || into.Length < n) return n;
            for (int i = 0; i < n; i++) into[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Terrain.Heights[i] * BytesPerUnit), 0, 255);
            return n;
        }

        public bool EditCells(int x0, int z0, int w, int h, byte[] values)
        {
            if (Terrain == null || values == null || values.Length < w * h) return false;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x0 + x, cz = z0 + z;
                    if (cx < 0 || cz < 0 || cx >= Terrain.HeightsW || cz >= Terrain.HeightsH) continue;
                    Terrain.Heights[cz * Terrain.HeightsW + cx] = values[z * w + x] / BytesPerUnit;
                }
            chunks.Clear();
            return true;
        }

        public uint[] ChunkLibrary() => (uint[])Library.Clone();

        public RgbaImage ChunkPicture(uint id)
        {
            int k = Array.IndexOf(Library, id);
            if (k < 0) return null;
            Color[] tints = { new Color(0.3f, 0.5f, 0.2f), new Color(0.82f, 0.72f, 0.5f), new Color(0.5f, 0.48f, 0.45f), new Color(0.9f, 0.92f, 0.95f) };
            const int S = MockTerrainGen.ChunkBlocks * MockTerrainGen.BlockTexels;
            var img = new RgbaImage(S, S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    Put(img, x, y, (Color32)(tints[k] * (0.8f + 0.4f * MockNoise.Value(x * 0.2f, y * 0.2f, k + 3))));
            return img;
        }

        public bool PaintBlocks(int bx, int by, int w, int h, uint[] chunkIds, byte[] texX, byte[] texY)
        {
            var t = Terrain;
            if (t == null || chunkIds == null || chunkIds.Length < w * h) return false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = bx + x, cy = by + y;
                    if (cx < 0 || cy < 0 || cx >= t.BlocksW || cy >= t.BlocksH) continue;
                    uint id = chunkIds[y * w + x];
                    if (!libraryChunk.TryGetValue(id, out int chunk))
                    {
                        var pic = ChunkPicture(id);
                        if (pic == null) return false;
                        chunk = t.ChunkCount++;
                        libraryChunk[id] = chunk;
                        chunks[chunk] = pic;
                    }
                    int b = cy * t.BlocksW + cx;
                    t.Blocks[3 * b] = chunk;
                    t.Blocks[3 * b + 1] = (texX != null ? texX[y * w + x] : 0) * t.BlockTexels;
                    t.Blocks[3 * b + 2] = (texY != null ? texY[y * w + x] : 0) * t.BlockTexels;
                }
            // The engine reads its terrain afresh after a paint, a new object.
            if (ReplacesTerrainOnPaint) Terrain = Clone(t);
            return true;
        }

        // For tests: behave as the engine does after PaintBlocks.
        public bool ReplacesTerrainOnPaint;

        public int PlaceFeature(int def, int cx, int cz)
        {
            if (Terrain == null || def < 0 || def >= featureDefs.Count) return -1;
            float x = cx * Terrain.CellSize, z = -cz * Terrain.CellSize;
            return PutFeature(new Feature { Def = def, Pos = new Vector3(x, Terrain.Sample(x, z), z), Heading = 0 });
        }

        // A feature at a point that sinks so many units a second, as a corpse
        // does, until it is gone. For tests.
        public int AddFeature(int def, Vector3 pos, float sinkPerSecond)
        {
            if (def < 0 || def >= featureDefs.Count) return -1;
            return PutFeature(new Feature { Def = def, Pos = pos, Heading = 0, Sink = sinkPerSecond });
        }

        int PutFeature(Feature f)
        {
            LookOf(f.Def, out f.Model, out f.Sprite);
            features.Add(f);
            NoteFeature(FeatureEventKind.Placed, features.Count - 1, f, -1, 0, f.Pos);
            return features.Count - 1;
        }

        void SinkFeatures(float dt)
        {
            for (int i = features.Count - 1; i >= 0; i--)
            {
                var f = features[i];
                if (f.Sink <= 0) continue;
                f.Pos.y -= f.Sink * dt;
                if (Terrain != null && f.Pos.y < Terrain.Sample(f.Pos.x, f.Pos.z) - 3f) TakeFeature(i);
            }
        }

        public bool RemoveFeature(int index)
        {
            if (index < 0 || index >= features.Count) return false;
            TakeFeature(index);
            return true;
        }

        void TakeFeature(int index)
        {
            NoteFeature(FeatureEventKind.Removed, index, features[index], -1, 0, features[index].Pos);
            features.RemoveAt(index);
        }

        // The mock keeps a saved map for the session: its ground, features
        // and a new entry in Maps.
        public bool SaveMap(string name)
        {
            if (Terrain == null || string.IsNullOrWhiteSpace(name)) return false;
            // A shipped map is never replaced, only the mock's own saves.
            if (maps.Exists(m => string.Equals(m.Id, name, StringComparison.OrdinalIgnoreCase)) && !savedMaps.ContainsKey(name)) return false;
            var t = Terrain;
            var copy = new MapTerrain
            {
                HeightsW = t.HeightsW, HeightsH = t.HeightsH, CellSize = t.CellSize, Heights = (float[])t.Heights.Clone(),
                SeaLevel = t.SeaLevel, BlocksW = t.BlocksW, BlocksH = t.BlocksH, BlockSize = t.BlockSize,
                BlockTexels = t.BlockTexels, ChunkCount = t.ChunkCount, Blocks = (int[])t.Blocks.Clone(),
            };
            var info = new MapInfo { Id = name, Name = name, Description = "Made in the map editor.", MaxPlayers = map.MaxPlayers, Size = map.Size, Climate = map.Climate, Starts = map.Starts };
            savedMaps[name] = (copy, new List<Feature>(features), new Dictionary<uint, int>(libraryChunk));
            maps.RemoveAll(m => m.Id == name);
            maps.Add(info);
            return true;
        }

        public RgbaImage TerrainChunk(int chunk)
        {
            if (Terrain == null || chunk < 0 || chunk >= Terrain.ChunkCount) return null;
            if (!chunks.TryGetValue(chunk, out var img))
            {
                // A painted chunk comes from the library, the rest from the land.
                foreach (var kv in libraryChunk) if (kv.Value == chunk) return chunks[chunk] = ChunkPicture(kv.Key);
                chunks[chunk] = img = new MockTerrainGen(map).Chunk(Terrain, chunk);
            }
            return img;
        }

        static MapTerrain Clone(MapTerrain t) => new MapTerrain
        {
            HeightsW = t.HeightsW, HeightsH = t.HeightsH, CellSize = t.CellSize, Heights = (float[])t.Heights.Clone(),
            SeaLevel = t.SeaLevel, BlocksW = t.BlocksW, BlocksH = t.BlocksH, BlockSize = t.BlockSize,
            BlockTexels = t.BlockTexels, ChunkCount = t.ChunkCount, Blocks = (int[])t.Blocks.Clone(),
        };

        public int ReadUnits(UnitState[] into)
        {
            int n = 0;
            foreach (var u in units)
            {
                if (n >= into.Length) break;
                if (aboard.Contains(u.Handle)) continue;
                var f = UnitFlags.Active;
                if (u.Dying) f = UnitFlags.Dying;
                if (u.Built < 1f) f |= UnitFlags.Building;
                if (u.Moving) f |= UnitFlags.Moving;
                if (u.Attacking) f |= UnitFlags.Attacking;
                if (u.Flying && !u.Dying) f |= UnitFlags.Airborne;
                into[n++] = new UnitState
                {
                    Handle = u.Handle, StableId = (uint)u.Handle, Def = u.Def, Player = IdOf(u.Player), Flags = f,
                    Position = u.Pos + Vector3.up * Lift(u), Heading = ContractHeading(u), Roll = u.Dying ? Mathf.Min(90f, u.DyingFor * 120f) : 0f,
                    Health = u.Health, MaxHealth = u.MaxHealth, BuildProgress = u.Built, Model = u.Model, Facing = u.Facing,
                    Mana = ManaOf(u.Handle), MaxMana = mana.ContainsKey(u.Handle) ? MageMana : 0,
                    Altitude = u.Alt, Speed = u.Dying ? 0f : u.Speed,
                };
            }
            return n;
        }

        // A building keeps its turn from where it stands at rest, facing
        // south as the engine's do.
        float ContractHeading(Unit u) => unitDefs[u.Def].IsBuilding ? Mathf.Repeat(u.Heading + 180f, 360f) : u.Heading;

        // As EngineBackend does: a ship afloat is drawn in the surface.
        float Lift(Unit u) => Terrain == null ? 0f : Afloat.Lift(unitDefs[u.Def], u.Pos.y, Terrain.SeaLevel);

        public int ReadFeatures(FeatureState[] into)
        {
            int n = 0;
            for (int i = 0; i < features.Count && n < into.Length; i++)
            {
                var f = features[i];
                var def = featureDefs[f.Def];
                into[n++] = new FeatureState
                {
                    Index = i, Def = f.Def, Position = f.Pos, Heading = f.Model < 0 ? FeatureState.PictureHeading : f.Heading, Model = f.Model, Sprite = f.Sprite,
                    SpriteBottom = 0, SpriteTop = def.Height, SpriteWidth = def.Footprint.x * 1.2f,
                    SpriteOffsetX = def.Footprint.x * 0.6f,
                };
            }
            return n;
        }

        public int ReadUnitPose(int handle, PiecePose[] into)
        {
            if (!byHandle.TryGetValue(handle, out var u)) return 0;
            var world = Matrix4x4.TRS(u.Pos + Vector3.up * Lift(u), Quaternion.Euler(0, u.Heading, u.Dying ? Mathf.Min(90f, u.DyingFor * 120f) : 0), Vector3.one);
            string anim = u.Attacking ? "attack" : u.Alt > 0f ? "fly" : u.Moving ? "walk" : "idle";
            float t = u.Attacking ? u.AttackPhase : u.Alt > 0f ? u.FlyTime : u.WalkPhase;
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
            if (objectName != null && modelById.TryGetValue((objectName, colour), out int known)) return known;
            string key = objectName + "#" + colour;
            if (modelIds.TryGetValue(key, out int id)) { if (objectName != null) modelById[(objectName, colour)] = id; return id; }
            var m = MockModels.Build(objectName, Palette[Mathf.Abs(colour) % Palette.Length]);
            if (m == null) return -1;
            models.Add(m);
            modelIds[key] = models.Count - 1;
            if (objectName != null) modelById[(objectName, colour)] = models.Count - 1;
            return models.Count - 1;
        }

        public ModelData GetModel(int model) => model >= 0 && model < models.Count ? models[model] : null;
        public RgbaImage Texture(int texture) => texture >= 0 && texture < textures.Count ? textures[texture] : null;
        public RgbaImage Sprite(int sprite) => sprite >= 0 && sprite < sprites.Count ? sprites[sprite] : null;
        // The stand-in world has no game files to paint from.
        public RgbaImage PaintPicture(string kind, string name, string world) => null;

        Matrix4x4[] poseScratch = new Matrix4x4[64];

        public int PoseModel(int model, string animation, float seconds, PiecePose[] into)
        {
            var m = GetModel(model);
            if (m == null) return 0;
            int n = Mathf.Min(into.Length, m.Pieces.Length);
            if (poseScratch.Length < n) poseScratch = new Matrix4x4[Mathf.NextPowerOfTwo(n)];
            var local = poseScratch;
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

        // Shift's Queue waits behind what the unit is doing, Ctrl's Keep
        // replaces the order in hand and leaves the queue behind it, and
        // anything else replaces them all. A factory keeps its own queue
        // and rally point.
        public bool Command(in GameCommand c)
        {
            if (Status != GameStatus.Running || !byHandle.TryGetValue(c.Unit, out var u) || u.Dying) return false;
            if (unitDefs[u.Def].IsBuilding) return FactoryCommand(u, c);
            if (u.Built < 1f) return false;
            if (c.Queue && c.Kind != CommandKind.Stop && Busy(u))
            {
                if (!CanTake(u, c)) return false;
                Enqueue(u.Handle, new Pending { Command = c });
                return true;
            }
            if (c.Keep && c.Kind != CommandKind.Stop && pending.TryGetValue(c.Unit, out var kept) && kept.Count > 0)
            {
                var rest = new List<Pending>(kept);
                LetGo(c.Unit);
                bool ok = Apply(u, c);
                pending[c.Unit] = rest;
                return ok;
            }
            LetGo(c.Unit);
            return Apply(u, c);
        }

        bool CanTake(Unit u, in GameCommand c)
        {
            var def = unitDefs[u.Def];
            switch (c.Kind)
            {
                case CommandKind.Move: case CommandKind.Patrol: return true;
                case CommandKind.Attack: return byHandle.ContainsKey(c.TargetUnit);
                case CommandKind.Build: case CommandKind.FactoryEnqueue:
                    return Array.IndexOf(def.BuildOptions, c.BuildDef) >= 0
                        && (c.Kind != CommandKind.Build || !unitDefs[c.BuildDef].IsBuilding || CanBuildAt(c.BuildDef, c.Target, c.Facing, out _));
                default: return false;
            }
        }

        // Carries an order out now, for a unit that is not a building.
        bool Apply(Unit u, in GameCommand c)
        {
            var def = unitDefs[u.Def];
            switch (c.Kind)
            {
                case CommandKind.Move:
                case CommandKind.Patrol:
                    u.Goal = new Vector2(c.Target.x, c.Target.z);
                    u.Home = u.Goal.Value;
                    u.Target = -1;
                    u.Ordered = true;
                    u.OrderKind = c.Kind == CommandKind.Patrol ? OrderKind.Patrol : OrderKind.Move;
                    return true;
                case CommandKind.Attack:
                    if (!byHandle.ContainsKey(c.TargetUnit)) return false;
                    u.Target = c.TargetUnit;
                    u.Goal = null;
                    u.Ordered = true;
                    return true;
                case CommandKind.Stop:
                    u.Goal = null;
                    u.Target = -1;
                    u.BuildDef = -1;
                    u.Ordered = false;
                    u.Home = new Vector2(u.Pos.x, u.Pos.z);
                    return true;
                case CommandKind.Build:
                case CommandKind.FactoryEnqueue:
                    // One thing at a time: a new order replaces the last.
                    if (Array.IndexOf(def.BuildOptions, c.BuildDef) < 0) return false;
                    if (c.Kind == CommandKind.Build && unitDefs[c.BuildDef].IsBuilding)
                    {
                        if (!CanBuildAt(c.BuildDef, c.Target, c.Facing, out var site)) return false;
                        u.BuildAt = site;
                        u.BuildFacing = c.Facing;
                    }
                    else u.BuildAt = null;
                    u.BuildDef = c.BuildDef;
                    u.BuildLeft = 5f;
                    return true;
                case CommandKind.FactoryDequeue:
                case CommandKind.FactoryCancel:
                    if (c.BuildDef >= 0 && u.BuildDef != c.BuildDef) return false;
                    u.BuildDef = -1;
                    return true;
                default:
                    return false;
            }
        }

        public bool DoAction(string id, Vector3 at, int unit, Rect area, bool queue) => DoMockAction(id, at, unit, area, queue);
        public RgbaImage ActionPicture(int picture) => null;

        // The mock is silent.
        public bool SetAudio(float volume, bool music) => false;
        public int AudioPumps { get; private set; }
        public void PumpAudio() => AudioPumps++;
        public void SetView(Vector3 centre, float width, float depth) { }

        public Economy ReadEconomy(int player) => PosOf(player) is int k && k >= 0 && k < economy.Count ? economy[k] : default;


        public string UnitAnimation(int handle)
        {
            if (!byHandle.TryGetValue(handle, out var u) || u.Dying) return "";
            if (u.Attacking) return "attack";
            if (u.Alt > 0f) return u.Flying ? "fly" : "land";
            if (u.Moving) return "walk";
            return "";
        }
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
                if (byHandle.TryGetValue(h, out var u) && !u.Dying && u.Player == 0 && Selectable(u) && !mockSelection.Contains(h))
                    mockSelection.Add(h);
        }

        public int ReadSelection(int[] into)
        {
            mockSelection.RemoveAll(h => !byHandle.TryGetValue(h, out var u) || u.Dying);
            for (int i = 0; into != null && i < mockSelection.Count && i < into.Length; i++) into[i] = mockSelection[i];
            return mockSelection.Count;
        }

        public void Click(Vector3 at, int unit, bool shift, bool keep = false)
        {
            byHandle.TryGetValue(unit, out var hit);
            bool friend = hit != null && hit.Player == 0;
            if (friend && !Selectable(hit) && !mockIsArmed) return;
            if (mockIsArmed)
            {
                mockIsArmed = false;
                foreach (int h in mockSelection.ToArray())
                    Command(new GameCommand { Kind = mockArmed, Unit = h, Target = at, TargetUnit = unit, BuildDef = mockArmedDef, Facing = mockArmedFacing, Queue = shift, Keep = keep });
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
                var c = unit >= 0
                    ? new GameCommand { Kind = CommandKind.Attack, Unit = h, TargetUnit = unit, BuildDef = -1 }
                    : GameCommand.To(CommandKind.Move, h, at);
                c.Queue = shift;
                c.Keep = keep;
                Command(c);
            }
        }

        // The original's rules, simply: an armed command shows its own
        // pointer, an enemy the sword once something is selected, any other
        // unit the select hand.
        public GameCursor CursorAt(Vector3 at, int unit, out bool siteClear)
        {
            siteClear = false;
            if (mockIsArmed)
            {
                if (mockArmed == CommandKind.Build) { siteClear = CanBuildAt(mockArmedDef, at, mockArmedFacing, out _); return GameCursor.Place; }
                return GameCursors.For(mockArmed);
            }
            if (unit < 0 || !byHandle.TryGetValue(unit, out var u) || u.Dying) return GameCursor.Normal;
            return u.Player != 0 && mockSelection.Count > 0 ? GameCursor.Attack : GameCursor.Select;
        }

        public CursorFrame[] CursorArt(GameCursor cursor) => null;

        public void Cancel()
        {
            if (mockIsArmed) mockIsArmed = false;
            else mockSelection.Clear();
        }

        int mockArmedFacing;

        public void Arm(CommandKind kind, int buildDef = -1, int facing = 0)
        {
            mockArmed = kind;
            mockArmedDef = buildDef;
            mockArmedFacing = facing;
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
            return AddGroup(group);
        }

        public int AddGroup(int group)
        {
            if (mockGroups.TryGetValue(group, out var g))
                foreach (int h in g)
                    if (byHandle.TryGetValue(h, out var u) && !u.Dying && !mockSelection.Contains(h)) mockSelection.Add(h);
            return mockSelection.Count;
        }

        public int SelectBy(SelectKind kind, string category = null, bool add = false)
        {
            var types = new HashSet<int>();
            foreach (int h in mockSelection)
                if (byHandle.TryGetValue(h, out var s) && s.Player == 0) types.Add(s.Def);
            if (kind == SelectKind.Category && !add) mockSelection.Clear();
            foreach (var u in units)
            {
                if (u.Player != 0 || u.Dying || u.Built < 1f || mockSelection.Contains(u.Handle)) continue;
                bool take = kind == SelectKind.All
                    || (kind == SelectKind.SameType && types.Contains(u.Def))
                    || (kind == SelectKind.Category && HasCategory(unitDefs[u.Def].Category, category));
                if (take) mockSelection.Add(u.Handle);
            }
            return mockSelection.Count;
        }

        static bool HasCategory(string line, string word)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(word)) return false;
            foreach (var w in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

        // ---- HUD helpers ----

        public bool CanRotate(int def) => def >= 0 && def < unitDefs.Count && unitDefs[def].IsBuilding;

        // The cells a building of def covers at a centre and facing: odd
        // facings swap its width and depth.
        public static RectInt FootprintAt(Vector2Int footprint, int facing, Vector3 centre)
        {
            var size = (facing & 1) == 1 ? new Vector2Int(footprint.y, footprint.x) : footprint;
            int x0 = Mathf.RoundToInt(centre.x - size.x / 2f), z0 = Mathf.RoundToInt(-centre.z - size.y / 2f);
            return new RectInt(x0, z0, size.x, size.y);
        }

        // Where a building stands: its footprint's cells, rows running south.
        public RectInt Occupied(int handle)
        {
            if (!byHandle.TryGetValue(handle, out var u) || !unitDefs[u.Def].IsBuilding) return new RectInt();
            return FootprintAt(unitDefs[u.Def].Footprint, u.Facing, u.Pos);
        }

        public bool CanBuildAt(int def, Vector3 at, int facing, out Vector3 snapped)
        {
            snapped = at;
            if (Terrain == null || def < 0 || def >= unitDefs.Count) return false;
            var d = unitDefs[def];
            if (!CanRotate(def)) facing = 0;
            var size = (facing & 1) == 1 ? new Vector2Int(d.Footprint.y, d.Footprint.x) : d.Footprint;
            // Snap so the footprint sits on whole cells.
            float cx = size.x % 2 == 0 ? Mathf.Round(at.x) : Mathf.Floor(at.x) + 0.5f;
            float cz = size.y % 2 == 0 ? Mathf.Round(at.z) : Mathf.Floor(at.z) + 0.5f;
            snapped = new Vector3(cx, Terrain.Sample(cx, cz), cz);
            if (Terrain.SeaLevel > 0 && snapped.y < Terrain.SeaLevel + 0.2f) return false;
            var rect = FootprintAt(d.Footprint, facing, snapped);
            foreach (var u in units)
            {
                if (u.Dying) continue;
                if (unitDefs[u.Def].IsBuilding)
                {
                    if (FootprintAt(unitDefs[u.Def].Footprint, u.Facing, u.Pos).Overlaps(rect)) return false;
                }
                else if (u.Alt <= 0f && rect.Contains(new Vector2Int(Mathf.FloorToInt(u.Pos.x), Mathf.FloorToInt(-u.Pos.z)))) return false;
            }
            return true;
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

        bool[] sight;

        public bool SeesAll { get; private set; }
        public void SeeAll(bool on) => SeesAll = on;

        public bool PlaysOn { get; private set; }

        public bool PlayOn()
        {
            if (Status == GameStatus.Defeat && !PlaysOn) PlaysOn = SidesAtWar();
            return PlaysOn;
        }

        bool SidesAtWar()
        {
            for (int a = 0; a < players.Count; a++)
                for (int c = a + 1; c < players.Count; c++)
                    if (players[a].Alive && players[c].Alive && !SeatTeam.Allied(players[a].Team, players[c].Team)) return true;
            return false;
        }

        // For tests: every unit of a player falls at once.
        public void Rout(int player)
        {
            foreach (var u in units)
                if (IdOf(u.Player) == player) Hurt(u, u.Health + 1);
        }

        public int ReadFog(byte[] into, out int width, out int height)
        {
            width = Terrain != null ? Terrain.HeightsW : 0;
            height = Terrain != null ? Terrain.HeightsH : 0;
            int need = width * height;
            if (into == null || into.Length < need || need == 0) return need;
            Look();
            bool revealed = SeesAll || setup != null && setup.MapRevealed;
            bool lineOfSight = setup == null || setup.LineOfSight;
            for (int i = 0; i < need; i++)
            {
                bool inSight = revealed || sight[i];
                // Seen before is dimmed only with line of sight on, as the
                // original's; with it off, explored ground stays clear.
                into[i] = inSight ? (byte)2 : fogSeen[i] == 0 ? (byte)0 : lineOfSight ? (byte)1 : (byte)2;
            }
            return need;
        }

        // Marks ground within a radius as seen before, for tests.
        public void Explore(Vector3 centre, float radius)
        {
            if (Terrain == null) return;
            int width = Terrain.HeightsW, height = Terrain.HeightsH;
            if (fogSeen.Length != width * height) fogSeen = new byte[width * height];
            float cell = Terrain.CellSize;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = x * cell - centre.x, dz = -y * cell - centre.z;
                    if (dx * dx + dz * dz <= radius * radius) fogSeen[y * width + x] = 1;
                }
        }

        // What the local player's units see now, remembered as seen. The
        // simulation looks a few times a second, so ground passed between
        // reads is remembered too.
        void Look()
        {
            if (Terrain == null) return;
            int width = Terrain.HeightsW, height = Terrain.HeightsH, need = width * height;
            if (need == 0) return;
            if (fogSeen.Length != need) fogSeen = new byte[need];
            bool revealed = setup != null && setup.MapRevealed;
            float cell = Terrain.CellSize;
            // Sight is ten units round each of the player's units, stamped
            // cell by cell around each, rather than every unit for every cell.
            if (sight == null || sight.Length != need) sight = new bool[need];
            System.Array.Clear(sight, 0, need);
            if (!revealed)
            {
                int r = Mathf.CeilToInt(10f / cell);
                foreach (var u in units)
                {
                    if (u.Dying || u.Player != 0) continue;
                    int cx = Mathf.RoundToInt(u.Pos.x / cell), cy = Mathf.RoundToInt(-u.Pos.z / cell);
                    for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(height - 1, cy + r); y++)
                        for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(width - 1, cx + r); x++)
                        {
                            float dx = x * cell - u.Pos.x, dy = -y * cell - u.Pos.z;
                            if (dx * dx + dy * dy < 100f) sight[y * width + x] = true;
                        }
                }
            }
            for (int i = 0; i < need; i++)
                if (revealed || sight[i]) fogSeen[i] = 1;
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
