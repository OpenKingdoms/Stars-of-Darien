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
        readonly int[] buildBuf = new int[256];
        readonly Dictionary<int, ModelData> modelCache = new Dictionary<int, ModelData>();
        OkxUnit[] unitBuf = new OkxUnit[1024];
        OkxFeature[] featureBuf = new OkxFeature[1024];
        readonly float[] pose = new float[128 * 12];
        readonly byte[] hidden = new byte[128];
        SkirmishSetup pending;
        GameStatus status = GameStatus.Idle;
        MapTerrain terrain;

        public string Name => "OpenKingdoms";

        public EngineBackend()
        {
            OkEngine.PreloadDependencies(EngineSettings.PluginDir);
            if (OkEngine.okx_api_version() != OkEngine.ApiVersion)
                throw new InvalidOperationException($"okengine API {OkEngine.okx_api_version()}, this binding expects {OkEngine.ApiVersion}");
            if (OkEngine.okx_init(EngineSettings.GameDir, EngineSettings.DataDir) != 0)
                throw new InvalidOperationException("okx_init: " + OkEngine.LastError);
            OkEngine.okx_set_override_dir(EngineSettings.OverrideDir.Replace('\\', '/'));
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
            string[,] kingdoms =
            {
                { "ARAMON", "Aramon" }, { "VERUNA", "Veruna" }, { "TAROS", "Taros" },
                { "ZHON", "Zhon" }, { "CREON", "Creon" }
            };
            for (int i = 0; i < kingdoms.GetLength(0); i++)
                sides.Add(new SideInfo { Id = kingdoms[i, 0], Name = kingdoms[i, 1], Description = "" });
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
            status = GameStatus.Loading;
        }

        public LoadProgress PumpLoading()
        {
            if (status != GameStatus.Loading || pending == null)
                return new LoadProgress { Fraction = 1, Done = status == GameStatus.Running, Failed = status == GameStatus.Failed };
            var setup = pending;
            pending = null;
            int ai = 0;
            string kingdom = "aramon";
            for (int i = 0; i < setup.Seats.Count; i++)
            {
                var seat = setup.Seats[i];
                if (i == 0 && !string.IsNullOrEmpty(seat.Side)) kingdom = seat.Side.ToLowerInvariant();
                if (i > 0 && seat.Kind == SeatKind.Computer) ai++;
            }
            var cfg = new OkxSkirmish
            {
                map = setup.MapId, kingdom = kingdom, aiPlayers = ai,
                lineOfSight = setup.LineOfSight ? 1 : 0, mapRevealed = setup.MapRevealed ? 1 : 0,
                seed = setup.Seed
            };
            if (OkEngine.okx_start_skirmish(ref cfg) != 0)
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

        void ReadCatalogue()
        {
            unitDefs.Clear();
            int n = OkEngine.okx_def_count();
            for (int i = 0; i < n; i++)
            {
                if (OkEngine.okx_def_info(i, out var d) != 0) continue;
                unitDefs.Add(new UnitDef
                {
                    Id = i, Name = d.name, ObjectName = d.obj, Side = d.side, Category = d.category,
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
                    Colour = s.color, Team = s.team, IsLocal = s.index == me, IsComputer = s.kind == 2,
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
                    Model = u.model
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

        public bool Command(in GameCommand c)
        {
            float x = c.Target.x / S, z = -c.Target.z / S;
            return OkEngine.okx_command((int)c.Kind, c.Unit, (int)x, (int)z, c.TargetUnit, c.BuildDef, c.Arg) == 0;
        }

        public Economy ReadEconomy(int player)
        {
            if (OkEngine.okx_economy(player, out var e) != 0) return default;
            return new Economy { Mana = e.mana, Storage = e.maxMana, Income = e.income, Expense = e.spentLastSec };
        }

        public void Dispose()
        {
            EndGame();
            // The engine stays up for the next backend in this process: SDL
            // and the file system are cheap to keep and slow to restart.
        }
    }
}
