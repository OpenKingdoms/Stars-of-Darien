// OkSim.cs - the C# side of core/include/ok_sim.h. Keep the two in step:
// a change to a struct or signature there bumps OK_SIM_ABI_VERSION, and
// AbiVersion below must match it or Create refuses to run.
using System;
using System.Runtime.InteropServices;

namespace OpenKingdomsUnity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OkUnitView
    {
        public int id, player, x, y, heading, state;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OkEvent
    {
        public int kind, tick, unit, a, b;
    }

    public static class OkNative
    {
        // The editor and desktop Mono players load okcore.dll/.so/.dylib by
        // name. IL2CPP on WebGL and iOS links the static library in.
#if (UNITY_WEBGL || UNITY_IOS) && !UNITY_EDITOR
        const string Lib = "__Internal";
#else
        const string Lib = "okcore";
#endif
        public const int AbiVersion = 1;
        public const int FixedOne = 65536;
        public const int TickRate = 30;
        public const int CmdBytes = 16;
        public const int UnitIdle = 0, UnitMoving = 1;
        public const int EventSpawned = 1, EventArrived = 2;

        [DllImport(Lib)] public static extern uint ok_sim_abi_version();
        [DllImport(Lib)] public static extern IntPtr ok_sim_create(uint seed, int mapW, int mapH);
        [DllImport(Lib)] public static extern void ok_sim_destroy(IntPtr sim);
        [DllImport(Lib)] public static extern int ok_sim_push_command(IntPtr sim, byte[] bytes, int len);
        [DllImport(Lib)] public static extern void ok_sim_tick(IntPtr sim);
        [DllImport(Lib)] public static extern uint ok_sim_tick_count(IntPtr sim);
        [DllImport(Lib)] public static extern ulong ok_sim_hash(IntPtr sim);
        [DllImport(Lib)] public static extern int ok_sim_snapshot(IntPtr sim, [Out] OkUnitView[] outViews, int cap);
        [DllImport(Lib)] public static extern int ok_sim_drain_events(IntPtr sim, [Out] OkEvent[] outEvents, int cap);
        [DllImport(Lib)] public static extern int ok_cmd_spawn(byte[] outBytes, int player, int x, int y);
        [DllImport(Lib)] public static extern int ok_cmd_move(byte[] outBytes, int player, int unit, int x, int y);
    }

    // Owns one native sim and frees it with the object.
    public sealed class OkSim : IDisposable
    {
        IntPtr handle;
        readonly byte[] cmd = new byte[OkNative.CmdBytes];

        public OkSim(uint seed, int mapW, int mapH)
        {
            uint abi = OkNative.ok_sim_abi_version();
            if (abi != OkNative.AbiVersion)
                throw new InvalidOperationException(
                    $"okcore ABI {abi}, this binding expects {OkNative.AbiVersion}. Rebuild the plugin.");
            handle = OkNative.ok_sim_create(seed, mapW, mapH);
            if (handle == IntPtr.Zero) throw new OutOfMemoryException("ok_sim_create");
        }

        public uint Tick => OkNative.ok_sim_tick_count(handle);
        public ulong Hash => OkNative.ok_sim_hash(handle);

        public void Spawn(int player, float cellX, float cellY)
        {
            OkNative.ok_cmd_spawn(cmd, player, ToFixed(cellX), ToFixed(cellY));
            OkNative.ok_sim_push_command(handle, cmd, cmd.Length);
        }

        public void Move(int player, int unit, float cellX, float cellY)
        {
            OkNative.ok_cmd_move(cmd, player, unit, ToFixed(cellX), ToFixed(cellY));
            OkNative.ok_sim_push_command(handle, cmd, cmd.Length);
        }

        public void Step() => OkNative.ok_sim_tick(handle);

        public int Snapshot(OkUnitView[] into) => OkNative.ok_sim_snapshot(handle, into, into.Length);

        public int DrainEvents(OkEvent[] into) => OkNative.ok_sim_drain_events(handle, into, into.Length);

        public static int ToFixed(float cells) => (int)Math.Round(cells * OkNative.FixedOne);
        public static float ToCells(int fixedValue) => fixedValue / (float)OkNative.FixedOne;

        public void Dispose()
        {
            if (handle != IntPtr.Zero) OkNative.ok_sim_destroy(handle);
            handle = IntPtr.Zero;
        }
    }
}
