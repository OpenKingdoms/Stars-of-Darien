// BattleRecord.cs - what the end of a battle shows: the original's tallies
// for every kingdom, as its own end screen printed them, and what the
// engine keeps beside them (samples over time, kinds made, damage, spells,
// a champion and the key moments). The honours and the graphs' scales are
// worked out here, apart from any screen, so they can be tested alone.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    // What each sample holds, in the engine's order.
    public enum BattleSeries { Army, Worth, Mana, Gathered, Spent, Built, Kills, Losses, Lodestones }

    public enum MomentKind { FirstBlood = 1, MonarchSlain = 2, Fell = 3, Yielded = 4 }

    public struct BattleMoment
    {
        public int Tick;
        public MomentKind Kind;
        // Whose moment it is, and the kingdom on the other side of it or 0.
        public int Player, Other;
        // Their units in it, -1 for none.
        public int Def, OtherDef;
    }

    public sealed class KingdomRecord
    {
        public int Player;
        // The original's tallies.
        public int UnitsBuilt, Kills, Losses, Score, LastAliveTick;
        public bool Eliminated;
        // What the engine keeps beside them.
        public int UnitsTrained, BuildingsRaised, DamageDealt, DamageTaken, SpellsCast;
        // When it had nothing left, 0 while it stands.
        public int FellTick;
        public float ManaGathered, ManaSpent;
        // The unit with the most kills: its kind (-1 for none), kills,
        // experience, rank 0 to 2 as the HUD shows it, and whether it lives.
        public int ChampionDef = -1, ChampionKills, ChampionXp, ChampionRank;
        public bool ChampionStanding;
        // The kinds it finished, most first.
        public readonly List<KeyValuePair<int, int>> Made = new List<KeyValuePair<int, int>>();
        // A series by BattleSeries: a sample every BattleRecord.Every ticks,
        // and last the value at BattleRecord.Tick.
        public readonly int[][] Series = new int[9][];

        public bool Standing => FellTick == 0;

        public int[] Of(BattleSeries s) => Series[(int)s] ?? new int[0];

        // The most a series reached.
        public int Peak(BattleSeries s)
        {
            int best = 0;
            foreach (int v in Of(s)) best = Mathf.Max(best, v);
            return best;
        }
    }

    public sealed class BattleRecord
    {
        // The tick it was read at, the ticks between samples and a second's ticks.
        public int Tick, Every = 300, TicksPerSecond = 60;
        public readonly List<KingdomRecord> Kingdoms = new List<KingdomRecord>();
        public readonly List<BattleMoment> Moments = new List<BattleMoment>();

        public KingdomRecord Of(int player)
        {
            foreach (var k in Kingdoms) if (k.Player == player) return k;
            return null;
        }

        // The tick of sample i of n, the last of which is now.
        public int SampleTick(int i, int n) => i >= n - 1 ? Tick : i * Every;

        // Mana gathered a second, over the gap before each sample.
        public float[] Income(KingdomRecord k)
        {
            var g = k.Of(BattleSeries.Gathered);
            var r = new float[g.Length];
            for (int i = 1; i < g.Length; i++)
            {
                float secs = (SampleTick(i, g.Length) - SampleTick(i - 1, g.Length)) / (float)Mathf.Max(1, TicksPerSecond);
                r[i] = secs > 0.01f ? Mathf.Max(0f, (g[i] - g[i - 1]) / secs) : r[i - 1];
            }
            if (r.Length > 1) r[0] = r[1];
            return r;
        }

        // hh:mm:ss, the original's Time.
        public static string Clock(int ticks, int tps)
        {
            int s = Mathf.Max(0, ticks) / Mathf.Max(1, tps);
            return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
        }

        // m:ss, or h:mm:ss past an hour, for the annals and the graph.
        public static string Short(int ticks, int tps)
        {
            int s = Mathf.Max(0, ticks) / Mathf.Max(1, tps);
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }

    // An honour earned: its name, the kingdom and why.
    public struct Honour
    {
        public string Title, Reason;
        public int Player;
    }

    public static class Honours
    {
        // Warlord, Master builder, Treasurer, Unbroken, First blood and
        // Champion, each to the one kingdom that earned it. A tie goes to
        // nobody, and an honour nobody earned is left out.
        public static List<Honour> Award(BattleRecord r)
        {
            var list = new List<Honour>();
            void Most(string title, System.Func<KingdomRecord, float> by, System.Func<KingdomRecord, string> why, System.Func<KingdomRecord, bool> may = null)
            {
                KingdomRecord best = null;
                bool tie = false;
                foreach (var k in r.Kingdoms)
                {
                    if (may != null && !may(k)) continue;
                    float v = by(k);
                    if (v <= 0f) continue;
                    if (best == null || v > by(best)) { best = k; tie = false; }
                    else if (Mathf.Approximately(v, by(best))) tie = true;
                }
                if (best != null && !tie) list.Add(new Honour { Title = title, Player = best.Player, Reason = why(best) });
            }
            Most("Warlord", k => k.Kills, k => Count(k.Kills, "kill"));
            Most("Master builder", k => k.UnitsBuilt, k => Count(k.UnitsBuilt, "unit") + " built");
            Most("Treasurer", k => k.ManaGathered, k => Mathf.RoundToInt(k.ManaGathered).ToString("N0") + " mana gathered");
            // The fewest losses among the kingdoms still standing that fought.
            KingdomRecord least = null;
            bool even = false;
            foreach (var k in r.Kingdoms)
            {
                if (!k.Standing || k.Kills + k.Losses == 0) continue;
                if (least == null || k.Losses < least.Losses) { least = k; even = false; }
                else if (k.Losses == least.Losses) even = true;
            }
            if (least != null && !even && r.Kingdoms.Count > 1)
                list.Add(new Honour { Title = "Unbroken", Player = least.Player, Reason = least.Losses == 0 ? "lost nothing" : "lost only " + least.Losses });
            foreach (var m in r.Moments)
                if (m.Kind == MomentKind.FirstBlood)
                {
                    list.Add(new Honour { Title = "First blood", Player = m.Player, Reason = "drew it at " + BattleRecord.Short(m.Tick, r.TicksPerSecond) });
                    break;
                }
            Most("Champion", k => k.ChampionDef >= 0 ? k.ChampionKills : 0, k => "a unit with " + Count(k.ChampionKills, "kill"));
            return list;
        }

        public static string Count(int n, string word) => n == 1 ? "1 " + word : n.ToString("N0") + " " + word + "s";
    }

    // Axis steps a reader takes in at a glance: 1, 2 or 5 times a power of ten.
    public static class GraphScale
    {
        public static float Step(float max, int lines = 4)
        {
            if (max <= 0f) return 1f;
            float raw = max / Mathf.Max(1, lines);
            float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            foreach (float m in new[] { 1f, 2f, 5f, 10f })
                if (m * pow >= raw) return Mathf.Max(1f, m * pow);
            return Mathf.Max(1f, 10f * pow);
        }

        // The top of the axis: the first step at or over the most.
        public static float Top(float max, int lines = 4)
        {
            float step = Step(max, lines);
            return Mathf.Max(step, Mathf.Ceil(max / step) * step);
        }

        // A tick label: 1,200, 12k past ten thousand, 1.5M past a million.
        public static string Label(float v) =>
            v >= 1000000f ? (v / 1000000f).ToString("0.#") + "M" : v >= 10000f ? (v / 1000f).ToString("0.#") + "k" : Mathf.RoundToInt(v).ToString("N0");
    }
}
