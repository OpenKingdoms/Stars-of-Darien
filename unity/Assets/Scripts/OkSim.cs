// OkSim.cs - the C# side of core/include/ok_sim.h, ok_turn.h and ok_ai.h.
// Keep them in step: a change to a struct or signature there bumps
// OK_SIM_ABI_VERSION, and AbiVersion below must match it or OkSim refuses
// to run.
using System;
using System.Runtime.InteropServices;

namespace OpenKingdomsUnity
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OkUnitView
    {
        public int id, player, kind, x, y, heading, state, hp, maxHp, target;
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
        public const int AbiVersion = 2;
        public const int FixedOne = 65536;
        public const int TickRate = 30;
        public const int MaxSide = 256, MaxUnits = 1024;
        public const int CmdBytes = 16;
        public const int KindSoldier = 0, KindArcher = 1, KindCount = 2;
        public const int UnitIdle = 0, UnitMoving = 1, UnitAttacking = 2, UnitDead = 3;
        public const int EventSpawned = 1, EventArrived = 2, EventAttacked = 3, EventDied = 4;
        public const int TurnTicks = 3, TurnMaxCmds = 64, TurnHeader = 8, TurnSeats = 8;
        public const int TurnFrameMax = TurnHeader + TurnMaxCmds * CmdBytes;

        [DllImport(Lib)] public static extern uint ok_sim_abi_version();
        [DllImport(Lib)] public static extern IntPtr ok_sim_create(uint seed, int mapW, int mapH);
        [DllImport(Lib)] public static extern void ok_sim_destroy(IntPtr sim);
        [DllImport(Lib)] public static extern int ok_sim_set_blocked(IntPtr sim, int cx, int cy, int blocked);
        [DllImport(Lib)] public static extern int ok_sim_is_blocked(IntPtr sim, int cx, int cy);
        [DllImport(Lib)] public static extern int ok_sim_push_command(IntPtr sim, byte[] bytes, int len);
        [DllImport(Lib)] public static extern void ok_sim_tick(IntPtr sim);
        [DllImport(Lib)] public static extern uint ok_sim_tick_count(IntPtr sim);
        [DllImport(Lib)] public static extern ulong ok_sim_hash(IntPtr sim);
        [DllImport(Lib)] public static extern int ok_sim_snapshot(IntPtr sim, [Out] OkUnitView[] outViews, int cap);
        [DllImport(Lib)] public static extern int ok_sim_drain_events(IntPtr sim, [Out] OkEvent[] outEvents, int cap);
        [DllImport(Lib)] public static extern int ok_sim_winner(IntPtr sim);
        [DllImport(Lib)] public static extern int ok_cmd_spawn([Out] byte[] outBytes, int player, int kind, int x, int y);
        [DllImport(Lib)] public static extern int ok_cmd_move([Out] byte[] outBytes, int player, int unit, int x, int y);
        [DllImport(Lib)] public static extern int ok_cmd_attack([Out] byte[] outBytes, int player, int unit, int target);
        [DllImport(Lib)] public static extern int ok_cmd_stop([Out] byte[] outBytes, int player, int unit);

        [DllImport(Lib)] public static extern IntPtr ok_relay_create();
        [DllImport(Lib)] public static extern void ok_relay_destroy(IntPtr relay);
        [DllImport(Lib)] public static extern int ok_relay_command(IntPtr relay, int seat, byte[] bytes, int len);
        [DllImport(Lib)] public static extern int ok_relay_close_turn(IntPtr relay, [Out] byte[] outFrame, int cap);
        [DllImport(Lib)] public static extern uint ok_relay_turns(IntPtr relay);
        [DllImport(Lib)] public static extern int ok_relay_report_hash(IntPtr relay, int seat, uint turn, ulong hash);
        [DllImport(Lib)] public static extern uint ok_relay_desyncs(IntPtr relay);

        [DllImport(Lib)] public static extern IntPtr ok_peer_create();
        [DllImport(Lib)] public static extern void ok_peer_destroy(IntPtr peer);
        [DllImport(Lib)] public static extern int ok_peer_receive(IntPtr peer, byte[] frame, int len);
        [DllImport(Lib)] public static extern int ok_peer_step(IntPtr peer, IntPtr sim);
        [DllImport(Lib)] public static extern uint ok_peer_turns(IntPtr peer);

        [DllImport(Lib)] public static extern IntPtr ok_ai_create(int player, int baseCx, int baseCy, int waves);
        [DllImport(Lib)] public static extern void ok_ai_destroy(IntPtr ai);
        [DllImport(Lib)] public static extern int ok_ai_think(IntPtr ai, IntPtr sim, [Out] byte[] outBytes, int cap);
        [DllImport(Lib)] public static extern int ok_ai_waves_left(IntPtr ai);
    }

    // Command bytes, built by the core so the layout lives in one place.
    public static class OkCmd
    {
        public static byte[] Spawn(int player, int kind, float cellX, float cellY)
        {
            var b = new byte[OkNative.CmdBytes];
            OkNative.ok_cmd_spawn(b, player, kind, OkSim.ToFixed(cellX), OkSim.ToFixed(cellY));
            return b;
        }

        public static byte[] Move(int player, int unit, float cellX, float cellY)
        {
            var b = new byte[OkNative.CmdBytes];
            OkNative.ok_cmd_move(b, player, unit, OkSim.ToFixed(cellX), OkSim.ToFixed(cellY));
            return b;
        }

        public static byte[] Attack(int player, int unit, int target)
        {
            var b = new byte[OkNative.CmdBytes];
            OkNative.ok_cmd_attack(b, player, unit, target);
            return b;
        }

        public static byte[] Stop(int player, int unit)
        {
            var b = new byte[OkNative.CmdBytes];
            OkNative.ok_cmd_stop(b, player, unit);
            return b;
        }
    }

    // Owns one native sim and frees it with the object.
    public sealed class OkSim : IDisposable
    {
        IntPtr handle;

        public OkSim(uint seed, int mapW, int mapH)
        {
            uint abi = OkNative.ok_sim_abi_version();
            if (abi != OkNative.AbiVersion)
                throw new InvalidOperationException(
                    $"okcore ABI {abi}, this binding expects {OkNative.AbiVersion}. Rebuild the plugin.");
            handle = OkNative.ok_sim_create(seed, mapW, mapH);
            if (handle == IntPtr.Zero) throw new ArgumentException("ok_sim_create refused the map size");
            MapW = mapW;
            MapH = mapH;
        }

        public IntPtr Handle => handle;
        public int MapW { get; }
        public int MapH { get; }
        public uint Tick => OkNative.ok_sim_tick_count(handle);
        public ulong Hash => OkNative.ok_sim_hash(handle);
        // The one player with living units, -1 while two or more have
        // some, -2 when nobody does.
        public int Winner => OkNative.ok_sim_winner(handle);

        public void SetBlocked(int cx, int cy, bool blocked) =>
            OkNative.ok_sim_set_blocked(handle, cx, cy, blocked ? 1 : 0);
        public bool IsBlocked(int cx, int cy) => OkNative.ok_sim_is_blocked(handle, cx, cy) != 0;

        public bool Push(byte[] cmd) => OkNative.ok_sim_push_command(handle, cmd, cmd.Length) == 0;
        public void Spawn(int player, int kind, float cellX, float cellY) => Push(OkCmd.Spawn(player, kind, cellX, cellY));
        public void Move(int player, int unit, float cellX, float cellY) => Push(OkCmd.Move(player, unit, cellX, cellY));
        public void Attack(int player, int unit, int target) => Push(OkCmd.Attack(player, unit, target));
        public void Stop(int player, int unit) => Push(OkCmd.Stop(player, unit));

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

    // The relay half of lockstep: gathers commands, closes turns.
    public sealed class OkRelay : IDisposable
    {
        IntPtr handle = OkNative.ok_relay_create();

        public uint Turns => OkNative.ok_relay_turns(handle);
        public uint Desyncs => OkNative.ok_relay_desyncs(handle);
        public bool Command(int seat, byte[] cmd) => OkNative.ok_relay_command(handle, seat, cmd, cmd.Length) == 0;
        public int CloseTurn(byte[] frame) => OkNative.ok_relay_close_turn(handle, frame, frame.Length);
        public bool ReportHash(int seat, uint turn, ulong hash) =>
            OkNative.ok_relay_report_hash(handle, seat, turn, hash) != 0;

        public void Dispose()
        {
            if (handle != IntPtr.Zero) OkNative.ok_relay_destroy(handle);
            handle = IntPtr.Zero;
        }
    }

    // The peer half of lockstep: runs a turn once it holds its frame.
    public sealed class OkPeer : IDisposable
    {
        IntPtr handle = OkNative.ok_peer_create();

        public uint Turns => OkNative.ok_peer_turns(handle);
        public bool Receive(byte[] frame, int len) => OkNative.ok_peer_receive(handle, frame, len) == 0;
        public bool Step(OkSim sim) => OkNative.ok_peer_step(handle, sim.Handle) != 0;

        public void Dispose()
        {
            if (handle != IntPtr.Zero) OkNative.ok_peer_destroy(handle);
            handle = IntPtr.Zero;
        }
    }

    // The wave opponent. Its commands go through the relay like a player's.
    public sealed class OkAi : IDisposable
    {
        IntPtr handle;
        readonly byte[] buffer = new byte[OkNative.CmdBytes * 256];
        readonly byte[] one = new byte[OkNative.CmdBytes];

        public OkAi(int player, int baseCx, int baseCy, int waves)
        {
            Player = player;
            handle = OkNative.ok_ai_create(player, baseCx, baseCy, waves);
        }

        public int Player { get; }
        public int WavesLeft => OkNative.ok_ai_waves_left(handle);

        // Think once a turn and hand every command to send.
        public void Think(OkSim sim, Action<byte[]> send)
        {
            int n = OkNative.ok_ai_think(handle, sim.Handle, buffer, buffer.Length);
            for (int i = 0; i + OkNative.CmdBytes <= n; i += OkNative.CmdBytes)
            {
                Buffer.BlockCopy(buffer, i, one, 0, OkNative.CmdBytes);
                send(one);
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) OkNative.ok_ai_destroy(handle);
            handle = IntPtr.Zero;
        }
    }

    // One machine playing alone, still through lockstep: commands go to a
    // local relay and come back as turns, exactly as they would over a
    // network, so a networked game changes only where the frames travel.
    public sealed class OkLocalLockstep : IDisposable
    {
        readonly OkRelay relay = new OkRelay();
        readonly OkPeer peer = new OkPeer();
        readonly byte[] frame = new byte[OkNative.TurnFrameMax];

        public OkLocalLockstep(OkSim sim) { Sim = sim; }

        public OkSim Sim { get; }
        public uint Turns => peer.Turns;

        public bool Send(int seat, byte[] cmd) => relay.Command(seat, cmd);

        // One sim tick. When the next turn is needed, beforeClose runs
        // first so a computer player can add its commands to it.
        public void Tick(Action beforeClose)
        {
            if (peer.Step(Sim)) return;
            beforeClose?.Invoke();
            int len = relay.CloseTurn(frame);
            peer.Receive(frame, len);
            peer.Step(Sim);
        }

        public void Dispose()
        {
            relay.Dispose();
            peer.Dispose();
        }
    }
}
