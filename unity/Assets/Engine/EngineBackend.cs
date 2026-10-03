// EngineBackend.cs - IGameBackend on the real OpenKingdoms engine
// (okengine), for the presentation's screens. Converts the engine's space
// (pixels, z south, heading 0 facing south) to the contract's (cells, z
// north, heading 0 facing north).
using System;
using System.Collections.Generic;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineBackend : IGameBackend
    {
        const float S = EngineSettings.PxToUnits;

        readonly List<MapInfo> maps = new List<MapInfo>();
        readonly List<SideInfo> sides = new List<SideInfo>();
        readonly List<UnitDef> unitDefs = new List<UnitDef>();
        readonly List<FeatureDef> featureDefs = new List<FeatureDef>();
        readonly List<PlayerInfo> players = new List<PlayerInfo>();
        readonly Dictionary<string, int> mapIndex = new Dictionary<string, int>();
        readonly Dictionary<int, (int def, int colour)> modelSource = new Dictionary<int, (int def, int colour)>();
        OkxProjectile[] projBuf = new OkxProjectile[256];
        OkxEffect[] effectBuf = new OkxEffect[256];
        readonly EngineFx fx = new EngineFx(StripSize);
        uint fxTick = uint.MaxValue;
        readonly int[] buildBuf = new int[256];
        readonly Dictionary<int, ModelData> modelCache = new Dictionary<int, ModelData>();
        OkxUnit[] unitBuf = new OkxUnit[1024];
        OkxFeature[] featureBuf = new OkxFeature[1024];
        readonly float[] pose = new float[128 * 12];
        readonly byte[] hidden = new byte[128];
        SkirmishSetup pending;
        string pendingSave;
        bool loadStarted;
        readonly byte[] statusBuf = new byte[128];
        GameStatus status = GameStatus.Idle;
        MapTerrain terrain;
        // How far each unit afloat is drawn above the ground under it, by
        // handle, from the last ReadUnits. Its pose is lifted to match.
        readonly Dictionary<int, float> lifts = new Dictionary<int, float>();

        public string Name => "OpenKingdoms";

        public EngineBackend()
        {
            OkEngine.PreloadDependencies(EngineSettings.PluginDir);
            if (OkEngine.okx_api_version() != OkEngine.ApiVersion)
                throw new InvalidOperationException($"okengine API {OkEngine.okx_api_version()}, this binding expects {OkEngine.ApiVersion}");
            OkEngine.okx_set_user_dir(EngineSettings.UserDir.Replace('\\', '/'));
            if (OkEngine.okx_init(EngineSettings.GameDir, EngineSettings.DataDir) != 0)
                throw new InvalidOperationException("okx_init: " + OkEngine.LastError);
            OkEngine.okx_set_override_dir(EngineSettings.OverrideDir.Replace('\\', '/'));
            ReadMaps();
            string[,] kingdoms =
            {
                { "ARAMON", "Aramon" }, { "VERUNA", "Veruna" }, { "TAROS", "Taros" },
                { "ZHON", "Zhon" }, { "CREON", "Creon" }
            };
            for (int i = 0; i < kingdoms.GetLength(0); i++)
                sides.Add(new SideInfo { Id = kingdoms[i, 0], Name = kingdoms[i, 1], Description = "" });
        }

        void ReadMaps()
        {
            int mapCount = OkEngine.okx_map_count();
            for (int i = 0; i < mapCount; i++)
            {
                if (OkEngine.okx_map_info(i, out var mi) != 0) continue;
                // Start positions in cells, StartPos1 first.
                int n = OkEngine.okx_map_starts(i, startsBuf, startsBuf.Length / 2);
                var starts = new Vector2[Mathf.Clamp(n, 0, startsBuf.Length / 2)];
                for (int k = 0; k < starts.Length; k++) starts[k] = new Vector2(startsBuf[2 * k], startsBuf[2 * k + 1]);
                maps.Add(new MapInfo
                {
                    Id = mi.name, Name = mi.name, Description = mi.description ?? "",
                    MaxPlayers = Mathf.Max(2, mi.maxPlayers),
                    // The engine gives the size in cells.
                    Size = new Vector2(mi.sizeX, mi.sizeY), Climate = Climate(mi.kingdom), Starts = starts
                });
                mapIndex[mi.name] = i;
            }
        }

        readonly int[] startsBuf = new int[16];

        public IReadOnlyList<MapInfo> Maps => maps;
        public IGameRooms Rooms { get; } = new EngineRooms();

        // The game's interface art through the engine, in the palette its
        // own screens use, or from an unpacked data folder when the engine
        // has none.
        public ArtFrame InterfaceArt(string gaf, string entry, int frame)
        {
            int need = OkEngine.okx_gui_art(gaf, entry, frame, null, 0, out int w, out int h, out _, out _, out _);
            if (need > 0)
            {
                var px = new byte[need];
                if (OkEngine.okx_gui_art(gaf, entry, frame, px, need, out w, out h, out int ox, out int oy, out int frames) == need)
                {
                    // Both run top row first.
                    var img = new RgbaImage(w, h);
                    Array.Copy(px, img.Pixels, need);
                    return new ArtFrame { Image = img, Origin = new Vector2Int(ox, oy), Frames = frames };
                }
            }
            string d = EngineSettings.DataDir;
            return string.IsNullOrEmpty(d) ? null : GafFiles.Read(System.IO.Path.Combine(d, "data", "anims"), gaf, entry, frame);
        }
        public IReadOnlyList<SideInfo> Sides => sides;
        public IReadOnlyList<UnitDef> UnitDefs => unitDefs;
        public IReadOnlyList<FeatureDef> FeatureDefs => featureDefs;
        // The land's look by the map's kingdom, for sky, weather and water.
        static string Climate(string kingdom)
        {
            switch ((kingdom ?? "").ToLowerInvariant())
            {
                case "aramon": return "grass";
                case "veruna": return "grass";
                case "taros": return "volcanic";
                case "zhon": return "swamp";
                case "creon": return "desert";
                default: return "";
            }
        }

        public RgbaImage MapPreview(string mapId, int maxSize)
        {
            if (mapId == null || !mapIndex.TryGetValue(mapId, out int i)) return null;
            int need = OkEngine.okx_map_preview(i, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_map_preview(i, img.Pixels, need, out w, out h);
            return img;
        }

        // ── A game ─────────────────────────────────────────────────────

        public void StartSkirmish(SkirmishSetup setup)
        {
            EndGame();
            pending = setup;
            pendingSave = null;
            loadStarted = false;
            // A room's match is built from what the relay described.
            netMatch = Rooms.State == RoomSession.Loading;
            status = GameStatus.Loading;
        }

        bool netMatch;

        // Saved games, through the engine's own writer and the same sliced
        // loading screen as a new battle.
        public bool SaveGame(string path) => status == GameStatus.Running && OkEngine.okx_save(path) == 0;

        public bool LoadGame(string path)
        {
            if (OkEngine.okx_save_info(path, out _) != 0) return false;
            EndGame();
            pendingSave = path;
            pending = new SkirmishSetup();
            loadStarted = false;
            status = GameStatus.Loading;
            return true;
        }

        public bool SaveInfo(string path, out string map, out uint tick, out DateTime savedAt)
        {
            map = null; tick = 0; savedAt = default;
            if (OkEngine.okx_save_info(path, out var info) != 0) return false;
            map = info.map;
            tick = info.tick;
            savedAt = DateTimeOffset.FromUnixTimeSeconds((long)info.savedAt).UtcDateTime;
            return true;
        }

        // How long one PumpLoading call may work, so the loading screen
        // keeps drawing while the engine loads.
        public int LoadSliceMs = 12;

        public LoadProgress PumpLoading()
        {
            if (status != GameStatus.Loading || pending == null)
                return new LoadProgress { Fraction = 1, Done = status == GameStatus.Running, Failed = status == GameStatus.Failed };
            var setup = pending;
            if (loadStarted)
            {
                int rc = OkEngine.okx_load_step(LoadSliceMs, out float progress, statusBuf, statusBuf.Length);
                string stage = System.Text.Encoding.ASCII.GetString(statusBuf, 0, Math.Max(0, Array.IndexOf(statusBuf, (byte)0)));
                if (rc == 0) return new LoadProgress { Fraction = progress, Stage = stage };
                pending = null;
                if (rc < 0)
                {
                    status = GameStatus.Failed;
                    return new LoadProgress { Fraction = 1, Failed = true, Error = OkEngine.LastError, Stage = "failed" };
                }
                ReadCatalogue();
                ReadTerrain();
                ReadPlayers(setup);
                status = GameStatus.Running;
                return new LoadProgress { Fraction = 1, Done = true, Stage = "ready" };
            }
            if (pendingSave != null)
            {
                if (OkEngine.okx_load_save_begin(pendingSave) != 0)
                {
                    pending = null;
                    status = GameStatus.Failed;
                    return new LoadProgress { Fraction = 1, Failed = true, Error = OkEngine.LastError, Stage = "failed" };
                }
                loadStarted = true;
                return new LoadProgress { Fraction = 0, Stage = "starting" };
            }
            if (netMatch)
            {
                if (OkEngine.okx_net_load_begin() != 0)
                {
                    pending = null;
                    status = GameStatus.Failed;
                    return new LoadProgress { Fraction = 1, Failed = true, Error = OkEngine.LastError, Stage = "failed" };
                }
                loadStarted = true;
                return new LoadProgress { Fraction = 0, Stage = "starting" };
            }
            var cfg = OkxSkirmish.For(setup.MapId);
            cfg.lineOfSight = setup.LineOfSight ? 1 : 0;
            cfg.mapRevealed = setup.MapRevealed ? 1 : 0;
            cfg.seed = setup.Seed;
            cfg.unitsPerSide = setup.UnitLimit;
            cfg.randomStartLocations = setup.RandomStarts ? 1 : 0;
            cfg.monarchExpendable = setup.MonarchExpendable ? 1 : 0;
            // The lobby's lineup seat by seat. The engine plays seat 0 as
            // the local player.
            cfg.seatCount = Mathf.Min(setup.Seats.Count, cfg.seats.Length);
            for (int i = 0; i < cfg.seatCount; i++)
            {
                var seat = setup.Seats[i];
                cfg.seats[i] = new OkxSeat
                {
                    kind = seat.Kind == SeatKind.Closed ? 0 : seat.Kind == SeatKind.Human ? 1 : 2,
                    side = SideIndex(seat.Side), team = EngineTeam(seat.Team, i), color = seat.Colour,
                    difficulty = (int)seat.Difficulty, start = seat.Start
                };
            }
            if (OkEngine.okx_load_begin(ref cfg) != 0)
            {
                pending = null;
                status = GameStatus.Failed;
                return new LoadProgress { Fraction = 1, Failed = true, Error = OkEngine.LastError, Stage = "failed" };
            }
            loadStarted = true;
            return new LoadProgress { Fraction = 0, Stage = "starting" };
        }

        void ReadCatalogue()
        {
            unitDefs.Clear();
            int n = OkEngine.okx_def_count();
            for (int i = 0; i < n; i++)
            {
                if (OkEngine.okx_def_info(i, out var d) != 0) continue;
                var def = new UnitDef
                {
                    Id = i, Name = d.name, Title = d.displayName ?? "", ObjectName = d.obj, Side = d.side, Category = d.category,
                    Description = d.description, MaxHealth = d.maxHealth, IsBuilding = d.isBuilding != 0,
                    Footprint = new Vector2Int(d.footprintX, d.footprintZ), ManaCost = d.buildCost,
                    BuildOptions = Buildables(i), Animations = OkEngine.Scripts(i)
                };
                // Until the engine reports canfly: a script with a flight loop
                // flies, and one that never begins a flight hovers.
                bool takesOff = Array.Exists(def.Animations, a => string.Equals(a, "BeginFlight", StringComparison.OrdinalIgnoreCase));
                def.CanFly = takesOff || Array.Exists(def.Animations, a => string.Equals(a, "FlightControl", StringComparison.OrdinalIgnoreCase));
                def.Hovers = def.CanFly && !takesOff;
                if (d.floater != 0) def.Waterline = d.waterline * S;
                unitDefs.Add(def);
            }
            featureDefs.Clear();
            int nf = OkEngine.okx_feature_def_count();
            for (int i = 0; i < nf; i++)
            {
                if (OkEngine.okx_feature_def_info(i, out var f) != 0) continue;
                featureDefs.Add(new FeatureDef
                {
                    Id = i, Name = f.name, ObjectName = f.obj ?? "",
                    SequenceName = string.IsNullOrEmpty(f.obj) ? f.seqname ?? "" : "",
                    Category = f.category, Footprint = new Vector2Int(f.footprintX, f.footprintZ),
                    Height = f.height * S
                });
            }
        }

        int[] Buildables(int def)
        {
            int n = OkEngine.okx_def_buildables(def, buildBuf, buildBuf.Length);
            if (n <= 0) return Array.Empty<int>();
            var r = new int[Mathf.Min(n, buildBuf.Length)];
            Array.Copy(buildBuf, r, r.Length);
            return r;
        }

        void ReadTerrain()
        {
            if (OkEngine.okx_terrain_info(out var t) != 0) { terrain = null; return; }
            var heights = new float[t.heightsW * t.heightsH];
            OkEngine.okx_terrain_heights(heights, heights.Length);
            for (int i = 0; i < heights.Length; i++) heights[i] *= S;
            int nb = t.blocksW * t.blocksH;
            var blocks = new int[nb * 3];
            OkEngine.okx_terrain_blocks(blocks, blocks.Length);
            for (int b = 0; b < nb; b++)
            {
                blocks[3 * b + 1] *= t.subPx;
                blocks[3 * b + 2] *= t.subPx;
            }
            terrain = new MapTerrain
            {
                HeightsW = t.heightsW, HeightsH = t.heightsH, CellSize = t.tilePx * S, Heights = heights,
                SeaLevel = t.waterHeight > 0 ? t.waterHeight * S : -1f,
                BlocksW = t.blocksW, BlocksH = t.blocksH, BlockSize = t.blockPx * S, BlockTexels = t.subPx,
                ChunkCount = t.chunkCount, Blocks = blocks
            };
        }

        internal static readonly string[] SideIds = { "ARAMON", "TAROS", "VERUNA", "ZHON", "", "", "", "CREON" };

        // A side's engine number, -1 for random.
        internal static int SideIndex(string side)
        {
            if (string.IsNullOrEmpty(side)) return -1;
            for (int i = 0; i < SideIds.Length; i++)
                if (SideIds[i].Length > 0 && string.Equals(SideIds[i], side, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void ReadPlayers(SkirmishSetup setup)
        {
            players.Clear();
            int me = OkEngine.okx_local_player();
            var seats = new OkxPlayer[8];
            int n = Mathf.Min(OkEngine.okx_players(seats, seats.Length), seats.Length);
            for (int i = 0; i < n; i++)
            {
                var s = seats[i];
                players.Add(new PlayerInfo
                {
                    Index = s.index, Name = s.name,
                    Side = s.side >= 0 && s.side < SideIds.Length ? SideIds[s.side] : "",
                    Colour = s.color, Team = PlayerTeam(s.team), IsLocal = s.index == me, IsComputer = s.kind == 2,
                    Alive = s.alive != 0, Tint = TeamTint(s.color)
                });
            }
        }

        // TAK_MAX_PLAYERS, the most seats and teams the engine plays.
        const int EngineSeats = 8;

        // A skirmish seat's team as the engine takes it: Team 1 up, and a
        // kingdom alone a number past every team and every other seat's.
        // The embed gives team 0 its seat's number, which a team can share.
        public static int EngineTeam(int team, int seat) => team >= 0 && team < EngineSeats ? team + 1 : EngineSeats + 1 + seat;

        // A player's or a room seat's team as the engine has it: 0 and the
        // numbers past the teams are a kingdom alone.
        public static int PlayerTeam(int team) => team >= 1 && team <= EngineSeats ? team - 1 : SeatTeam.Alone;

        static Color32 TeamTint(int colour)
        {
            Color32[] tints =
            {
                new Color32(40, 90, 220, 255), new Color32(210, 40, 40, 255), new Color32(240, 240, 240, 255),
                new Color32(40, 170, 60, 255), new Color32(30, 40, 150, 255), new Color32(140, 60, 170, 255),
                new Color32(230, 210, 40, 255), new Color32(30, 30, 30, 255), new Color32(120, 200, 230, 255),
                new Color32(200, 170, 120, 255), new Color32(230, 130, 30, 255), new Color32(120, 80, 40, 255)
            };
            return tints[Mathf.Clamp(colour, 0, tints.Length - 1)];
        }

        public GameStatus Status
        {
            get
            {
                if (status != GameStatus.Running) return status;
                int o = OkEngine.okx_outcome();
                return o == 1 ? GameStatus.Victory : o == -1 ? GameStatus.Defeat : GameStatus.Running;
            }
        }

        public void EndGame()
        {
            if (status == GameStatus.Running || status == GameStatus.Victory || status == GameStatus.Defeat)
                OkEngine.okx_end_game();
            modelCache.Clear();
            modelSource.Clear();
            flights.Clear();
            nextFlightSweep = 0;
            fx.Reset();
            fxTick = uint.MaxValue;
            terrain = null;
            players.Clear();
            status = GameStatus.Idle;
        }

        public int LocalPlayer => OkEngine.okx_local_player();
        public IReadOnlyList<PlayerInfo> Players => players;
        public int TicksPerSecond => OkEngine.okx_tick_rate();
        public uint Tick => OkEngine.okx_tick_count();
        public int Advance(int n) => status == GameStatus.Running ? OkEngine.okx_tick(n) : 0;

        // ── The ground ─────────────────────────────────────────────────

        public MapTerrain Terrain => terrain;

        public float GroundHeight(float x, float z) => OkEngine.okx_ground_height(x / S, -z / S) * S;

        public RgbaImage TerrainChunk(int chunk)
        {
            int need = OkEngine.okx_terrain_chunk(chunk, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_terrain_chunk(chunk, img.Pixels, need, out w, out h);
            return img;
        }

        // ── Snapshots ──────────────────────────────────────────────────

        // Engine heading: radians, 0 facing north, turning clockwise seen
        // from above, as the contract's degrees turn.
        static float Heading(float radians) => Mathf.Repeat(radians * Mathf.Rad2Deg, 360f);

        // The engine does not report a flyer's altitude, speed or flying
        // state yet, so they are worked out here from where it stands, tick
        // to tick. A flyer losing height has begun to land. Its type's
        // cruise height and top speed are the most any of them has shown.
        struct Flight { public uint Tick; public float X, Z, Alt, Speed; public bool Down; }
        readonly Dictionary<uint, Flight> flights = new Dictionary<uint, Flight>();
        readonly List<uint> gone = new List<uint>();
        uint nextFlightSweep;

        void TrackFlight(in OkxUnit u, ref UnitState s, UnitDef def, uint tick, int tps)
        {
            float alt = Mathf.Max(0f, u.y - OkEngine.okx_ground_height(u.x, u.z));
            if (flights.TryGetValue(u.stableId, out var f))
            {
                if (tick != f.Tick)
                {
                    // Whole pixels a tick, so smoothed over about eight ticks.
                    uint ticks = Math.Max(1u, tick - f.Tick);
                    float now = new Vector2(u.x - f.X, u.z - f.Z).magnitude * S * tps / ticks;
                    f.Speed += (now - f.Speed) * (1f - Mathf.Exp(-ticks / 8f));
                    if (alt < f.Alt - 0.01f) f.Down = true;
                    else if (alt > f.Alt + 0.01f) f.Down = false;
                    f.Tick = tick; f.X = u.x; f.Z = u.z; f.Alt = alt;
                }
            }
            else f = new Flight { Tick = tick, X = u.x, Z = u.z, Alt = alt };
            flights[u.stableId] = f;
            s.Altitude = alt * S;
            s.Speed = f.Speed;
            if (s.Altitude > 0.02f && !f.Down && s.Flags == UnitFlags.Active)
            {
                s.Flags |= UnitFlags.Airborne;
                def.CruiseAltitude = Mathf.Max(def.CruiseAltitude, s.Altitude);
                def.MaxSpeed = Mathf.Max(def.MaxSpeed, s.Speed);
            }
            if (OkEngine.okx_unit_anim(u.handle, null, 0) == OkEngine.AnimAttacking) s.Flags |= UnitFlags.Attacking;
        }

        public int ReadUnits(UnitState[] into)
        {
            int n = OkEngine.okx_units(null, 0);
            if (unitBuf.Length < n) unitBuf = new OkxUnit[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_units(unitBuf, unitBuf.Length), unitBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            uint tick = Tick;
            int tps = Mathf.Max(1, TicksPerSecond);
            lifts.Clear();
            for (int i = 0; i < count; i++)
            {
                var u = unitBuf[i];
                var flags = u.state == OkEngine.UnitActive ? UnitFlags.Active : UnitFlags.Dying;
                if (u.building != 0) flags |= UnitFlags.Building;
                var at = EngineSettings.ToUnity(u.x, u.y, u.z);
                if (Floating(u.def, at, out float drawn, out float ground))
                {
                    at.y = drawn;
                    lifts[u.handle] = drawn - ground;
                }
                into[i] = new UnitState
                {
                    Handle = u.handle, StableId = u.stableId, Def = u.def, Player = u.player, Flags = flags,
                    Position = at,
                    Heading = Heading(u.heading), Pitch = u.pitch * Mathf.Rad2Deg, Roll = u.roll * Mathf.Rad2Deg,
                    Health = u.health, MaxHealth = u.maxHealth,
                    BuildProgress = u.building != 0 && u.maxHealth > 0 ? Mathf.Clamp01(u.health / (float)u.maxHealth) : 1f,
                    Model = u.model,
                    Facing = u.facing
                };
                if (OkEngine.okx_unit_mana(u.handle, out float mana, out float maxMana) == 0)
                {
                    into[i].Mana = Mathf.FloorToInt(mana);
                    into[i].MaxMana = Mathf.RoundToInt(maxMana);
                }
                if (u.model >= 0 && !modelSource.ContainsKey(u.model)) modelSource[u.model] = (u.def, u.color);
                if (u.def >= 0 && u.def < unitDefs.Count && unitDefs[u.def].CanFly) TrackFlight(u, ref into[i], unitDefs[u.def], tick, tps);
            }
            // Flyers not read for ten seconds of game time are forgotten,
            // looked for every five seconds whatever the game's speed.
            if (flights.Count > 0 && tick >= nextFlightSweep)
            {
                nextFlightSweep = tick + (uint)(tps * 5);
                gone.Clear();
                foreach (var kv in flights) if (tick - kv.Value.Tick > (uint)(tps * 10)) gone.Add(kv.Key);
                foreach (var id in gone) flights.Remove(id);
            }
            return n;
        }

        // A ship is drawn with its origin just under the surface and a
        // hovering unit on it, from the ground under them, whether the engine
        // reports them at the sea floor (before API 20) or at the sea.
        bool Floating(int def, Vector3 at, out float drawn, out float ground)
        {
            drawn = ground = at.y;
            if (terrain == null || terrain.SeaLevel <= 0 || def < 0 || def >= unitDefs.Count) return false;
            var kind = unitDefs[def].Float;
            if (kind == FloatKind.None) return false;
            ground = GroundHeight(at.x, at.z);
            drawn = Afloat.Height(kind, ground, terrain.SeaLevel);
            return true;
        }

        public int ReadFeatures(FeatureState[] into)
        {
            int n = OkEngine.okx_features(null, 0);
            if (featureBuf.Length < n) featureBuf = new OkxFeature[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_features(featureBuf, featureBuf.Length), featureBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var f = featureBuf[i];
                into[i] = new FeatureState
                {
                    Index = f.index, Def = f.def, Position = EngineSettings.ToUnity(f.x, f.y, f.z),
                    Heading = f.model < 0 ? FeatureState.PictureHeading : Heading(f.heading), Pitch = f.pitch * Mathf.Rad2Deg, Roll = f.roll * Mathf.Rad2Deg,
                    Model = f.model, Sprite = f.sprite,
                    SpriteTop = (f.top - f.y) * S, SpriteBottom = (f.bottom - f.y) * S,
                    SpriteOffsetX = f.offX * S, SpriteWidth = f.w * S, Flat = f.flat != 0
                };
            }
            return n;
        }

        public int ReadProjectiles(ProjectileState[] into)
        {
            RefreshFx();
            return fx.Projectiles(into);
        }

        public int ReadProjectilePose(int id, PiecePose[] into)
        {
            int nodes = OkEngine.okx_projectile_pose(id, pose, 128);
            return nodes > 0 ? WritePose(nodes, into, false) : 0;
        }

        // The engine's shots and effects, read once a tick and completed by
        // EngineFx with what API 20 does not report.
        void RefreshFx()
        {
            uint now = Tick;
            if (now == fxTick) return;
            fxTick = now;
            int np = OkEngine.okx_projectiles(null, 0);
            if (projBuf.Length < np) projBuf = new OkxProjectile[Mathf.NextPowerOfTwo(np)];
            np = Mathf.Min(OkEngine.okx_projectiles(projBuf, projBuf.Length), projBuf.Length);
            int ne = OkEngine.okx_effects(null, 0);
            if (effectBuf.Length < ne) effectBuf = new OkxEffect[Mathf.NextPowerOfTwo(ne)];
            ne = Mathf.Min(OkEngine.okx_effects(effectBuf, effectBuf.Length), effectBuf.Length);
            fx.Update(now, OkEngine.okx_tick_rate(), effectBuf, Mathf.Max(0, ne), projBuf, Mathf.Max(0, np));
        }

        static Vector2Int StripSize(int strip) =>
            OkEngine.okx_effect_strip(strip, null, 0, out int w, out int h) > 0 ? new Vector2Int(w, h) : Vector2Int.zero;

        int WritePose(int nodes, PiecePose[] into, bool withHidden)
        {
            int count = Mathf.Min(nodes, into?.Length ?? 0);
            for (int i = 0; i < count; i++)
                into[i] = new PiecePose { Matrix = EngineSettings.PoseToUnity(pose, i * 12), Hidden = withHidden && hidden[i] != 0 };
            return nodes;
        }

        public int ReadUnitPose(int handle, PiecePose[] into)
        {
            int nodes = OkEngine.okx_unit_pose(handle, pose, hidden, 128);
            if (nodes <= 0) return 0;
            WritePose(nodes, into, true);
            // The engine poses a unit on the ground under it, even a floater
            // it reports at the sea.
            if (into != null && lifts.TryGetValue(handle, out float lift) && lift != 0f)
            {
                var up = Matrix4x4.Translate(new Vector3(0, lift, 0));
                for (int i = 0; i < Mathf.Min(nodes, into.Length); i++) into[i].Matrix = up * into[i].Matrix;
            }
            return nodes;
        }

        readonly byte[] runningBuf = new byte[1024];

        // The function driving the pose: of the threads running now, a walk
        // while moving, an attack or a weapon while fighting, a build while
        // building. Watchers and control loops that always run never count.
        public string UnitAnimation(int handle)
        {
            int state = OkEngine.okx_unit_anim(handle, runningBuf, runningBuf.Length);
            if (state < 0 || state == OkEngine.AnimIdle || state == OkEngine.AnimDead) return "";
            int end = Array.IndexOf(runningBuf, (byte)0);
            var running = System.Text.Encoding.ASCII.GetString(runningBuf, 0, Math.Max(0, end))
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] wanted;
            switch (state)
            {
                case OkEngine.AnimMoving: wanted = new[] { "walk" }; break;
                case OkEngine.AnimAttacking: wanted = new[] { "attack", "fire", "aim", "melee" }; break;
                case OkEngine.AnimBuilding: wanted = new[] { "startbuild", "build" }; break;
                case OkEngine.AnimDying: wanted = new[] { "dying", "killed" }; break;
                default: wanted = Array.Empty<string>(); break;
            }
            foreach (var w in wanted)
                foreach (var r in running)
                    if (r.StartsWith(w, StringComparison.OrdinalIgnoreCase) &&
                        !r.EndsWith("control", StringComparison.OrdinalIgnoreCase) &&
                        !r.EndsWith("watcher", StringComparison.OrdinalIgnoreCase))
                        return r;
            return state == OkEngine.AnimMoving ? "walk" : "";
        }

        public int ReadFeaturePose(int index, PiecePose[] into)
        {
            int nodes = OkEngine.okx_feature_pose(index, pose, 128);
            return nodes > 0 ? WritePose(nodes, into, false) : 0;
        }

        // ── Art ────────────────────────────────────────────────────────

        public int LoadModel(string objectName, int colour)
        {
            int id = OkEngine.okx_model_load(objectName, colour);
            if (id >= 0 && !modelSource.ContainsKey(id))
            {
                foreach (var d in unitDefs)
                    if (string.Equals(d.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
                    {
                        modelSource[id] = (d.Id, colour);
                        break;
                    }
            }
            return id;
        }

        public ModelData GetModel(int model)
        {
            if (model < 0) return null;
            if (modelCache.TryGetValue(model, out var md)) return md;
            if (OkEngine.okx_model_info(model, out var info) != 0) return null;
            int V = info.vertCount;
            var pos = new float[V * 3];
            var nrm = new float[V * 3];
            var uv = new float[V * 2];
            var col = new uint[V];
            var node = new int[V];
            var idx = new int[info.indexCount];
            OkEngine.okx_model_geometry(model, pos, nrm, uv, col, node, idx);
            var nodes = new OkxNode[info.nodeCount];
            OkEngine.okx_model_nodes(model, nodes, nodes.Length);
            var batches = new OkxBatch[info.batchCount];
            OkEngine.okx_model_batches(model, batches, batches.Length);

            md = new ModelData
            {
                Name = model.ToString(),
                Positions = new Vector3[V], Normals = new Vector3[V], Uvs = new Vector2[V],
                Colors = new Color32[V], VertexPiece = node, Indices = idx,
                Pieces = new PieceInfo[info.nodeCount], Batches = new Batch[info.batchCount],
                Scale = info.scale * S, FromOverride = info.fromOverride != 0
            };
            var textured = new bool[V];
            for (int b = 0; b < batches.Length; b++)
            {
                md.Batches[b] = new Batch { FirstIndex = batches[b].firstIndex, IndexCount = batches[b].indexCount, Texture = batches[b].texture };
                if (batches[b].texture < 0) continue;
                for (int i = batches[b].firstIndex; i < batches[b].firstIndex + batches[b].indexCount && i < idx.Length; i++)
                    textured[idx[i]] = true;
            }
            for (int v = 0; v < V; v++)
            {
                md.Positions[v] = new Vector3(pos[3 * v], pos[3 * v + 1], pos[3 * v + 2]);
                md.Normals[v] = new Vector3(nrm[3 * v], nrm[3 * v + 1], nrm[3 * v + 2]);
                md.Uvs[v] = new Vector2(uv[2 * v], uv[2 * v + 1]);
                uint c = col[v];
                byte a = (byte)(c >> 24);
                // A textured vertex takes its colour from the picture.
                md.Colors[v] = textured[v] ? new Color32(255, 255, 255, a)
                                           : new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), a);
            }
            for (int n = 0; n < nodes.Length; n++)
                md.Pieces[n] = new PieceInfo { Name = nodes[n].name, Parent = nodes[n].parent, Offset = new Vector3(nodes[n].offX, nodes[n].offY, nodes[n].offZ) };
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(info.minX, info.minY, info.minZ), new Vector3(info.maxX, info.maxY, info.maxZ));
            md.Bounds = bounds;
            modelCache[model] = md;
            return md;
        }

        public RgbaImage Texture(int texture)
        {
            int need = OkEngine.okx_texture(texture, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_texture(texture, img.Pixels, need, out w, out h);
            return img;
        }

        public RgbaImage UnitPicture(int def)
        {
            int need = OkEngine.okx_unit_picture(def, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_unit_picture(def, img.Pixels, need, out w, out h);
            return img;
        }

        public RgbaImage Sprite(int sprite)
        {
            int need = OkEngine.okx_sprite(sprite, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_sprite(sprite, img.Pixels, need, out w, out h);
            return img;
        }

        public RgbaImage PaintPicture(string kind, string name, string world)
        {
            bool texture = kind == "texture";
            if (string.IsNullOrEmpty(name) || (!texture && kind != "feature")) return null;
            int need = texture ? OkEngine.okx_texture_by_name(name, world, null, 0, out int w, out int h)
                               : OkEngine.okx_sprite_by_name(name, world, null, 0, out w, out h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            if (texture) OkEngine.okx_texture_by_name(name, world, img.Pixels, need, out w, out h);
            else OkEngine.okx_sprite_by_name(name, world, img.Pixels, need, out w, out h);
            return img;
        }

        // A pose from the unit's own script, run in an engine of its own
        // so the battle does not move. The model stands at the origin.
        public int PoseModel(int model, string animation, float seconds, PiecePose[] into)
        {
            if (!modelSource.TryGetValue(model, out var src)) return 0;
            int ticks = Mathf.Max(0, Mathf.RoundToInt(seconds * OkEngine.okx_tick_rate()));
            int nodes = OkEngine.okx_studio_pose(src.def, src.colour, animation ?? "", ticks, pose, hidden, 128);
            return nodes > 0 ? WritePose(nodes, into, true) : 0;
        }

        // ── Orders ─────────────────────────────────────────────────────

        // A build order's arg is its facing, Queue puts the order behind
        // the ones the unit holds, as Shift does, and Keep puts it in place
        // of the one in hand and keeps the rest, as Ctrl does.
        public bool Command(in GameCommand c)
        {
            float x = c.Target.x / S, z = -c.Target.z / S;
            return OkEngine.okx_command((int)c.Kind, c.Unit, (int)x, (int)z, c.TargetUnit, c.BuildDef, CommandArg(c)) == 0;
        }

        public static int CommandArg(in GameCommand c)
        {
            int arg = c.Kind == CommandKind.Build ? (c.Facing & 3) : c.Arg;
            if (c.Queue) arg |= OkEngine.Queue;
            if (c.Keep) arg |= OkEngine.Keep;
            return arg;
        }

        public static int ClickFlags(bool shift, bool keep) => (shift ? OkEngine.ClickShift : 0) | (keep ? OkEngine.ClickCtrl : 0);

        // One order for them all, points in pixels as Command sends them and
        // the heading in engine radians, as Heading() reads them back.
        public bool MoveFormation(int[] units, Vector2[] targets, float? heading, bool groupSpeed, bool queue)
        {
            if (units == null || targets == null || units.Length != targets.Length || units.Length == 0) return false;
            int n = units.Length;
            var xy = new int[2 * n];
            for (int i = 0; i < n; i++)
            {
                xy[2 * i] = (int)(targets[i].x / S);
                xy[2 * i + 1] = (int)(-targets[i].y / S);
            }
            float rad = heading.HasValue ? heading.Value * Mathf.Deg2Rad : 0f;
            return OkEngine.okx_move_formation(units, xy, n, heading.HasValue ? 1 : 0, rad, groupSpeed ? 1 : 0, queue ? 1 : 0) == 0;
        }

        public bool CanRotate(int def) => OkEngine.okx_def_can_turn(def) != 0;

        // ── The sidebar's buttons ──────────────────────────────────────
        // The engine lists them from the classic HUD's own table, and a
        // press goes through the same orders the sidebar sends.
        readonly OkxHudCommand[] hudBuf = new OkxHudCommand[32];
        readonly Dictionary<string, RgbaImage> actionArt = new Dictionary<string, RgbaImage>();
        readonly Dictionary<int, string> artKey = new Dictionary<int, string>();

        static ActionKind KindOf(in OkxHudCommand c)
        {
            if (c.weaponSlot >= 0) return ActionKind.Spell;
            if (c.group == 1) return ActionKind.Stance;
            switch (c.id)
            {
                case 5: case 6: case 7: case 8: case 120: case 121: case 122: case 123: return ActionKind.Ability;
                default: return ActionKind.Order;
            }
        }

        static ActionTarget TargetOf(in OkxHudCommand c)
        {
            if (c.weaponSlot >= 0) return ActionTarget.PointOrUnit;
            switch (c.id)
            {
                case 1: case 4: case 6: return ActionTarget.Point;          // move, patrol, unload
                case 2: case 8: return ActionTarget.PointOrUnit;            // attack, clear
                case 3: case 7: return ActionTarget.Unit;                   // guard, heal
                case 5: return ActionTarget.Area;                           // load: a rider or a box
                default: return ActionTarget.None;
            }
        }

        static void CommandOf(in OkxHudCommand c, out CommandKind kind, out int arg)
        {
            arg = 0;
            switch (c.id)
            {
                case 1: kind = CommandKind.Move; break;
                case 2: kind = CommandKind.Attack; break;
                case 3: kind = CommandKind.Guard; break;
                case 4: kind = CommandKind.Patrol; break;
                case 5: kind = CommandKind.Load; break;
                case 6: kind = CommandKind.Unload; break;
                case 7: kind = CommandKind.Repair; break;
                case 8: kind = CommandKind.Reclaim; break;
                case 100: kind = CommandKind.Stop; break;
                case 101: kind = CommandKind.SetAggro; arg = 2; break;
                case 102: kind = CommandKind.SetAggro; arg = 1; break;
                case 103: kind = CommandKind.SetAggro; arg = 0; break;
                case 122: kind = CommandKind.Gate; arg = 1; break;
                case 123: kind = CommandKind.Gate; arg = 0; break;
                default:
                    kind = c.weaponSlot >= 0 ? CommandKind.SetWeapon : CommandKind.Stop;
                    arg = Math.Max(0, c.weaponSlot);
                    break;
            }
        }

        public UnitAction[] SelectionActions()
        {
            int n = Math.Min(OkEngine.okx_hud_commands(hudBuf, hudBuf.Length), hudBuf.Length);
            var list = new List<UnitAction>(Math.Max(0, n));
            for (int i = 0; i < n; i++)
            {
                var c = hudBuf[i];
                // What the engine cannot carry out yet (the cloak pair) is left out.
                if (c.enabled == 0 && c.why == OkEngine.WhyUnsupported) continue;
                CommandOf(c, out var kind, out int arg);
                // Frame 0 of the common buttons is a blank tile, so a disabled
                // order shows its rest picture. A spell has its own disabled one.
                int state = c.enabled == 0 && c.weaponSlot >= 0 ? 0 : c.active != 0 ? 1 : 2;
                int picture = c.id * 4 + state;
                artKey[picture] = picture + ":" + (c.weapon ?? "");
                list.Add(new UnitAction
                {
                    Id = c.name,
                    Label = c.weaponSlot >= 0 && !string.IsNullOrEmpty(c.weapon) ? c.weapon : c.label,
                    Kind = KindOf(c),
                    Command = kind,
                    Arg = arg,
                    Target = TargetOf(c),
                    ManaCost = c.manaCost,
                    Enabled = c.enabled != 0,
                    Why = c.why == OkEngine.WhyMana ? "Not enough mana"
                        : c.why == OkEngine.WhyUnsupported ? "Not in the engine yet" : "",
                    Toggled = c.active != 0,
                    StanceGroup = c.group > 0 ? c.group : -1,
                    Hotkey = c.hotkey > 0 ? ((char)c.hotkey).ToString() : "",
                    Picture = picture,
                });
            }
            return list.ToArray();
        }

        public bool DoAction(string id, Vector3 at, int unit, Rect area, bool queue)
        {
            int n = Math.Min(OkEngine.okx_hud_commands(hudBuf, hudBuf.Length), hudBuf.Length);
            for (int i = 0; i < n; i++)
            {
                var c = hudBuf[i];
                if (c.name != id) continue;
                if (c.enabled == 0) return false;
                bool aimed = unit >= 0 || at != Vector3.zero;
                if (c.weaponSlot >= 0)
                {
                    // Choose the spell, then cast it as an attack where aimed.
                    if (c.active == 0 && OkEngine.okx_hud_do(c.id) == 0) return false;
                    if (!aimed) return true;
                    OkEngine.okx_arm(OkEngine.ArmAttack, -1);
                    OkEngine.okx_click(at.x / S, -at.z / S, unit, queue ? 1 : 0);
                    return true;
                }
                if (c.kind != OkEngine.CmdTarget) return OkEngine.okx_hud_do(c.id) != 0;
                // A targeted order: armed, then carried out by the click or
                // the drag the classic view would send. The engine's drag
                // carries out only a load, so an attack over a box goes for
                // the nearest enemy in it by a click, and stays armed for
                // nothing after.
                bool boxed = area.width > 0f && area.height > 0f;
                if (boxed && c.id != OkEngine.ArmLoad)
                {
                    unit = NearestEnemyIn(area, out at);
                    if (unit < 0) return false;
                    aimed = true;
                }
                OkEngine.okx_arm(c.id, -1);
                if (boxed && c.id == OkEngine.ArmLoad)
                {
                    OkEngine.okx_drag(area.xMin / S, -area.yMin / S, area.xMax / S, -area.yMax / S, queue ? 1 : 0);
                    return true;
                }
                if (aimed) OkEngine.okx_click(at.x / S, -at.z / S, unit, queue ? 1 : 0);
                return true;
            }
            return false;
        }

        public RgbaImage ActionPicture(int picture)
        {
            if (picture < 0) return null;
            string key = artKey.TryGetValue(picture, out var k) ? k : picture.ToString();
            if (actionArt.TryGetValue(key, out var img)) return img;
            int need = OkEngine.okx_hud_command_art(picture / 4, picture % 4, null, 0, out int w, out int h);
            if (need <= 0 || w <= 0 || h <= 0) { actionArt[key] = null; return null; }
            img = new RgbaImage(w, h);
            OkEngine.okx_hud_command_art(picture / 4, picture % 4, img.Pixels, img.Pixels.Length, out w, out h);
            actionArt[key] = img;
            return img;
        }

        public bool CanBuildAt(int def, Vector3 at, int facing, out Vector3 snapped)
        {
            int ok = OkEngine.okx_build_site_facing(def, facing & 3, (int)(at.x / S), (int)(-at.z / S), out int sx, out int sy);
            snapped = new Vector3(sx * S, 0f, -sy * S);
            snapped.y = GroundHeight(snapped.x, snapped.z);
            return ok != 0;
        }

        public int QueuedCount(int factory, int def) => OkEngine.okx_factory_queue(factory, def);

        public bool AddToQueue(int factory, int def, int count) =>
            count != 0 && OkEngine.okx_factory_add(factory, def, count) == 0;

        public bool SetRepeat(int factory, int def, bool on) =>
            OkEngine.okx_factory_set_repeat(factory, def, on ? 1 : 0) == 0;

        public int RepeatOf(int factory) => OkEngine.okx_factory_repeat_of(factory);

        readonly OkxOrderLeg[] legBuf = new OkxOrderLeg[64];

        // A factory's rally reads as a move, the sweep and the raise as the
        // reclaim and resurrect they are.
        static OrderKind LegKind(in OkxOrderLeg l)
        {
            switch ((OkxCmd)l.kind)
            {
                case OkxCmd.Move: case OkxCmd.Rally: return OrderKind.Move;
                case OkxCmd.Attack: case OkxCmd.Capture: return OrderKind.Attack;
                case OkxCmd.SpecialWeapon: return l.target >= 0 ? OrderKind.Attack : OrderKind.Move;
                case OkxCmd.Build: return OrderKind.Build;
                case OkxCmd.Patrol: return OrderKind.Patrol;
                case OkxCmd.Guard: return OrderKind.Guard;
                case OkxCmd.Repair: return OrderKind.Repair;
                case OkxCmd.Reclaim: case OkxCmd.ReclaimFeature: return OrderKind.Reclaim;
                case OkxCmd.ResurrectFeature: return OrderKind.Resurrect;
                case OkxCmd.Load: return OrderKind.Load;
                case OkxCmd.Unload: return OrderKind.Unload;
                case OkxCmd.AttackGround: return OrderKind.AttackGround;
                default: return OrderKind.None;
            }
        }

        public int ReadOrderQueue(int handle, OrderLeg[] into)
        {
            int n = OkEngine.okx_unit_orders(handle, legBuf, legBuf.Length);
            if (n <= 0) return 0;
            int fill = Math.Min(Math.Min(n, legBuf.Length), into?.Length ?? 0);
            for (int i = 0; i < fill; i++)
            {
                var l = legBuf[i];
                var at = new Vector3(l.x * S, 0f, -l.y * S);
                at.y = GroundHeight(at.x, at.z);
                into[i] = new OrderLeg { Kind = LegKind(l), Target = at, TargetUnit = l.target, BuildDef = l.def, Facing = l.facing };
            }
            return n;
        }

        public bool PlaySound(string wav, float volume) =>
            !string.IsNullOrEmpty(wav) && OkEngine.okx_play_ui_sound(wav, Mathf.RoundToInt(Mathf.Clamp01(volume) * 127f)) == 0;

        // The engine keeps kills and experience but does not hand them out yet.
        public bool UnitRecord(int handle, out int kills, out int rank)
        {
            kills = 0;
            rank = 0;
            return false;
        }

        // The game's own controls. The engine keeps the selection, and Click
        // is the original's left click, so the game decides what it means
        // and the units answer in their voices.
        readonly int[] selectionBuf = new int[128];

        public void Select(int[] handles, bool add) => OkEngine.okx_select(handles, handles?.Length ?? 0, add ? 1 : 0);

        public int ReadSelection(int[] into)
        {
            int n = OkEngine.okx_selection(selectionBuf, selectionBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            if (count > 0) Array.Copy(selectionBuf, into, count);
            return n;
        }

        public void Click(Vector3 at, int unit, bool shift, bool keep = false) => OkEngine.okx_click(at.x / S, -at.z / S, unit, ClickFlags(shift, keep));

        public void Cancel() => OkEngine.okx_cancel();

        public GameCursor CursorAt(Vector3 at, int unit, out bool siteClear)
        {
            int c = OkEngine.okx_cursor_at(at.x / S, -at.z / S, unit, out int clear);
            siteClear = clear != 0;
            return (GameCursor)c;
        }

        // The engine's cursor numbers are GameCursor's.
        public CursorFrame[] CursorArt(GameCursor cursor)
        {
            int frames = OkEngine.okx_cursor_frame((int)cursor, 0, null, 0, out _, out _, out _, out _, out _);
            if (frames <= 0) return null;
            var art = new CursorFrame[frames];
            for (int f = 0; f < frames; f++)
            {
                OkEngine.okx_cursor_frame((int)cursor, f, null, 0, out int w, out int h, out _, out _, out _);
                if (w <= 0 || h <= 0) return null;
                var img = new RgbaImage(w, h);
                OkEngine.okx_cursor_frame((int)cursor, f, img.Pixels, img.Pixels.Length, out w, out h, out int hx, out int hy, out int ms);
                art[f] = new CursorFrame { Image = img, Hotspot = new Vector2Int(hx, hy), Millis = ms };
            }
            return art;
        }

        // The enemy standing nearest a box's middle, inside it, or -1.
        int NearestEnemyIn(Rect area, out Vector3 at)
        {
            int n = OkEngine.okx_units(null, 0);
            if (unitBuf.Length < n) unitBuf = new OkxUnit[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_units(unitBuf, unitBuf.Length), unitBuf.Length);
            int best = -1;
            float bestD = float.MaxValue;
            at = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var u = unitBuf[i];
                if (u.state != OkEngine.UnitActive || this.Allied(u.player, LocalPlayer)) continue;
                var w = EngineSettings.ToUnity(u.x, u.y, u.z);
                var p = new Vector2(w.x, w.z);
                if (!area.Contains(p)) continue;
                float d = (p - area.center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u.handle; at = w; }
            }
            return best;
        }

        public void Arm(CommandKind kind, int buildDef = -1, int facing = 0)
        {
            int mode;
            switch (kind)
            {
                case CommandKind.Move: mode = OkEngine.ArmMove; break;
                case CommandKind.Attack: mode = OkEngine.ArmAttack; break;
                case CommandKind.Guard: mode = OkEngine.ArmGuard; break;
                case CommandKind.Patrol: mode = OkEngine.ArmPatrol; break;
                case CommandKind.Load: mode = OkEngine.ArmLoad; break;
                case CommandKind.Unload: mode = OkEngine.ArmUnload; break;
                case CommandKind.Repair: mode = OkEngine.ArmHeal; break;
                case CommandKind.Reclaim: mode = OkEngine.ArmClear; break;
                case CommandKind.Build: mode = OkEngine.ArmBuild; break;
                default: mode = OkEngine.ArmNone; break;
            }
            OkEngine.okx_arm(mode, buildDef);
            if (kind == CommandKind.Build) OkEngine.okx_set_build_facing(facing & 3);
        }

        public bool OrderSelection(CommandKind kind, int arg = 0) => OkEngine.okx_order_selection((int)kind, arg) == 0;

        public void AssignGroup(int group) => OkEngine.okx_group_assign(group);
        public int RecallGroup(int group) => OkEngine.okx_group_recall(group);

        public UnitOrder ReadOrder(int handle)
        {
            if (OkEngine.okx_unit_order(handle, out var o) != 0)
                return new UnitOrder { Kind = OrderKind.None, TargetUnit = -1, Building = -1 };
            var at = new Vector3(o.x * S, 0f, -o.y * S);
            at.y = GroundHeight(at.x, at.z);
            return new UnitOrder { Kind = (OrderKind)o.kind, TargetUnit = o.target, Target = at, Building = o.building };
        }

        byte[] fogCells = Array.Empty<byte>();

        // The engine keeps one byte a cell. A height grid has one more
        // sample each way, so the last row and column repeat.
        public int ReadFog(byte[] into, out int width, out int height)
        {
            width = terrain != null ? terrain.HeightsW : 0;
            height = terrain != null ? terrain.HeightsH : 0;
            int need = width * height;
            if (into == null || into.Length < need || need == 0) return need;
            int cells = OkEngine.okx_fog(null, 0, out int cw, out int ch);
            if (cells <= 0) return need;
            if (fogCells.Length < cells) fogCells = new byte[cells];
            OkEngine.okx_fog(fogCells, cells, out cw, out ch);
            for (int y = 0; y < height; y++)
            {
                int sy = Mathf.Min(y, ch - 1);
                for (int x = 0; x < width; x++)
                    into[y * width + x] = fogCells[sy * cw + Mathf.Min(x, cw - 1)];
            }
            return need;
        }

        // Not in IGameBackend yet: the engine's own sound and music, and the
        // view it places sounds by. Volume 0 to 1.
        // Outside a battle okx_tick runs no ticks and moves the music on.
        public void PumpAudio() => OkEngine.okx_tick(0);

        public bool SetAudio(float volume, bool music) =>
            OkEngine.okx_audio(volume > 0f ? 1 : 0, Mathf.RoundToInt(Mathf.Clamp01(volume) * 127f), music ? 1 : 0) == 0;

        public void SetView(Vector3 centre, float width, float depth) =>
            OkEngine.okx_set_view((int)(centre.x / S), (int)(-centre.z / S), (int)(width / S), (int)(depth / S));

        public int ReadEffects(EffectState[] into)
        {
            RefreshFx();
            return fx.Effects(into);
        }

        public EffectFrame[] EffectFrames(int strip) => fx.Frames(strip);

        // What the units on the map and all they can build can show, not
        // every strip the engine holds. okx_units lists only what the player
        // sees, so every handle is asked.
        public IReadOnlyList<int> WarmEffectStrips()
        {
            var onMap = new List<int>();
            for (int h = 0, misses = 0; h < 8192 && misses < 64; h++)
            {
                if (OkEngine.okx_unit(h, out var u) == 0) { onMap.Add(u.def); misses = 0; }
                else misses++;
            }
            return EngineFx.StripsToWarm(onMap, d => d >= 0 && d < unitDefs.Count ? unitDefs[d].BuildOptions : null,
                OkEngine.okx_def_effect_strips);
        }

        public RgbaImage EffectStrip(int strip)
        {
            int need = OkEngine.okx_effect_strip(strip, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_effect_strip(strip, img.Pixels, need, out w, out h);
            return img;
        }

        // Not in IGameBackend yet: the map editor. Heights are the map's own
        // bytes, one a cell. Blocks take a chunk id from the library and a
        // sub square. A map saves under a new name in the player's folder.
        public int ReadCells(byte[] into, out int width, out int height) => OkEngine.okx_map_cells(into, into?.Length ?? 0, out width, out height);

        public bool EditCells(int x0, int z0, int w, int h, byte[] values)
        {
            if (OkEngine.okx_edit_cells(x0, z0, w, h, values) != 0) return false;
            // Terrain follows in place, so the editor rebuilds only the
            // regions it touched. The engine's corner grid repeats the last
            // row and column, so the whole grid is read back, which is cheap.
            if (terrain != null)
            {
                OkEngine.okx_terrain_heights(terrain.Heights, terrain.Heights.Length);
                for (int i = 0; i < terrain.Heights.Length; i++) terrain.Heights[i] *= S;
            }
            return true;
        }

        public uint[] ChunkLibrary()
        {
            int n = OkEngine.okx_chunk_library(null, 0);
            var ids = new uint[Math.Max(0, n)];
            if (n > 0) OkEngine.okx_chunk_library(ids, n);
            return ids;
        }

        public RgbaImage ChunkPicture(uint id)
        {
            int need = OkEngine.okx_chunk_picture(id, null, 0, out int w, out int h);
            if (need <= 0) return null;
            var img = new RgbaImage(w, h);
            OkEngine.okx_chunk_picture(id, img.Pixels, need, out w, out h);
            return img;
        }

        public bool PaintBlocks(int bx, int by, int w, int h, uint[] chunkIds, byte[] texX, byte[] texY)
        {
            if (OkEngine.okx_edit_blocks(bx, by, w, h, chunkIds, texX, texY) != 0) return false;
            ReadTerrain();
            return true;
        }

        public int PlaceFeature(int def, int cx, int cz) => OkEngine.okx_feature_place(def, cx, cz);
        public bool RemoveFeature(int index) => OkEngine.okx_feature_remove(index) == 0;

        public bool SaveMap(string name)
        {
            if (OkEngine.okx_map_save(name) != 0) return false;
            maps.Clear();
            mapIndex.Clear();
            ReadMaps();
            return true;
        }

        public Economy ReadEconomy(int player)
        {
            if (OkEngine.okx_economy(player, out var e) != 0) return default;
            return new Economy { Mana = e.mana, Storage = e.maxMana, Income = e.income, Expense = e.spentLastSec };
        }

        public void Dispose()
        {
            EndGame();
            OkEngine.okx_audio(0, 0, 0);
            // The engine stays up for the next backend in this process: SDL
            // and the file system are cheap to keep and slow to restart.
        }
    }
}
