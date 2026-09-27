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
        // Saved games. SaveGame writes the running battle. LoadGame is
        // StartSkirmish for a save, and PumpLoading then brings it up. A
        // save that will not read returns false and changes nothing.
        bool SaveGame(string path);
        bool LoadGame(string path);
        bool SaveInfo(string path, out string map, out uint tick, out DateTime savedAt);
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
        // Explosions, sparks, blood and smoke, and shots drawn as pictures,
        // each at its current frame. Returns the full count.
        int ReadEffects(EffectState[] into);
        // Each piece's transform, piece space to world, for a unit (by
        // handle) or a model feature (by index). Returns the piece count.
        int ReadUnitPose(int handle, PiecePose[] into);
        int ReadFeaturePose(int index, PiecePose[] into);
        // The unit script function driving a unit's pose now, one of its
        // UnitDef.Animations ("walk", "attack1"), or "" when idle or unknown.
        string UnitAnimation(int handle);

        // Art. LoadModel returns a model id (or -1) for an object name in a
        // team colour. Model ids in UnitState and FeatureState are the same ids.
        int LoadModel(string objectName, int colour);
        ModelData GetModel(int model);
        RgbaImage Texture(int texture);
        RgbaImage Sprite(int sprite);
        // A unit's build-menu picture from the game, or null.
        RgbaImage UnitPicture(int def);
        // An effect's frames side by side, for EffectState.Strip.
        RgbaImage EffectStrip(int strip);
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

        // The game's own controls. The backend keeps the selection, and Click
        // is the original's left click: with the selection and any armed
        // command it selects, moves, attacks, repairs or carries out the
        // armed command, as the game decides. `unit` is what the pointer is
        // over, or -1 for ground. Cancel is the right click and Escape: it
        // disarms an armed command, or else deselects.
        void Select(int[] handles, bool add);
        int ReadSelection(int[] into);
        void Click(Vector3 at, int unit, bool shift);
        void Cancel();
        // The pointer the original shows at a ground point or over `unit`
        // (-1 for ground), from the selection and any armed command, as the
        // game decides it. For Place, siteClear says whether the armed
        // building can stand there.
        GameCursor CursorAt(Vector3 at, int unit, out bool siteClear);
        // A cursor's frames from the game's own art, or null when the
        // backend has none and the system pointer stays.
        CursorFrame[] CursorArt(GameCursor cursor);
        // Arm a command button so the next Click carries it out: Move,
        // Attack, Guard, Patrol, Load, Unload, Repair, Reclaim, or Build with
        // the building's def.
        void Arm(CommandKind kind, int buildDef = -1, int facing = 0);
        // An order for everything selected that needs no point: Stop,
        // SetAggro and SetWeapon with arg, Gate.
        bool OrderSelection(CommandKind kind, int arg = 0);
        void AssignGroup(int group);
        int RecallGroup(int group);

        // Orders, for one unit at a time. False when the engine refuses it.
        bool Command(in GameCommand command);
        Economy ReadEconomy(int player);

        // For the HUD. Where a building of def would stand for a site at
        // `at`, snapped to the cell grid as the game places it, and whether
        // it can be built there.
        // facing is 0 to 3, a quarter turn clockwise seen from above each,
        // and odd facings swap the footprint.
        bool CanBuildAt(int def, Vector3 at, int facing, out Vector3 snapped);
        // False for a building the engine will not turn, such as a
        // lodestone, whose yard must cover its site. It is placed at 0.
        bool CanRotate(int def);
        // How many of def a factory has queued or in progress, or all of
        // them for def -1.
        int QueuedCount(int factory, int def);
        // What a unit is doing now.
        UnitOrder ReadOrder(int handle);
        // The local player's fog, one byte per height sample (HeightsW by
        // HeightsH, row 0 on the north edge), as the classic overlay draws
        // it: 0 black, 1 dimmed (seen before, only with line of sight on),
        // 2 clear (in sight, or seen before with line of sight off). Returns the byte count, and with into null or
        // too small only reports the size. Read it a few times a second.
        int ReadFog(byte[] into, out int width, out int height);

        // The map editor. Heights are the map's own bytes, one a cell, and
        // the battle ground (Terrain, GroundHeight) follows an edit at once.
        // Returns the byte count, and with into null or too small only
        // reports the size.
        int ReadCells(byte[] into, out int width, out int height);
        bool EditCells(int x0, int z0, int w, int h, byte[] values);
        // Ground pictures from the game's whole library, by id. PaintBlocks
        // gives each block in a rectangle a library chunk and the square in
        // it (in BlockTexels units), and Terrain.Blocks follows.
        uint[] ChunkLibrary();
        RgbaImage ChunkPicture(uint id);
        bool PaintBlocks(int bx, int by, int w, int h, uint[] chunkIds, byte[] texX, byte[] texY);
        // A feature at a cell, returning its index for ReadFeatures, and
        // taking one away by that index.
        int PlaceFeature(int def, int cx, int cz);
        bool RemoveFeature(int index);
        // Saves the map being edited as a new map under that name, which
        // then appears in Maps and plays in a skirmish.
        bool SaveMap(string name);
    }

    public enum GameStatus { Idle, Loading, Running, Victory, Defeat, Failed }

    public enum SeatKind { Closed, Human, Computer }

    public enum AiDifficulty { Easy, Normal, Hard, Brutal }

    // The engine's command numbers, TAK_CMD_* in tak_commands.h.
    // The original's pointers. Place is a building's ghost with the plain
    // pointer, Cannot is the red pointer and Busy the hourglass.
    public enum GameCursor
    {
        Normal, Select, Move, Attack, Guard, Patrol, Load, Unload, Repair, Reclaim,
        Revive, Place, Cannot, Busy
    }

    public static class GameCursors
    {
        // The pointer an armed command shows.
        public static GameCursor For(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.Move: return GameCursor.Move;
                case CommandKind.Attack: case CommandKind.AttackGround: return GameCursor.Attack;
                case CommandKind.Guard: return GameCursor.Guard;
                case CommandKind.Patrol: return GameCursor.Patrol;
                case CommandKind.Load: return GameCursor.Load;
                case CommandKind.Unload: return GameCursor.Unload;
                case CommandKind.Repair: return GameCursor.Repair;
                case CommandKind.Reclaim: case CommandKind.ReclaimFeature: return GameCursor.Reclaim;
                case CommandKind.ResurrectFeature: return GameCursor.Revive;
                case CommandKind.Build: return GameCursor.Place;
                default: return GameCursor.Normal;
            }
        }
    }

    public sealed class CursorFrame
    {
        public RgbaImage Image;
        public Vector2Int Hotspot;  // the pixel the pointer's position falls on, from the top left
        public int Millis;          // how long this frame shows
    }

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
        public string Title = "";   // the name the game's HUD shows, "Knight"
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

    [Serializable]
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
        public int Facing;          // a building's quarter turns, 0 to 3; Heading carries it too
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

    public struct EffectState
    {
        public int Id;              // stable while it lives
        public int Strip;           // for EffectStrip()
        public bool IsProjectile;   // a shot in flight drawn as a picture, else an impact effect
        public Vector3 Position;    // world
        // The current frame as a camera-facing quad, like the sprite
        // features: from Bottom to Top above Position.y, Width wide with its
        // anchor OffsetX from the left, sampling the strip from UvMin.x to
        // UvMax.x across and 0 to UvMax.y down (row 0 at the top).
        public float Top, Bottom, OffsetX, Width;
        public Vector2 UvMin, UvMax;
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
        public int Facing;          // for Build, 0 to 3

        public static GameCommand To(CommandKind kind, int unit, Vector3 at) =>
            new GameCommand { Kind = kind, Unit = unit, Target = at, TargetUnit = -1, BuildDef = -1 };
    }

    public struct Economy
    {
        public float Mana, Storage;
        public float Income, Expense; // per second
    }
}
