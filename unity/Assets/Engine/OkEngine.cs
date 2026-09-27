// OkEngine.cs - the C# side of OpenKingdoms' include/ok_embed.h, the
// whole engine as a native library (okengine). Keep the two in step: a
// change there bumps OKX_API_VERSION and ApiVersion below.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenKingdomsUnity.Engine
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxDefInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string obj;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string side;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string category;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string displayName;
        public int maxHealth, isBuilding, footprintX, footprintZ, buildCost;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxMapInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 96)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string kingdom;
        public int sizeX, sizeY, maxPlayers;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] playerCounts;
        public int playerCountN;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxPlayer
    {
        public int index, kind, side, team, color, alive;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxEconomy
    {
        public float mana;
        public int maxMana;
        public float income;
        public int earnedLastSec, spentLastSec;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxProjectile
    {
        public int id, player, color, kind, model;
        public float x, y, z, vx, vy, vz, heading, pitch, roll, fromX, fromY, fromZ;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxSaveInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 96)] public string map;
        public uint tick;
        public ulong savedAt;
        public int players;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxSeat
    {
        public int kind, side, team, color, difficulty;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxSkirmish
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 96)] public string map;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string kingdom;
        public int aiPlayers, lineOfSight, mapRevealed;
        public uint seed;
        public int seatCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public OkxSeat[] seats;
        public int unitsPerSide, monarchExpendable, randomStartLocations;

        // Every field set, seats included, ready to hand to the engine.
        public static OkxSkirmish For(string map) =>
            new OkxSkirmish { map = map, kingdom = "aramon", seats = new OkxSeat[8] };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxTerrainInfo
    {
        public int mapW, mapH, heightsW, heightsH, tilePx, blocksW, blocksH, blockPx, subPx, chunkCount, waterHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxModelInfo
    {
        public int vertCount, indexCount, nodeCount, batchCount;
        public float minX, minY, minZ, maxX, maxY, maxZ;
        public float scale;
        public int fromOverride;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxNode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name;
        public int parent;
        public float offX, offY, offZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxBatch
    {
        public int firstIndex, indexCount, texture;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxUnit
    {
        public int handle;
        public uint stableId;
        public int def, player, color, state;
        public float x, y, z, heading, pitch, roll;
        public int health, maxHealth, building, model;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxFeature
    {
        public int index, def;
        public float x, y, z, heading, pitch, roll;
        public int model, sprite;
        public float top, bottom, offX, w;
        public int flat;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct OkxFeatureDefInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string obj;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string seqname;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string category;
        public int footprintX, footprintZ, height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxEffect
    {
        public int kind, id, sprite, frame;
        public float x, y, z, top, bottom, offX, w, u0, u1, v1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkxOrder
    {
        public int kind, target, x, y, building;
    }

    // The engine's command types (TAK_CMD_* in tak_commands.h).
    public enum OkxCmd
    {
        Move = 1, Attack, Build, Stop, Patrol, Guard, Repair, Reclaim, Capture,
        Load, Unload, Wait, SetAggro, SetWeapon,
        FactoryEnqueue, FactoryDequeue, FactoryCancel, Rally, Gate, AttackGround,
        SpecialWeapon, ReclaimFeature, ResurrectFeature
    }

    public static class OkEngine
    {
        const string Lib = "okengine";
        public const int ApiVersion = 13;
        public const int ArmNone = 0, ArmMove = 1, ArmAttack = 2, ArmGuard = 3, ArmPatrol = 4,
            ArmLoad = 5, ArmUnload = 6, ArmHeal = 7, ArmClear = 8, ArmBuild = 200;
        public const int EffectImpact = 0, EffectProjectile = 1;
        public const int ProjDot = 0, ProjModel = 1, ProjSprite = 2, ProjBeam = 3;
        public const int UnitActive = 1, UnitDying = 2;

        [DllImport(Lib)] public static extern int okx_api_version();
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_init(string gameDir, string dataDir);
        [DllImport(Lib)] public static extern void okx_shutdown();
        [DllImport(Lib)] static extern IntPtr okx_last_error();
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern void okx_set_override_dir(string dir);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern void okx_set_user_dir(string dir);

        [DllImport(Lib)] public static extern int okx_map_count();
        [DllImport(Lib)] static extern int okx_map_name(int index, [Out] byte[] outName, int cap);
        [DllImport(Lib)] public static extern int okx_def_count();
        [DllImport(Lib)] public static extern int okx_def_info(int def, out OkxDefInfo info);
        [DllImport(Lib)] public static extern int okx_def_buildables(int def, [Out] int[] defs, int cap);
        [DllImport(Lib)] public static extern int okx_unit_picture(int def, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] static extern int okx_def_scripts(int def, [Out] byte[] names, int cap);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_studio_pose(int def, int color, string script, int ticks,
            [Out] float[] matrices, [Out] byte[] hidden, int cap);
        [DllImport(Lib)] public static extern int okx_map_info(int index, out OkxMapInfo info);
        [DllImport(Lib)] public static extern int okx_map_preview(int index, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_players([Out] OkxPlayer[] players, int cap);
        [DllImport(Lib)] public static extern int okx_economy(int player, out OkxEconomy economy);
        [DllImport(Lib)] public static extern int okx_projectiles([Out] OkxProjectile[] projectiles, int cap);
        [DllImport(Lib)] public static extern int okx_projectile_pose(int id, [Out] float[] matrices, int cap);
        [DllImport(Lib)] public static extern int okx_effects([Out] OkxEffect[] effects, int cap);
        [DllImport(Lib)] public static extern int okx_effect_strip(int sprite, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_build_site(int def, int x, int y, out int sx, out int sy);
        [DllImport(Lib)] public static extern int okx_factory_queue(int handle, int def);
        [DllImport(Lib)] public static extern int okx_unit_order(int handle, out OkxOrder order);
        [DllImport(Lib)] public static extern int okx_fog([Out] byte[] cells, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_select(int[] handles, int n, int add);
        [DllImport(Lib)] public static extern int okx_selection([Out] int[] handles, int cap);
        [DllImport(Lib)] public static extern void okx_click(float x, float z, int unit, int shift);
        [DllImport(Lib)] public static extern void okx_cancel();
        [DllImport(Lib)] public static extern void okx_arm(int mode, int def);
        [DllImport(Lib)] public static extern int okx_armed(out int def);
        [DllImport(Lib)] public static extern int okx_order_selection(int type, int arg);
        [DllImport(Lib)] public static extern void okx_group_assign(int group);
        [DllImport(Lib)] public static extern int okx_group_recall(int group);

        [DllImport(Lib)] public static extern int okx_start_skirmish(ref OkxSkirmish cfg);
        [DllImport(Lib)] public static extern int okx_load_begin(ref OkxSkirmish cfg);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_save(string path);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_load_save_begin(string path);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_save_info(string path, out OkxSaveInfo info);
        [DllImport(Lib)] public static extern int okx_load_step(int maxMs, out float progress, [Out] byte[] status, int cap);
        [DllImport(Lib)] public static extern void okx_end_game();
        [DllImport(Lib)] public static extern int okx_tick_rate();
        [DllImport(Lib)] public static extern int okx_tick(int n);
        [DllImport(Lib)] public static extern uint okx_tick_count();
        [DllImport(Lib)] public static extern int okx_local_player();
        [DllImport(Lib)] public static extern int okx_outcome();
        [DllImport(Lib)] public static extern int okx_audio(int enable, int volume, int music);
        [DllImport(Lib)] public static extern void okx_set_view(int cx, int cy, int w, int h);
        [DllImport(Lib)] public static extern int okx_command(int type, int handle, int x, int y, int target, int buildDef, int arg);

        [DllImport(Lib)] public static extern int okx_terrain_info(out OkxTerrainInfo info);
        [DllImport(Lib)] public static extern int okx_terrain_heights([Out] float[] heights, int cap);
        [DllImport(Lib)] public static extern int okx_terrain_blocks([Out] int[] blocks, int cap);
        [DllImport(Lib)] public static extern int okx_terrain_chunk(int chunk, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern float okx_ground_height(float x, float z);
        [DllImport(Lib)] public static extern int okx_map_cells([Out] byte[] cells, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_edit_cells(int x0, int z0, int w, int h, byte[] values);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_map_save(string name);
        [DllImport(Lib)] public static extern int okx_chunk_library([Out] uint[] ids, int cap);
        [DllImport(Lib)] public static extern int okx_chunk_picture(uint id, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern uint okx_terrain_chunk_id(int chunk);
        [DllImport(Lib)] public static extern int okx_edit_blocks(int bx, int by, int w, int h, uint[] chunkIds, byte[] texX, byte[] texY);
        [DllImport(Lib)] public static extern int okx_feature_place(int def, int cx, int cz);
        [DllImport(Lib)] public static extern int okx_feature_remove(int index);

        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_model_load(string objectName, int color);
        [DllImport(Lib)] public static extern int okx_model_info(int model, out OkxModelInfo info);
        [DllImport(Lib)] public static extern int okx_model_geometry(int model, [Out] float[] positions, [Out] float[] normals,
            [Out] float[] uvs, [Out] uint[] colors, [Out] int[] nodes, [Out] int[] indices);
        [DllImport(Lib)] public static extern int okx_model_nodes(int model, [Out] OkxNode[] nodes, int cap);
        [DllImport(Lib)] public static extern int okx_model_batches(int model, [Out] OkxBatch[] batches, int cap);
        [DllImport(Lib)] public static extern int okx_texture(int texture, [Out] byte[] rgba, int cap, out int w, out int h);

        [DllImport(Lib)] public static extern int okx_units([Out] OkxUnit[] units, int cap);
        [DllImport(Lib)] public static extern int okx_unit_pose(int handle, [Out] float[] matrices, [Out] byte[] hidden, int cap);
        [DllImport(Lib)] public static extern int okx_features([Out] OkxFeature[] features, int cap);
        [DllImport(Lib)] public static extern int okx_feature_pose(int index, [Out] float[] matrices, int cap);
        [DllImport(Lib)] public static extern int okx_sprite(int def, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_feature_def_count();
        [DllImport(Lib)] public static extern int okx_feature_def_info(int def, out OkxFeatureDefInfo info);

        public static string LastError => Marshal.PtrToStringAnsi(okx_last_error()) ?? "";

        public static string MapName(int index)
        {
            var buf = new byte[96];
            int n = okx_map_name(index, buf, buf.Length);
            return n > 0 ? Encoding.ASCII.GetString(buf, 0, n) : null;
        }

        // The def's unit script functions, or none.
        public static string[] Scripts(int def)
        {
            var buf = new byte[8192];
            int n = okx_def_scripts(def, buf, buf.Length);
            if (n <= 0) return Array.Empty<string>();
            return Encoding.ASCII.GetString(buf, 0, n).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string[] Maps()
        {
            int n = okx_map_count();
            var names = new string[n];
            for (int i = 0; i < n; i++) names[i] = MapName(i);
            return names;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);
#endif

        // okengine.dll needs SDL2.dll, which Windows only finds next to
        // the executable or on PATH. Loading it by full path first makes
        // the one beside the plugin the one in use.
        public static void PreloadDependencies(string pluginDir)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string sdl = Path.Combine(pluginDir, "SDL2.dll");
            if (File.Exists(sdl)) LoadLibraryW(sdl);
#endif
        }
    }
}
