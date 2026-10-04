// MockOrders.cs - the mock's order queue and factories. Shift's orders
// wait behind the ones a unit has and each starts when the one before
// ends. A factory makes its queue in turn, or one def over and over, and
// sends each unit to its rally point. A building still being built takes
// only a queue, which waits for it to finish.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        // An order waiting its turn: a formation's leg or a command.
        struct Pending
        {
            public bool IsLeg;
            public Leg Leg;
            public GameCommand Command;
        }

        readonly Dictionary<int, List<Pending>> pending = new Dictionary<int, List<Pending>>();

        void Enqueue(int handle, Pending p)
        {
            if (!pending.TryGetValue(handle, out var list)) pending[handle] = list = new List<Pending>();
            list.Add(p);
        }

        bool Busy(Unit u) =>
            u.Ordered && (u.Goal != null || u.Target >= 0) || u.BuildDef >= 0 || u.Helps >= 0
            || pending.TryGetValue(u.Handle, out var q) && q.Count > 0;

        // Once the order in hand is done, the next queued one starts.
        void Next(Unit u)
        {
            if (u.Goal == null && u.Target < 0 && u.Helps < 0) u.Ordered = false;
            if (u.Ordered || u.BuildDef >= 0 || u.Helps >= 0) return;
            if (!pending.TryGetValue(u.Handle, out var q)) return;
            while (q.Count > 0)
            {
                var p = q[0];
                q.RemoveAt(0);
                if (p.IsLeg) { Begin(u, p.Leg); return; }
                if (Apply(u, p.Command)) return;
            }
        }

        // ---- Factories ----

        static bool Makes(UnitDef d) => d.IsBuilding && d.BuildOptions.Length > 0;

        bool FactoryCommand(Unit f, in GameCommand c)
        {
            switch (c.Kind)
            {
                case CommandKind.Build:
                case CommandKind.FactoryEnqueue: return AddToQueue(f.Handle, c.BuildDef, 1);
                case CommandKind.FactoryDequeue: return AddToQueue(f.Handle, c.BuildDef, -1);
                case CommandKind.FactoryCancel:
                    if (f.Built < 1f) return false;
                    f.BuildDef = -1;
                    f.Line.Clear();
                    f.Repeat = -1;
                    return true;
                case CommandKind.Move:
                case CommandKind.Patrol:
                case CommandKind.Rally:
                    // Where its units go, as the original's Move on a factory sets it.
                    if (f.Built < 1f || !Makes(unitDefs[f.Def])) return false;
                    f.Rally = new Vector2(c.Target.x, c.Target.z);
                    return true;
                case CommandKind.Stop:
                    if (f.Built < 1f) return false;
                    f.BuildDef = -1;
                    f.Line.Clear();
                    f.Repeat = -1;
                    return true;
                default:
                    return false;
            }
        }

        Unit OwnFactory(int handle, int def)
        {
            if (Status != GameStatus.Running || !byHandle.TryGetValue(handle, out var f) || f.Dying || f.Player != 0) return null;
            var d = unitDefs[f.Def];
            return Makes(d) && System.Array.IndexOf(d.BuildOptions, def) >= 0 ? f : null;
        }

        // A builder of yours that walks and can make def.
        Unit OwnBuilder(int handle, int def)
        {
            if (Status != GameStatus.Running || !byHandle.TryGetValue(handle, out var u) || u.Dying || u.Player != 0) return null;
            var d = unitDefs[u.Def];
            return !d.IsBuilding && System.Array.IndexOf(d.BuildOptions, def) >= 0 ? u : null;
        }

        // The def a walking builder summons without end, in hand or queued, or -1.
        int SummonsOf(Unit u)
        {
            if (unitDefs[u.Def].IsBuilding) return -1;
            if (u.Repeat >= 0) return u.Repeat;
            if (pending.TryGetValue(u.Handle, out var q))
                foreach (var p in q)
                    if (!p.IsLeg && p.Command.Kind == CommandKind.Build && p.Command.Endless) return p.Command.BuildDef;
            return -1;
        }

        // A walking builder's build orders of def come off, the one in hand
        // first, as the original's right click on its card takes them.
        bool DropBuilds(Unit u, int def, int count)
        {
            int was = count;
            if (u.BuildDef == def && count > 0)
            {
                u.BuildDef = -1;
                u.BuildAt = null;
                u.Repeat = -1;
                count--;
            }
            if (pending.TryGetValue(u.Handle, out var q))
                for (int i = 0; i < q.Count && count > 0;)
                {
                    if (!q[i].IsLeg && q[i].Command.Kind == CommandKind.Build && q[i].Command.BuildDef == def) { q.RemoveAt(i); count--; }
                    else i++;
                }
            if (count < was) Next(u);
            return count < was;
        }

        public bool AddToQueue(int factory, int def, int count)
        {
            if (count < 0 && OwnBuilder(factory, def) is Unit b) return DropBuilds(b, def, -count);
            var f = OwnFactory(factory, def);
            if (f == null || count == 0) return false;
            if (count > 0)
            {
                for (int i = 0; i < count; i++) f.Line.Add(def);
                if (f.BuildDef < 0) StartNext(f);
                return true;
            }
            int take = -count;
            for (int i = f.Line.Count - 1; i >= 0 && take > 0; i--)
                if (f.Line[i] == def) { f.Line.RemoveAt(i); take--; }
            if (take > 0 && f.BuildDef == def) { f.BuildDef = -1; take--; StartNext(f); }
            return take < -count;
        }

        public bool SetRepeat(int factory, int def, bool on)
        {
            if (!on && OwnBuilder(factory, def) is Unit b)
                return SummonsOf(b) == def && DropBuilds(b, def, int.MaxValue);
            var f = OwnFactory(factory, def);
            if (f == null) return false;
            if (on)
            {
                f.Repeat = def;
                if (f.BuildDef < 0) StartNext(f);
                return true;
            }
            if (f.Repeat != def) return false;
            // Off drops every one of that def, as the original's right click on a +++ card.
            f.Repeat = -1;
            f.Line.RemoveAll(d => d == def);
            if (f.BuildDef == def) { f.BuildDef = -1; StartNext(f); }
            return true;
        }

        public int RepeatOf(int factory)
        {
            if (!byHandle.TryGetValue(factory, out var f) || f.Dying) return -1;
            return unitDefs[f.Def].IsBuilding ? f.Repeat : SummonsOf(f);
        }

        // The next unit off the line, or the repeated one when the line is dry.
        void StartNext(Unit f)
        {
            if (f.BuildDef >= 0) return;
            int next = -1;
            if (f.Line.Count > 0) { next = f.Line[0]; f.Line.RemoveAt(0); }
            else if (f.Repeat >= 0) next = f.Repeat;
            if (next < 0) return;
            f.BuildDef = next;
            f.BuildAt = null;
            f.BuildLeft = 5f;
        }

        public int QueuedCount(int factory, int def)
        {
            if (!byHandle.TryGetValue(factory, out var u)) return 0;
            int n = u.BuildDef >= 0 && (def < 0 || u.BuildDef == def) ? 1 : 0;
            foreach (int d in u.Line) if (def < 0 || d == def) n++;
            return n;
        }

        // ---- The order lines ----

        public int ReadOrderQueue(int handle, OrderLeg[] into)
        {
            if (!byHandle.TryGetValue(handle, out var u) || u.Dying) return 0;
            int n = 0;
            void Put(OrderLeg leg)
            {
                if (into != null && n < into.Length) into[n] = leg;
                n++;
            }
            if (unitDefs[u.Def].IsBuilding)
            {
                if (u.Rally is Vector2 r) Put(LegTo(OrderKind.Move, r));
                return n;
            }
            var now = ReadOrder(handle);
            if (now.Kind != OrderKind.None)
                Put(new OrderLeg
                {
                    Kind = now.Kind == OrderKind.Move ? u.OrderKind : now.Kind, Target = now.Kind == OrderKind.Build && u.BuildAt is Vector3 site ? site : now.Target,
                    TargetUnit = now.TargetUnit, BuildDef = now.Kind == OrderKind.Build ? u.BuildDef : -1, Facing = u.BuildFacing,
                });
            if (pending.TryGetValue(handle, out var q))
                foreach (var p in q)
                {
                    if (p.IsLeg) { Put(LegTo(OrderKind.Move, p.Leg.To)); continue; }
                    var c = p.Command;
                    var kind = c.Kind == CommandKind.Attack ? OrderKind.Attack : c.Kind == CommandKind.Patrol ? OrderKind.Patrol
                        : c.Kind == CommandKind.Repair ? OrderKind.Repair
                        : c.Kind == CommandKind.Build || c.Kind == CommandKind.FactoryEnqueue ? OrderKind.Build : OrderKind.Move;
                    var at = c.Target;
                    if (c.TargetUnit >= 0 && byHandle.TryGetValue(c.TargetUnit, out var t)) at = t.Pos;
                    if (kind == OrderKind.Build && CanBuildAt(c.BuildDef, c.Target, c.Facing, out var snapped)) at = snapped;
                    Put(new OrderLeg { Kind = kind, Target = at, TargetUnit = c.TargetUnit, BuildDef = kind == OrderKind.Build ? c.BuildDef : -1, Facing = c.Facing });
                }
            return n;
        }

        OrderLeg LegTo(OrderKind kind, Vector2 to) => new OrderLeg
        {
            Kind = kind, Target = new Vector3(to.x, Terrain != null ? Terrain.Sample(to.x, to.y) : 0f, to.y), TargetUnit = -1, BuildDef = -1,
        };

        // ---- Selection and sound ----

        // A frame can be chosen only to queue units in it.
        bool Selectable(Unit u) => u.Built >= 1f || Makes(unitDefs[u.Def]);

        // The mock is silent, and keeps what it was asked to play, for tests.
        public readonly List<string> SoundsPlayed = new List<string>();

        public bool PlaySound(string wav, float volume)
        {
            if (string.IsNullOrEmpty(wav)) return false;
            SoundsPlayed.Add(wav.ToLowerInvariant());
            return false;
        }
    }
}
