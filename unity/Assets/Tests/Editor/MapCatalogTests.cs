// MapCatalogTests.cs - the lobby's map browser finds maps by name, filters
// them by start positions and size, sorts them, and stays quick with
// thousands of maps.
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MapCatalogTests
    {
        // A map of w by h original map units (32 cells each) with n starts.
        static MapInfo Map(string name, int players, int w, int h)
        {
            var starts = new Vector2[players];
            for (int i = 0; i < players; i++) starts[i] = new Vector2(10 + i, 10 + i);
            return new MapInfo { Id = name.ToLowerInvariant(), Name = name, MaxPlayers = players, Size = new Vector2(w * 32, h * 32), Starts = starts };
        }

        static readonly List<MapInfo> Maps = new List<MapInfo>
        {
            Map("Angvir's Maze", 8, 10, 10),
            Map("Athri Cay", 5, 15, 15),
            Map("Lake Lokken", 4, 15, 15),
            Map("Lake Ferrix", 8, 15, 12),
            Map("King of the Hill", 4, 4, 4),
            Map("Two Castles", 2, 12, 12),
            Map("Iapur Narrows", 3, 16, 4),
            Map("Great Steppe", 8, 24, 24),
        };

        static List<string> Names(MapQuery q)
        {
            var c = new MapCatalog();
            c.Use(Maps);
            return c.Query(q).ConvertAll(m => m.Name);
        }

        [Test]
        public void SearchFindsEveryWordInTheNameWhateverTheCase()
        {
            CollectionAssert.AreEquivalent(new[] { "Lake Lokken", "Lake Ferrix" }, Names(new MapQuery { Text = "lake" }));
            CollectionAssert.AreEqual(new[] { "Lake Lokken" }, Names(new MapQuery { Text = "LAKE lok" }));
            CollectionAssert.AreEqual(new[] { "Angvir's Maze" }, Names(new MapQuery { Text = "angvirs" }));
            CollectionAssert.AreEqual(new[] { "Angvir's Maze" }, Names(new MapQuery { Text = "Angvir's" }));
            Assert.AreEqual(0, Names(new MapQuery { Text = "zzz" }).Count);
            Assert.AreEqual(Maps.Count, Names(new MapQuery { Text = "  " }).Count);
        }

        [Test]
        public void TheStartsFilterKeepsMapsWithExactlyThatMany()
        {
            CollectionAssert.AreEquivalent(new[] { "Angvir's Maze", "Lake Ferrix", "Great Steppe" }, Names(new MapQuery { Players = 8 }));
            CollectionAssert.AreEquivalent(new[] { "Two Castles" }, Names(new MapQuery { Players = 2 }));
            // A backend that cannot list the starts still counts its players.
            var c = new MapCatalog();
            c.Use(new List<MapInfo> { new MapInfo { Id = "a", Name = "A", MaxPlayers = 6, Size = new Vector2(320, 320) } });
            Assert.AreEqual(1, c.Query(new MapQuery { Players = 6 }).Count);
        }

        [Test]
        public void TheSizeFilterUsesTheOriginalsMapUnits()
        {
            // By area, so a long thin 16 x 4 is as small as 8 x 8.
            CollectionAssert.AreEquivalent(new[] { "King of the Hill", "Iapur Narrows" }, Names(new MapQuery { Size = MapSize.Small }));
            CollectionAssert.AreEquivalent(new[] { "Angvir's Maze", "Two Castles" }, Names(new MapQuery { Size = MapSize.Medium }));
            CollectionAssert.AreEquivalent(new[] { "Athri Cay", "Lake Lokken", "Lake Ferrix" }, Names(new MapQuery { Size = MapSize.Large }));
            CollectionAssert.AreEquivalent(new[] { "Great Steppe" }, Names(new MapQuery { Size = MapSize.Huge }));
            Assert.AreEqual("15 x 12", MapCatalog.SizeLabel(Maps[3]));
        }

        [Test]
        public void FiltersAndSearchCombine()
        {
            CollectionAssert.AreEqual(new[] { "Lake Ferrix" }, Names(new MapQuery { Text = "lake", Players = 8, Size = MapSize.Large }));
            Assert.AreEqual(0, Names(new MapQuery { Text = "lake", Size = MapSize.Small }).Count);
        }

        [Test]
        public void MapsSortByNamePlayersOrSizeEitherWay()
        {
            var byName = Names(new MapQuery());
            CollectionAssert.AreEqual(new[] { "Angvir's Maze", "Athri Cay", "Great Steppe", "Iapur Narrows", "King of the Hill", "Lake Ferrix", "Lake Lokken", "Two Castles" }, byName);
            byName.Reverse();
            CollectionAssert.AreEqual(byName, Names(new MapQuery { Descending = true }));
            // Ties fall back to the name.
            CollectionAssert.AreEqual(new[] { "Two Castles", "Iapur Narrows", "King of the Hill", "Lake Lokken", "Athri Cay", "Angvir's Maze", "Great Steppe", "Lake Ferrix" },
                Names(new MapQuery { Sort = MapSort.Players }));
            CollectionAssert.AreEqual(new[] { "King of the Hill", "Iapur Narrows", "Angvir's Maze", "Two Castles", "Lake Ferrix", "Athri Cay", "Lake Lokken", "Great Steppe" },
                Names(new MapQuery { Sort = MapSort.Size }));
            Assert.AreEqual("Great Steppe", Names(new MapQuery { Sort = MapSort.Size, Descending = true })[0]);
        }

        [Test]
        public void ThousandsOfMapsStayQuick()
        {
            var many = new List<MapInfo>();
            for (int i = 0; i < 5000; i++) many.Add(Map("Map " + (i * 7919 % 5000) + (i % 3 == 0 ? " lake" : " hill"), 2 + i % 7, 4 + i % 20, 4 + i % 13));
            var c = new MapCatalog();
            var into = new List<MapInfo>();
            var clock = Stopwatch.StartNew();
            c.Use(many);
            for (int k = 0; k < 20; k++)
            {
                c.Use(many);
                c.Query(new MapQuery { Text = k % 2 == 0 ? "lake" : "", Players = k % 4, Size = (MapSize)(k % 5), Sort = (MapSort)(k % 3), Descending = k % 2 == 1 }, into);
            }
            clock.Stop();
            Assert.Less(clock.ElapsedMilliseconds, 1500, "twenty queries over 5000 maps");
            Assert.AreEqual(5000, c.Count);
        }

        [Test]
        public void TheIndexFollowsAGrowingList()
        {
            var list = new List<MapInfo>(Maps);
            var c = new MapCatalog();
            c.Use(list);
            list.Add(Map("New Found Land", 2, 6, 6));
            c.Use(list);
            CollectionAssert.AreEqual(new[] { "New Found Land" }, c.Query(new MapQuery { Text = "found" }).ConvertAll(m => m.Name));
        }
    }
}
