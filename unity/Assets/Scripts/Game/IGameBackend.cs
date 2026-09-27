// IGameBackend.cs - everything the presentation needs from a game engine.
// The embedded OpenKingdoms engine implements it, and MockBackend fakes it
// so the menus, terrain, units and studio run without the engine or the
// game files. Change it only in agreement with both sides.
//
// Space: Unity world units, one unit per map cell (16 engine pixels), y up.
// The map's north-west corner is the origin, x runs east and z runs north,
// so the whole map lies at z <= 0. Headings are degrees about +y, 0 facing
// north (+z), clockwise seen from above. Pictures are RGBA with row 0 at
// the top. Model UVs address a texture uploaded as is, v = row / height.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public interface IGameBackend : IDisposable
    {
        string Name { get; }

        // The catalogue, readable before any game starts.
        IReadOnlyList<MapInfo> Maps { get; }
        IReadOnlyList<SideInfo> Sides { get; }
        IReadOnlyList<UnitDef> UnitDefs { get; }
        IReadOnlyList<FeatureDef> FeatureDefs { get; }
        // A picture of the whole map at most maxSize on its longer side, or null.
        RgbaImage MapPreview(string mapId, int maxSize);

        // A game. StartSkirmish only records the setup. The presentation then
        // calls PumpLoading once a frame, and each call does a slice of the
        // work, until it reports Done (Status is Running) or Failed.
        void StartSkirmish(SkirmishSetup setup);
        LoadProgress PumpLoading();
        GameStatus Status { get; }
        void EndGame();

        int LocalPlayer { get; }
        IReadOnlyList<PlayerInfo> Players { get; }
        int TicksPerSecond { get; }
        uint Tick { get; }
        // Runs up to n simulation ticks and returns how many ran. The
        // presentation owns the clock, so pausing is simply not calling it.
        int Advance(int n);

        // The ground, valid once loading is done.
        MapTerrain Terrain { get; }
        float GroundHeight(float x, float z);
        // A ground picture that MapTerrain.Blocks points into.
        RgbaImage TerrainChunk(int chunk);

        // Snapshots, each filling a caller's buffer and returning the count.
        int ReadUnits(UnitState[] into);
        int ReadFeatures(FeatureState[] into);
        int ReadProjectiles(ProjectileState[] into);
        // Each piece's transform, piece space to world, for a unit (by
        // handle) or a model feature (by index). Returns the piece count.
        int ReadUnitPose(int handle, PiecePose[] into);
        int ReadFeaturePose(int index, PiecePose[] into);

        // Art. LoadModel returns a model id (or -1) for an object name in a
        // team colour. Model ids in UnitState and FeatureState are the same ids.
        int LoadModel(string objectName, int colour);
        ModelData GetModel(int model);
        RgbaImage Texture(int texture);
        RgbaImage Sprite(int sprite);
        // A pose of a model outside any game, in model space, for the
        // studio. The animation is one of UnitDef.Animations. Returns the
        // piece count, or 0 when the backend cannot pose it.
        int PoseModel(int model, string animation, float seconds, PiecePose[] into);

        // Sound, which the engine plays straight to the audio device.
        // Volume 0 to 1, 0 stops it all. Call before StartSkirmish so the
        // music follows the local kingdom, and whenever the options change.
        // False when there is no audio device, and the game plays on.
        bool SetAudio(float volume, bool music);
        // The ground the camera looks at, its centre and the width and depth
        // in view in world units, once a frame, so sounds pan and fade.
        void SetView(Vector3 centre, float width, float depth);

        // Orders, for one unit at a time. False when the engine refuses it.
        bool Command(in GameCommand command);
        Economy ReadEconomy(int player);

        // For the HUD. Where a building of def would stand for a site at
        // `at`, snapped to the cell grid as the game places it, and whether
        // it can be built there.
        bool CanBuildAt(int def, Vector3 at, out Vector3 snapped);
        // How many of def a factory has queued or in progress, or all of
        // them for def -1.
        int QueuedCount(int factory, int def);
        // What a unit is doing now.
        UnitOrder ReadOrder(int handle);
        // The local player's fog, one byte per height sample (HeightsW by
        // HeightsH, row 0 on the north edge): 0 never seen, 1 seen before,
        // 2 in sight now. Returns the byte count, and with into null or
        // too small only reports the size. Read it a few times a second.
        int ReadFog(byte[] into, out int width, out int height);
    }

    public enum GameStatus { Idle, Loading, Running, Victory, Defeat, Failed }

    public enum SeatKind { Closed, Human, Computer }

    public enum AiDifficulty { Easy, Normal, Hard }

    // The engine's command numbers, TAK_CMD_* in tak_commands.h.
    public enum CommandKind
    {
        Move = 1, Attack, Build, Stop, Patrol, Guard, Repair, Reclaim, Capture,
        Load, Unload, Wait, SetAggro, SetWeapon,
        FactoryEnqueue, FactoryDequeue, FactoryCancel, Rally, Gate, AttackGround,
        SpecialWeapon, ReclaimFeature, ResurrectFeature
    }

    public enum OrderKind
    {
        None, Move, Attack, Build, Patrol, Guard, Repair, Reclaim, Load, Unload,
        AttackGround, Resurrect, Board
    }

    public struct UnitOrder
    {
        public OrderKind Kind;
        public int TargetUnit;      // handle, or -1
        public Vector3 Target;      // world point
        public int Building;        // the building a builder works on, or -1
    }

    [Flags]
    public enum UnitFlags { None = 0, Active = 1, Dying = 2, Building = 4, Moving = 8, Attacking = 16 }

    public sealed class RgbaImage
    {
        public int Width, Height;
        public byte[] Pixels;   // Width * Height * 4, row 0 at the top

        public RgbaImage(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }
    }

    public sealed class MapInfo
    {
        public string Id;           // what StartSkirmish and MapPreview take
        public string Name;
        public string Description;
        public int MaxPlayers;
        public Vector2 Size;        // world units, east by south
        // The land's look, for sky, weather and water defaults: "grass",
        // "snow", "desert", "swamp", "volcanic" or "".
        public string Climate = "";
    }

    public sealed class SideInfo
    {
        public string Id;           // "ARAMON", as the engine names it
        public string Name;         // "Aramon"
        public string Description;
    }

    public sealed class UnitDef
    {
        public int Id;
        public string Name;         // the internal unit name
        public string ObjectName;   // the model name, for LoadModel
        public string Side;
        public string Category;
        public string Description;
        public int MaxHealth;
        public int ManaCost;
        public bool IsBuilding;
        public Vector2Int Footprint;
        public string[] Animations = Array.Empty<string>();
        public int[] BuildOptions = Array.Empty<int>();
    }

    public sealed class FeatureDef
    {
        public int Id;
        public string Name;
        public string ObjectName;   // "" for a sprite-only feature
        public string SequenceName; // the sprite name, "" for a model feature
        public string Category;
        public Vector2Int Footprint;
        public float Height;
    }

    public sealed class SeatSetup
    {
        public SeatKind Kind;
        public string Side;         // SideInfo.Id, or "" for random
        public int Colour;
        public int Team;            // allies share a team
        public AiDifficulty Difficulty = AiDifficulty.Normal;
    }

    public sealed class SkirmishSetup
    {
        public string MapId;
        public uint Seed;
        // Seat 0 is the local player. Closed seats are skipped.
        public List<SeatSetup> Seats = new List<SeatSetup>();
        public bool LineOfSight = true;
        public bool MapRevealed;
        public int StartMana = 1000;
        public int UnitLimit = 250;
    }

    public struct LoadProgress
    {
        public float Fraction;      // 0 to 1
        public string Stage;        // shown under the bar
        public bool Done;
        public bool Failed;
        public string Error;
    }

    public sealed class PlayerInfo
    {
        public int Index;
        public string Name;
        public string Side;
        public int Colour;
        public Color32 Tint;
        public int Team;
        public bool IsLocal, IsComputer, Alive;
    }

    public sealed class MapTerrain
    {
        public int HeightsW, HeightsH;
        public float CellSize;      // world units between height samples
        public float[] Heights;     // world units, row 0 on the north edge
        public float SeaLevel;      // world y of the water, below 0 for none
        // The ground picture in square blocks. Block (bx, by) covers x from
        // bx * BlockSize and z from -by * BlockSize, and draws the square of
        // BlockTexels at (Blocks[3b + 1], Blocks[3b + 2]) in chunk
        // Blocks[3b], with b = by * BlocksW + bx.
        public int BlocksW, BlocksH;
        public float BlockSize;
        public int BlockTexels;
        public int ChunkCount;
        public int[] Blocks;

        public Vector2 Size => new Vector2((HeightsW - 1) * CellSize, (HeightsH - 1) * CellSize);

        public float HeightAt(int x, int z)
        {
            x = Mathf.Clamp(x, 0, HeightsW - 1);
            z = Mathf.Clamp(z, 0, HeightsH - 1);
            return Heights[z * HeightsW + x];
        }

        // Bilinear height at a world point.
        public float Sample(float worldX, float worldZ)
        {
            float fx = worldX / CellSize, fz = -worldZ / CellSize;
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(HeightAt(x0, z0), HeightAt(x0 + 1, z0), tx);
            float b = Mathf.Lerp(HeightAt(x0, z0 + 1), HeightAt(x0 + 1, z0 + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }
    }

    public struct UnitState
    {
        public int Handle;          // valid while the unit lives
        public uint StableId;       // never reused in a game
        public int Def;
        public int Player;
        public UnitFlags Flags;
        public Vector3 Position;
        public float Heading, Pitch, Roll;
        public int Health, MaxHealth;
        public float BuildProgress; // 1 when finished
        public int Model;
    }

    public struct FeatureState
    {
        public int Index;
        public int Def;
        public Vector3 Position;
        public float Heading, Pitch, Roll;
        public int Model;           // -1 for a sprite feature
        public int Sprite;          // -1 for a model feature
        // A sprite quad in world units, from Bottom to Top above the
        // ground, OffsetX left of centre, Width wide. Flat lies on the ground.
        public float SpriteTop, SpriteBottom, SpriteOffsetX, SpriteWidth;
        public bool Flat;
    }

    public struct ProjectileState
    {
        public int Id;
        public int Player;
        public int Kind;            // weapon id, for the look
        public Vector3 Position, Velocity;
        public int Model;           // -1 to draw a streak
    }

    public struct PiecePose
    {
        public Matrix4x4 Matrix;    // piece space to world (or model space)
        public bool Hidden;
    }

    public sealed class PieceInfo
    {
        public string Name;
        public int Parent;          // -1 for the root
        public Vector3 Offset;      // from the parent, model units
    }

    public struct Batch
    {
        public int FirstIndex, IndexCount;
        public int Texture;         // for Texture(), -1 for vertex colour
    }

    // A model as one vertex pool. Every vertex belongs to one piece
    // (VertexPiece) and sits in that piece's space, and the triangles come
    // in batches by texture.
    public sealed class ModelData
    {
        public string Name;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public Color32[] Colors;
        public int[] VertexPiece;
        public int[] Indices;
        public PieceInfo[] Pieces;
        public Batch[] Batches;
        public Bounds Bounds;       // at rest, model units
        public float Scale = 1f;    // model units to world units
        public bool FromOverride;
    }

    public struct GameCommand
    {
        public CommandKind Kind;
        public int Unit;            // handle
        public Vector3 Target;      // world point, when TargetUnit is -1
        public int TargetUnit;
        public int BuildDef;        // for Build, else -1
        public int Arg;
        public bool Queue;          // add after current orders

        public static GameCommand To(CommandKind kind, int unit, Vector3 at) =>
            new GameCommand { Kind = kind, Unit = unit, Target = at, TargetUnit = -1, BuildDef = -1 };
    }

    public struct Economy
    {
        public float Mana, Storage;
        public float Income, Expense; // per second
    }
}
