// MapCatalog.cs - the lobby's map browser as plain rules: search, filters
// for start positions and size, and sorting, over an index built once per
// map list, and the start positions each seat takes. Pure C#, so the rules
// are tested without a scene.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public enum MapSort { Name, Players, Size }

    // Size classes by area in the original's map units of 32 cells, the
    // "15 x 15" its lobby shows: up to 8 x 8, 12 x 12, 16 x 16, and beyond.
    public enum MapSize { Any, Small, Medium, Large, Huge }

    public sealed class MapQuery
    {
        public string Text = "";
        // Start positions, 0 for any.
        public int Players;
        public MapSize Size;
        public MapSort Sort;
        public bool Descending;
    }

    public sealed class MapCatalog
    {
        public const float CellsPerUnit = 32f;

        struct Entry
        {
            public MapInfo Map;
            public string Key;      // the name folded for search
            public int Players;
            public float Area;
            public MapSize Size;
        }

        readonly List<Entry> entries = new List<Entry>();
        IReadOnlyList<MapInfo> source;
        int sourceCount = -1;

        public int Count => entries.Count;

        // The index follows the backend's list, rebuilt when it grows.
        public void Use(IReadOnlyList<MapInfo> maps)
        {
            if (ReferenceEquals(maps, source) && maps.Count == sourceCount) return;
            source = maps;
            sourceCount = maps.Count;
            entries.Clear();
            foreach (var m in maps)
                entries.Add(new Entry { Map = m, Key = Fold(m.Name), Players = PlayersOf(m), Area = AreaOf(m), Size = SizeOf(m) });
        }

        // The maps a query shows, in its order, into a caller's list.
        public List<MapInfo> Query(MapQuery q, List<MapInfo> into = null)
        {
            into = into ?? new List<MapInfo>();
            into.Clear();
            var words = Fold(q.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<Entry>();
            foreach (var e in entries)
            {
                if (q.Players > 0 && e.Players != q.Players) continue;
                if (q.Size != MapSize.Any && e.Size != q.Size) continue;
                bool all = true;
                foreach (var w in words) if (e.Key.IndexOf(w, StringComparison.Ordinal) < 0) { all = false; break; }
                if (all) hits.Add(e);
            }
            Comparison<Entry> byName = (a, b) =>
            {
                int c = string.CompareOrdinal(a.Key, b.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Map.Id, b.Map.Id);
            };
            Comparison<Entry> order;
            switch (q.Sort)
            {
                case MapSort.Players: order = (a, b) => a.Players != b.Players ? a.Players.CompareTo(b.Players) : byName(a, b); break;
                case MapSort.Size: order = (a, b) => a.Area != b.Area ? a.Area.CompareTo(b.Area) : byName(a, b); break;
                default: order = byName; break;
            }
            hits.Sort(q.Descending ? (a, b) => order(b, a) : order);
            foreach (var e in hits) into.Add(e.Map);
            return into;
        }

        // Lower case letters and digits, everything else a single space, so
        // "angvir" finds "Angvir's Maze".
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new System.Text.StringBuilder(s.Length);
            bool space = true;
            foreach (char ch in s)
            {
                if (ch == '\'' || ch == '’') continue;
                if (char.IsLetterOrDigit(ch)) { b.Append(char.ToLowerInvariant(ch)); space = false; }
                else if (!space) { b.Append(' '); space = true; }
            }
            return b.ToString().TrimEnd();
        }

        public static int PlayersOf(MapInfo m) => m.Starts != null && m.Starts.Length > 0 ? m.Starts.Length : m.MaxPlayers;

        public static float AreaOf(MapInfo m) => m.Size.x / CellsPerUnit * (m.Size.y / CellsPerUnit);

        public static MapSize SizeOf(MapInfo m)
        {
            float a = AreaOf(m);
            return a <= 64.5f ? MapSize.Small : a <= 144.5f ? MapSize.Medium : a <= 256.5f ? MapSize.Large : MapSize.Huge;
        }

        // "15 x 15", as the original's lobby writes a map's size.
        public static string SizeLabel(MapInfo m) =>
            $"{Mathf.Max(1, Mathf.RoundToInt(m.Size.x / CellsPerUnit))} x {Mathf.Max(1, Mathf.RoundToInt(m.Size.y / CellsPerUnit))}";

        public static readonly string[] SizeNames = { "Any size", "Small", "Medium", "Large", "Huge" };
    }

    // Who starts where. A seat that took a start keeps it, and the others
    // take what is left: in seat order, or dealt at random by the seed.
    public static class StartPositions
    {
        public static bool Open(SeatSetup s) => s != null && s.Kind != SeatKind.Closed;

        // Each seat's start, -1 for a closed seat or when the map has too
        // few. The same seats, count, rule and seed give the same answer.
        public static int[] Assign(IReadOnlyList<SeatSetup> seats, int count, bool random, uint seed)
        {
            var dealt = new int[seats.Count];
            var taken = new bool[Math.Max(0, count)];
            for (int i = 0; i < seats.Count; i++)
            {
                dealt[i] = -1;
                int want = Open(seats[i]) ? seats[i].Start : -1;
                if (want >= 0 && want < count && !taken[want]) { dealt[i] = want; taken[want] = true; }
            }
            var free = new List<int>();
            for (int p = 0; p < count; p++) if (!taken[p]) free.Add(p);
            if (random)
            {
                var rng = new System.Random(unchecked((int)seed));
                for (int k = free.Count - 1; k > 0; k--)
                {
                    int j = rng.Next(k + 1);
                    (free[k], free[j]) = (free[j], free[k]);
                }
            }
            int next = 0;
            for (int i = 0; i < seats.Count; i++)
                if (Open(seats[i]) && dealt[i] < 0 && next < free.Count) dealt[i] = free[next++];
            return dealt;
        }

        // The seat holding a start, or -1.
        public static int HolderOf(IReadOnlyList<SeatSetup> seats, int position)
        {
            for (int i = 0; i < seats.Count; i++) if (Open(seats[i]) && seats[i].Start == position) return i;
            return -1;
        }

        // A seat takes a free start. False when it is closed, the start is
        // not on the map, or another seat holds it.
        public static bool Claim(IReadOnlyList<SeatSetup> seats, int seat, int position, int count)
        {
            if (seat < 0 || seat >= seats.Count || !Open(seats[seat])) return false;
            if (position < 0 || position >= count) return false;
            int holder = HolderOf(seats, position);
            if (holder >= 0 && holder != seat) return false;
            seats[seat].Start = position;
            return true;
        }

        public static void Release(IReadOnlyList<SeatSetup> seats, int seat)
        {
            if (seat >= 0 && seat < seats.Count) seats[seat].Start = -1;
        }

        // Moves a seat to a start, swapping with the seat that holds it. -1
        // frees the seat's start.
        public static bool Move(IReadOnlyList<SeatSetup> seats, int seat, int position, int count)
        {
            if (seat < 0 || seat >= seats.Count || !Open(seats[seat])) return false;
            if (position < 0) { seats[seat].Start = -1; return true; }
            if (position >= count) return false;
            int holder = HolderOf(seats, position);
            if (holder >= 0 && holder != seat) seats[holder].Start = seats[seat].Start;
            seats[seat].Start = position;
            return true;
        }

        // After the map or the seats change: claims beyond the map's starts,
        // on closed seats or held twice are let go.
        public static void Tidy(IReadOnlyList<SeatSetup> seats, int count)
        {
            var seen = new HashSet<int>();
            foreach (var s in seats)
            {
                if (s == null) continue;
                if (!Open(s) || s.Start >= count || (s.Start >= 0 && !seen.Add(s.Start))) s.Start = -1;
            }
        }
    }
}
