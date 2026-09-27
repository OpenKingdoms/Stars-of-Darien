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
                maps.Add(new MapInfo
                {
                    Id = mi.name, Name = mi.name, Description = mi.description ?? "",
                    MaxPlayers = Mathf.Max(2, mi.maxPlayers),
                    Size = new Vector2(mi.sizeX, mi.sizeY), Climate = Climate(mi.kingdom)
                });
                mapIndex[mi.name] = i;
            }
        }

        public IReadOnlyList<MapInfo> Maps => maps;
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
            status = GameStatus.Loading;
        }

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
            var cfg = OkxSkirmish.For(setup.MapId);
            cfg.lineOfSight = setup.LineOfSight ? 1 : 0;
            cfg.mapRevealed = setup.MapRevealed ? 1 : 0;
            cfg.seed = setup.Seed;
            cfg.unitsPerSide = setup.UnitLimit;
            // The lobby's lineup seat by seat. The engine plays seat 0 as
            // the local player.
            cfg.seatCount = Mathf.Min(setup.Seats.Count, cfg.seats.Length);
            for (int i = 0; i < cfg.seatCount; i++)
            {
                var seat = setup.Seats[i];
                cfg.seats[i] = new OkxSeat
                {
                    kind = seat.Kind == SeatKind.Closed ? 0 : seat.Kind == SeatKind.Human ? 1 : 2,
                    // Teams count from 0 here and from 1 in the engine,
                    // where 0 means a side alone.
                    side = SideIndex(seat.Side), team = Mathf.Max(0, seat.Team) + 1, color = seat.Colour,
                    difficulty = (int)seat.Difficulty
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
                unitDefs.Add(new UnitDef
                {
                    Id = i, Name = d.name, Title = d.displayName ?? "", ObjectName = d.obj, Side = d.side, Category = d.category,
                    Description = d.description, MaxHealth = d.maxHealth, IsBuilding = d.isBuilding != 0,
                    Footprint = new Vector2Int(d.footprintX, d.footprintZ), ManaCost = d.buildCost,
                    BuildOptions = Buildables(i), Animations = OkEngine.Scripts(i)
                });
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

        static readonly string[] SideIds = { "ARAMON", "TAROS", "VERUNA", "ZHON", "", "", "", "CREON" };

        // A side's engine number, -1 for random.
        static int SideIndex(string side)
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
                    Colour = s.color, Team = Mathf.Max(0, s.team - 1), IsLocal = s.index == me, IsComputer = s.kind == 2,
                    Alive = s.alive != 0, Tint = TeamTint(s.color)
                });
            }
        }

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

        // Engine heading: radians, 0 facing south, turning clockwise seen
        // from above. The contract's: degrees, 0 facing north, clockwise.
        static float Heading(float radians)
        {
            float d = radians * Mathf.Rad2Deg + 180f;
            return Mathf.Repeat(d, 360f);
        }

        public int ReadUnits(UnitState[] into)
        {
            int n = OkEngine.okx_units(null, 0);
            if (unitBuf.Length < n) unitBuf = new OkxUnit[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_units(unitBuf, unitBuf.Length), unitBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var u = unitBuf[i];
                var flags = u.state == OkEngine.UnitActive ? UnitFlags.Active : UnitFlags.Dying;
                if (u.building != 0) flags |= UnitFlags.Building;
                into[i] = new UnitState
                {
                    Handle = u.handle, StableId = u.stableId, Def = u.def, Player = u.player, Flags = flags,
                    Position = EngineSettings.ToUnity(u.x, u.y, u.z),
                    Heading = Heading(u.heading), Pitch = u.pitch * Mathf.Rad2Deg, Roll = u.roll * Mathf.Rad2Deg,
                    Health = u.health, MaxHealth = u.maxHealth,
                    BuildProgress = u.building != 0 && u.maxHealth > 0 ? Mathf.Clamp01(u.health / (float)u.maxHealth) : 1f,
                    Model = u.model,
                    Facing = u.facing
                };
                if (u.model >= 0 && !modelSource.ContainsKey(u.model)) modelSource[u.model] = (u.def, u.color);
            }
            return n;
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
                    Heading = Heading(f.heading), Pitch = f.pitch * Mathf.Rad2Deg, Roll = f.roll * Mathf.Rad2Deg,
                    Model = f.model, Sprite = f.sprite,
                    SpriteTop = (f.top - f.y) * S, SpriteBottom = (f.bottom - f.y) * S,
                    SpriteOffsetX = f.offX * S, SpriteWidth = f.w * S, Flat = f.flat != 0
                };
            }
            return n;
        }

        public int ReadProjectiles(ProjectileState[] into)
        {
            int n = OkEngine.okx_projectiles(null, 0);
            if (projBuf.Length < n) projBuf = new OkxProjectile[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_projectiles(projBuf, projBuf.Length), projBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var p = projBuf[i];
                into[i] = new ProjectileState
                {
                    Id = p.id, Player = p.player, Kind = p.kind, Model = p.model,
                    Position = EngineSettings.ToUnity(p.x, p.y, p.z),
                    // Pixels a tick to units a second.
                    Velocity = new Vector3(p.vx, p.vy, -p.vz) * (S * OkEngine.okx_tick_rate())
                };
            }
            return n;
        }

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
            return nodes > 0 ? WritePose(nodes, into, true) : 0;
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

        // A build order's arg is its facing.
        public bool Command(in GameCommand c)
        {
            float x = c.Target.x / S, z = -c.Target.z / S;
            int arg = c.Kind == CommandKind.Build ? (c.Facing & 3) : c.Arg;
            return OkEngine.okx_command((int)c.Kind, c.Unit, (int)x, (int)z, c.TargetUnit, c.BuildDef, arg) == 0;
        }

        public bool CanRotate(int def) => OkEngine.okx_def_can_turn(def) != 0;

        public bool CanBuildAt(int def, Vector3 at, int facing, out Vector3 snapped)
        {
            int ok = OkEngine.okx_build_site_facing(def, facing & 3, (int)(at.x / S), (int)(-at.z / S), out int sx, out int sy);
            snapped = new Vector3(sx * S, 0f, -sy * S);
            snapped.y = GroundHeight(snapped.x, snapped.z);
            return ok != 0;
        }

        public int QueuedCount(int factory, int def) => OkEngine.okx_factory_queue(factory, def);

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

        public void Click(Vector3 at, int unit, bool shift) => OkEngine.okx_click(at.x / S, -at.z / S, unit, shift ? 1 : 0);

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
        public bool SetAudio(float volume, bool music) =>
            OkEngine.okx_audio(volume > 0f ? 1 : 0, Mathf.RoundToInt(Mathf.Clamp01(volume) * 127f), music ? 1 : 0) == 0;

        public void SetView(Vector3 centre, float width, float depth) =>
            OkEngine.okx_set_view((int)(centre.x / S), (int)(-centre.z / S), (int)(width / S), (int)(depth / S));

        public int ReadEffects(EffectState[] into)
        {
            int n = OkEngine.okx_effects(null, 0);
            if (effectBuf.Length < n) effectBuf = new OkxEffect[Mathf.NextPowerOfTwo(n)];
            n = Mathf.Min(OkEngine.okx_effects(effectBuf, effectBuf.Length), effectBuf.Length);
            int count = Mathf.Min(n, into?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var e = effectBuf[i];
                into[i] = new EffectState
                {
                    Id = e.kind == OkEngine.EffectProjectile ? -1 - e.id : e.id,
                    Strip = e.sprite, IsProjectile = e.kind == OkEngine.EffectProjectile,
                    Position = EngineSettings.ToUnity(e.x, e.y, e.z),
                    Top = (e.top - e.y) * S, Bottom = (e.bottom - e.y) * S,
                    OffsetX = e.offX * S, Width = e.w * S,
                    UvMin = new Vector2(e.u0, 0f), UvMax = new Vector2(e.u1, e.v1)
                };
            }
            return n;
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
