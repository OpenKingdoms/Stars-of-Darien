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
    public struct OkxSkirmish
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 96)] public string map;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string kingdom;
        public int aiPlayers, lineOfSight, mapRevealed;
        public uint seed;
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

    // The engine's command types (TAK_CMD_* in tak_commands.h).
    public enum OkxCmd
    {
        Move = 1, Attack, Build, Stop, Patrol, Guard, Repair, Reclaim, Capture,
        Load, Unload, Wait, SetAggro, SetWeapon
    }

    public static class OkEngine
    {
        const string Lib = "okengine";
        public const int ApiVersion = 2;
        public const int ProjDot = 0, ProjModel = 1, ProjSprite = 2, ProjBeam = 3;
        public const int UnitActive = 1, UnitDying = 2;

        [DllImport(Lib)] public static extern int okx_api_version();
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int okx_init(string gameDir, string dataDir);
        [DllImport(Lib)] public static extern void okx_shutdown();
        [DllImport(Lib)] static extern IntPtr okx_last_error();
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern void okx_set_override_dir(string dir);

        [DllImport(Lib)] public static extern int okx_map_count();
        [DllImport(Lib)] static extern int okx_map_name(int index, [Out] byte[] outName, int cap);
        [DllImport(Lib)] public static extern int okx_def_count();
        [DllImport(Lib)] public static extern int okx_def_info(int def, out OkxDefInfo info);
        [DllImport(Lib)] public static extern int okx_def_buildables(int def, [Out] int[] defs, int cap);
        [DllImport(Lib)] public static extern int okx_map_info(int index, out OkxMapInfo info);
        [DllImport(Lib)] public static extern int okx_map_preview(int index, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern int okx_players([Out] OkxPlayer[] players, int cap);
        [DllImport(Lib)] public static extern int okx_economy(int player, out OkxEconomy economy);
        [DllImport(Lib)] public static extern int okx_projectiles([Out] OkxProjectile[] projectiles, int cap);
        [DllImport(Lib)] public static extern int okx_projectile_pose(int id, [Out] float[] matrices, int cap);

        [DllImport(Lib)] public static extern int okx_start_skirmish(ref OkxSkirmish cfg);
        [DllImport(Lib)] public static extern void okx_end_game();
        [DllImport(Lib)] public static extern int okx_tick_rate();
        [DllImport(Lib)] public static extern int okx_tick(int n);
        [DllImport(Lib)] public static extern uint okx_tick_count();
        [DllImport(Lib)] public static extern int okx_local_player();
        [DllImport(Lib)] public static extern int okx_outcome();
        [DllImport(Lib)] public static extern int okx_command(int type, int handle, int x, int y, int target, int buildDef, int arg);

        [DllImport(Lib)] public static extern int okx_terrain_info(out OkxTerrainInfo info);
        [DllImport(Lib)] public static extern int okx_terrain_heights([Out] float[] heights, int cap);
        [DllImport(Lib)] public static extern int okx_terrain_blocks([Out] int[] blocks, int cap);
        [DllImport(Lib)] public static extern int okx_terrain_chunk(int chunk, [Out] byte[] rgba, int cap, out int w, out int h);
        [DllImport(Lib)] public static extern float okx_ground_height(float x, float z);

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
