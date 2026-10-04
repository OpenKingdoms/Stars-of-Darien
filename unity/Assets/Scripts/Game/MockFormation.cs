// MockFormation.cs - the mock's formation move: each unit walks to its own
// point, held to the slowest unit's pace when asked, turns to the given
// heading on arrival and holds it, and queued moves wait their turn. Any
// other order lets it all go.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        public sealed class FormationCall
        {
            public int[] Units;
            public Vector2[] Targets;
            public float? Heading;
            public bool GroupSpeed, Queue;
            public int Accepted;
        }

        internal struct Leg
        {
            public Vector2 To;
            public float? Heading;
            public float Pace;      // 0 for none
        }

        readonly Dictionary<int, float> paceCap = new Dictionary<int, float>();
        readonly Dictionary<int, float> holdHeading = new Dictionary<int, float>();

        // Every MoveFormation this game and the last one, for tests.
        public readonly List<FormationCall> FormationCalls = new List<FormationCall>();
        public FormationCall LastFormation { get; private set; }

        public const float KnightSpeed = 3.2f, FootSpeed = 2.4f;

        float MockSpeed(Unit u)
        {
            var d = unitDefs[u.Def];
            if (d.CanFly) return d.MaxSpeed;
            return RoleOf(u.Def) == Role.Knight || RoleOf(u.Def) == Role.Wagon ? KnightSpeed : FootSpeed;
        }

        public bool MoveFormation(int[] handles, Vector2[] targets, float? heading, bool groupSpeed, bool queue)
        {
            if (Status != GameStatus.Running || handles == null || targets == null || handles.Length != targets.Length) return false;
            var call = new FormationCall { Units = (int[])handles.Clone(), Targets = (Vector2[])targets.Clone(), Heading = heading, GroupSpeed = groupSpeed, Queue = queue };
            LastFormation = call;
            FormationCalls.Add(call);
            var taken = new List<(Unit u, Vector2 to)>();
            float pace = float.MaxValue;
            for (int i = 0; i < handles.Length; i++)
            {
                if (!byHandle.TryGetValue(handles[i], out var u) || u.Dying || u.Player != 0 || u.Built < 1f) continue;
                if (unitDefs[u.Def].IsBuilding || aboard.Contains(u.Handle)) continue;
                taken.Add((u, targets[i]));
                pace = Mathf.Min(pace, MockSpeed(u));
            }
            foreach (var (u, to) in taken)
            {
                var leg = new Leg { To = to, Heading = heading, Pace = groupSpeed ? pace : 0f };
                if (queue && (u.Goal != null || Busy(u)))
                {
                    Enqueue(u.Handle, new Pending { Leg = leg, IsLeg = true });
                    continue;
                }
                LetGo(u.Handle);
                u.Target = -1;
                Begin(u, leg);
            }
            call.Accepted = taken.Count;
            return taken.Count > 0;
        }

        void Begin(Unit u, Leg leg)
        {
            u.Goal = leg.To;
            u.Home = leg.To;
            u.Target = -1;
            u.Helps = -1;
            u.Ordered = true;
            u.OrderKind = OrderKind.Move;
            if (leg.Pace > 0) paceCap[u.Handle] = leg.Pace; else paceCap.Remove(u.Handle);
            if (leg.Heading is float h) holdHeading[u.Handle] = h; else holdHeading.Remove(u.Handle);
        }

        void ForgetFormations()
        {
            paceCap.Clear();
            holdHeading.Clear();
            pending.Clear();
            FormationCalls.Clear();
            LastFormation = null;
        }

        // Another order: the pace, the held heading and the queue go.
        void LetGo(int handle)
        {
            paceCap.Remove(handle);
            holdHeading.Remove(handle);
            pending.Remove(handle);
        }

        float PacedSpeed(Unit u, float speed) => paceCap.TryGetValue(u.Handle, out float cap) ? Mathf.Min(speed, cap) : speed;

        bool HoldsFormation(Unit u) => holdHeading.ContainsKey(u.Handle);

        // At its point: the pace ends and the next queued leg starts.
        void Arrived(Unit u)
        {
            paceCap.Remove(u.Handle);
            u.Ordered = false;
            Next(u);
        }

        // Idle, a unit turns to its formation's heading and keeps it.
        void HoldFacing(Unit u, float dt)
        {
            if (u.Moving || u.Attacking || u.Goal != null || !holdHeading.TryGetValue(u.Handle, out float h)) return;
            u.Heading = Mathf.MoveTowardsAngle(u.Heading, h, 360f * dt);
        }

        // Queued points still to go, for tests.
        public int QueuedLegs(int handle) => pending.TryGetValue(handle, out var q) ? q.Count : 0;
        public float? PaceOf(int handle) => paceCap.TryGetValue(handle, out float p) ? p : (float?)null;
    }
}
