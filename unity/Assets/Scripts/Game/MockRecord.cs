// MockRecord.cs - the mock's battle record, kept the way the engine keeps
// its own: every creation counted as built, kills and losses, damage up
// to what the victim had left, spells, the champion, finished units by
// kind, the key moments and a sample of every kingdom every 5 s.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        public const int SampleTicks = Tps * 5;

        sealed class Kept
        {
            public int Built, Kills, Losses, Score, LastAlive, Trained, Raised, Dealt, Taken, Spells, Fell;
            public float Gathered, Spent;
            public int Champion = -1, ChampionDef = -1, ChampionKills;
            public readonly Dictionary<int, int> Made = new Dictionary<int, int>();
            public readonly List<int>[] Samples = new List<int>[9];
            public bool Stood = true;
        }

        readonly List<Kept> kept = new List<Kept>();
        readonly List<BattleMoment> moments = new List<BattleMoment>();

        Kept KeptOf(int pos)
        {
            while (kept.Count <= pos) kept.Add(new Kept());
            return kept[pos];
        }

        void ForgetRecord()
        {
            kept.Clear();
            moments.Clear();
        }

        void NoteSpawn(Unit u) => KeptOf(u.Player).Built++;

        void NoteFinished(Unit u)
        {
            var k = KeptOf(u.Player);
            if (unitDefs[u.Def].IsBuilding) k.Raised++; else k.Trained++;
            k.Made.TryGetValue(u.Def, out int n);
            k.Made[u.Def] = n + 1;
        }

        void NoteHit(Unit by, Unit t, int hp)
        {
            if (by == null || by.Player == t.Player || hp <= 0) return;
            KeptOf(by.Player).Dealt += hp;
            KeptOf(t.Player).Taken += hp;
        }

        void NoteDeath(Unit by, Unit t)
        {
            KeptOf(t.Player).Losses++;
            if (by == null || by.Player == t.Player) return;
            var k = KeptOf(by.Player);
            k.Kills++;
            k.Score += unitDefs[t.Def].ManaCost + 50;
            int at = (int)Tick;
            if (!moments.Exists(m => m.Kind == MomentKind.FirstBlood))
                moments.Add(new BattleMoment { Tick = at, Kind = MomentKind.FirstBlood, Player = IdOf(by.Player), Other = IdOf(t.Player), Def = by.Def, OtherDef = t.Def });
            if (RoleOf(t.Def) == Role.Monarch)
                moments.Add(new BattleMoment { Tick = at, Kind = MomentKind.MonarchSlain, Player = IdOf(t.Player), Other = IdOf(by.Player), Def = t.Def, OtherDef = by.Def });
            if (by.Kills > k.ChampionKills || by.Handle == k.Champion)
            {
                k.Champion = by.Handle;
                k.ChampionDef = by.Def;
                k.ChampionKills = by.Kills;
            }
        }

        void NoteCast(int pos) => KeptOf(pos).Spells++;

        // Once a second, after the outcome is read: who still stands.
        void NoteStanding()
        {
            for (int pos = 0; pos < players.Count; pos++)
            {
                var k = KeptOf(pos);
                if (players[pos].Alive) { k.LastAlive = (int)Tick; continue; }
                if (!k.Stood) continue;
                k.Stood = false;
                k.Fell = (int)Tick;
                moments.Add(new BattleMoment { Tick = (int)Tick, Kind = MomentKind.Fell, Player = IdOf(pos), Def = -1, OtherDef = -1 });
            }
        }

        int[] SampleNow(int pos)
        {
            var k = KeptOf(pos);
            var v = new int[9];
            foreach (var u in units)
            {
                if (u.Player != pos || u.Dying || u.Built < 1f) continue;
                var d = unitDefs[u.Def];
                if (!d.IsBuilding) { v[(int)BattleSeries.Army]++; v[(int)BattleSeries.Worth] += d.ManaCost; }
                else if (RoleOf(u.Def) == Role.Lodge) v[(int)BattleSeries.Lodestones]++;
            }
            v[(int)BattleSeries.Mana] = pos < economy.Count ? Mathf.FloorToInt(economy[pos].Mana) : 0;
            v[(int)BattleSeries.Gathered] = Mathf.FloorToInt(k.Gathered);
            v[(int)BattleSeries.Spent] = Mathf.FloorToInt(k.Spent);
            v[(int)BattleSeries.Built] = k.Built;
            v[(int)BattleSeries.Kills] = k.Kills;
            v[(int)BattleSeries.Losses] = k.Losses;
            return v;
        }

        void NoteSample()
        {
            if (Tick != 1 && Tick % SampleTicks != 0) return;
            for (int pos = 0; pos < players.Count; pos++)
            {
                var k = KeptOf(pos);
                var v = SampleNow(pos);
                for (int s = 0; s < 9; s++) (k.Samples[s] ?? (k.Samples[s] = new List<int>())).Add(v[s]);
            }
        }

        public BattleRecord ReadBattle()
        {
            if (Status == GameStatus.Idle || players.Count == 0) return null;
            var r = new BattleRecord { Tick = (int)Tick, Every = SampleTicks, TicksPerSecond = Tps };
            for (int pos = 0; pos < players.Count; pos++)
            {
                var k = KeptOf(pos);
                byHandle.TryGetValue(k.Champion, out var champ);
                var kr = new KingdomRecord
                {
                    Player = IdOf(pos), UnitsBuilt = k.Built, Kills = k.Kills, Losses = k.Losses, Score = k.Score,
                    LastAliveTick = k.LastAlive, Eliminated = !k.Stood, UnitsTrained = k.Trained, BuildingsRaised = k.Raised,
                    DamageDealt = k.Dealt, DamageTaken = k.Taken, SpellsCast = k.Spells, FellTick = k.Fell,
                    ManaGathered = k.Gathered, ManaSpent = k.Spent, ChampionDef = k.ChampionDef, ChampionKills = k.ChampionKills,
                    ChampionXp = k.ChampionKills * 100, ChampionRank = k.ChampionKills >= 10 ? 2 : k.ChampionKills >= 3 ? 1 : 0,
                    ChampionStanding = champ != null && !champ.Dying,
                };
                var now = SampleNow(pos);
                for (int s = 0; s < 9; s++)
                {
                    var list = new List<int>(k.Samples[s] ?? new List<int>()) { now[s] };
                    kr.Series[s] = list.ToArray();
                }
                var made = new List<KeyValuePair<int, int>>(k.Made);
                made.Sort((a, b) => b.Value.CompareTo(a.Value));
                kr.Made.AddRange(made);
                r.Kingdoms.Add(kr);
            }
            r.Moments.AddRange(moments);
            return r;
        }
    }
}
