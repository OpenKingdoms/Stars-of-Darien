// HudLayoutTests.cs - the battle HUD's geometry at every tested screen and
// interface scale: the original's proportions, buttons big enough to hit,
// no text over a bar or other text, every order in its original slot, and
// every build option on show without paging.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class HudLayoutTests
    {
        static readonly Vector2Int[] Screens =
        {
            new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440),
            new Vector2Int(3440, 1440), new Vector2Int(3840, 2160),
        };

        static IEnumerable<HudLayout> Every()
        {
            foreach (var s in Screens)
                foreach (int p in HudLayout.ScaleStops)
                    yield return new HudLayout(s.x, s.y, p);
        }

        static string At(HudLayout l) => $"{l.ScreenW}x{l.ScreenH} at {l.Percent}%";

        // The engine's full table, as okx_hud_commands names the widgets.
        static readonly string[] AllIds =
        {
            "MOVE", "ATTACK", "GUARD", "PATROL", "STOP", "HEAL", "LOAD", "UNLOAD", "CLEAR",
            "Cloaked", "Uncloaked", "Active", "Inactive", "Offensive", "Defensive", "Passive",
            "PrimaryWeapon", "SecondaryWeapon", "SpecialWeapon",
        };

        [Test]
        public void TheSidebarAndStripKeepTheOriginalsProportions()
        {
            foreach (var l in Every())
            {
                float byHeight = l.Percent / 100f * l.ScreenH / 480f;
                Assert.AreEqual(Mathf.Max(1f, Mathf.Min(byHeight, l.ScreenW / 4f / 128f)), l.S, 1e-4f, "the scale follows the height, " + At(l));
                Assert.AreEqual(128f, l.Sidebar.width, 0.01f, At(l));
                Assert.AreEqual(l.ScreenW - 128f * l.S, l.Play.width * l.S, 0.5f, "the sidebar is the original's 128, scaled, " + At(l));
                Assert.LessOrEqual(l.Sidebar.width * l.S, l.ScreenW * 0.25f + 0.01f, "the sidebar takes at most a quarter, " + At(l));
                Assert.AreEqual(49f, l.Strip.height, 0.01f, At(l));
                Assert.AreEqual(l.ScreenH - 49f * l.S, l.Play.height * l.S, 0.5f, "the strip is the original's 49, scaled, " + At(l));
                Assert.AreEqual(l.ScreenW, (l.Play.width + l.Sidebar.width) * l.S, 0.5f, At(l));
                Assert.AreEqual(l.ScreenH, (l.Play.height + l.Strip.height) * l.S, 0.5f, At(l));
                if (l.Percent <= 100)
                    Assert.LessOrEqual(l.Covered, HudLayout.OriginalShare + 0.001f, "no more of the screen than the original's, " + At(l));
            }
        }

        [Test]
        public void TheOwnersScreenAtTheDefaultScale()
        {
            var l = new HudLayout(1920, 1080, HudLayout.DefaultScale);
            Assert.AreEqual(1.8f, l.S, 0.001f);
            Assert.AreEqual(88f, l.Strip.height * l.S, 0.5f, "the strip");
            Assert.AreEqual(230f, l.Sidebar.width * l.S, 0.5f, "the sidebar");
            Assert.AreEqual(52f, HudLayout.Slot("O1L").width * l.S, 0.5f, "an order button");
            Assert.AreEqual(0f, l.MapPanel.y, "the minimap at the top right");
            Assert.AreEqual(l.Sidebar.x, l.MapPanel.x);
        }

        [Test]
        public void ButtonsAreBigEnoughToHit()
        {
            foreach (var l in Every())
            {
                Assert.GreaterOrEqual(l.S, 1f, "never under the original's own pixels, " + At(l));
                foreach (var name in HudLayout.SlotNames)
                {
                    var r = HudLayout.Slot(name);
                    Assert.GreaterOrEqual(r.width * l.S, 28f, $"{name} drawn at {At(l)}");
                    var hit = HudLayout.Hit(r);
                    Assert.GreaterOrEqual(hit.width * l.S, 32f, $"{name} hit width at {At(l)}");
                    Assert.GreaterOrEqual(hit.height * l.S, 32f, $"{name} hit height at {At(l)}");
                }
                Assert.GreaterOrEqual(HudLayout.MenuHit.width * l.S, 32f, "the Menu button's width at " + At(l));
                Assert.GreaterOrEqual(HudLayout.MenuHit.height * l.S, 32f, "the Menu button's height at " + At(l));
            }
            var menu = HudLayout.MenuHit;
            Assert.IsTrue(menu.Contains(HudLayout.MenuButton.min) && menu.Contains(HudLayout.MenuButton.max), "the Menu hit covers its lozenge");
            Assert.IsFalse(menu.Overlaps(HudLayout.Clock), "nor the clock");
            foreach (var name in HudLayout.SlotNames)
                Assert.IsFalse(menu.Overlaps(HudLayout.Hit(HudLayout.Slot(name))), "the Menu hit clear of " + name);
            foreach (var k in HudLayout.Knots) Assert.IsFalse(menu.Overlaps(k), "the Menu hit clear of a corner knot");
        }

        [Test]
        public void StripTextNeverRunsIntoABarOrOtherText()
        {
            foreach (var l in Every())
            {
                var texts = l.StripTexts().ToList();
                var bars = l.StripTroughs().ToList();
                for (int i = 0; i < texts.Count; i++)
                {
                    Assert.LessOrEqual(texts[i].rect.xMax, l.StripW + 0.01f, texts[i].name + " inside the strip, " + At(l));
                    Assert.LessOrEqual(texts[i].rect.yMax, HudLayout.StripH, texts[i].name);
                    for (int j = i + 1; j < texts.Count; j++)
                        Assert.IsFalse(texts[i].rect.Overlaps(texts[j].rect), $"{texts[i].name} and {texts[j].name} at {At(l)}");
                    foreach (var b in bars)
                        Assert.IsFalse(texts[i].rect.Overlaps(b.rect), $"{texts[i].name} over the {b.name} bar at {At(l)}");
                }
                Assert.IsFalse(HudLayout.Portrait.Overlaps(HudLayout.Name));
            }
        }

        [Test]
        public void SidebarSlotsFitTheOrderBlockWithoutOverlapping()
        {
            var block = new Rect(0, 0, HudLayout.SidebarW, HudLayout.BlockH);
            var fixedParts = new[] { HudLayout.MenuButton, HudLayout.Clock, HudLayout.Help, HudLayout.Income, HudLayout.Ball, HudLayout.Spend };
            foreach (var r in fixedParts.Concat(HudLayout.SlotNames.Select(HudLayout.Slot)))
            {
                Assert.IsTrue(r.xMin >= HudLayout.BandW && r.xMax <= block.xMax && r.yMin >= 0 && r.yMax <= block.yMax, $"{r} inside the block, right of the band");
                foreach (var k in HudLayout.Knots) Assert.IsFalse(k.Overlaps(r), $"a corner knot over {r}");
            }
            for (int i = 0; i < fixedParts.Length; i++)
                for (int j = i + 1; j < fixedParts.Length; j++)
                    Assert.IsFalse(fixedParts[i].Overlaps(fixedParts[j]), $"{fixedParts[i]} and {fixedParts[j]}");
            // What one selection can show never overlaps, hit rects included.
            var placed = HudLayout.PlaceActions(AllIds);
            var rects = placed.Values.Select(HudLayout.Slot).ToList();
            for (int i = 0; i < rects.Count; i++)
            {
                foreach (var f in fixedParts) Assert.IsFalse(HudLayout.Hit(rects[i]).Overlaps(f), $"{rects[i]} over {f}");
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(HudLayout.Hit(rects[i]).Overlaps(HudLayout.Hit(rects[j])), $"{rects[i]} and {rects[j]}");
            }
        }

        [Test]
        public void EveryActionSitsInTheOriginalsSlot()
        {
            var knight = HudLayout.PlaceActions(new[] { "MOVE", "ATTACK", "GUARD", "PATROL", "STOP", "Offensive", "Defensive", "Passive" });
            Assert.AreEqual("O1L", knight["MOVE"]);
            Assert.AreEqual("O1R", knight["PATROL"]);
            Assert.AreEqual("O2L", knight["ATTACK"]);
            Assert.AreEqual("O2R", knight["GUARD"]);
            Assert.AreEqual("O4C", knight["STOP"]);
            Assert.AreEqual("S1", knight["Offensive"]);
            Assert.AreEqual("S2", knight["Defensive"]);
            Assert.AreEqual("S3", knight["Passive"]);
            var mage = HudLayout.PlaceActions(new[] { "MOVE", "HEAL", "CLEAR", "PrimaryWeapon", "SecondaryWeapon", "SpecialWeapon", "Cloaked", "Uncloaked" });
            Assert.AreEqual("O3L", mage["HEAL"]);
            Assert.AreEqual("O3R", mage["CLEAR"]);
            Assert.AreEqual("W1", mage["PrimaryWeapon"]);
            Assert.AreEqual("W2", mage["SecondaryWeapon"]);
            Assert.AreEqual("W3", mage["SpecialWeapon"]);
            Assert.AreEqual("O4R", mage["Cloaked"], "with spells the cloak pair moves beside STOP");
            Assert.AreEqual("O4L", mage["Uncloaked"]);
            var gate = HudLayout.PlaceActions(new[] { "Active", "Inactive" });
            Assert.AreEqual("W1c", gate["Active"], "a gate's pair takes the weapon row");
            Assert.AreEqual("W2c", gate["Inactive"]);
            // HEAL and LOAD share a slot, and the second overflows.
            var both = HudLayout.PlaceActions(new[] { "HEAL", "LOAD", "CLEAR", "UNLOAD" });
            Assert.AreEqual("O3L", both["HEAL"]);
            Assert.AreEqual("C1", both["LOAD"]);
            Assert.AreEqual("O3R", both["CLEAR"]);
            Assert.AreEqual("C2", both["UNLOAD"]);
            // A caster that heals, carries, clears and cloaks still fits.
            var busy = AllIds.Where(id => id != "Active" && id != "Inactive").ToArray();
            Assert.AreEqual(busy.Length, HudLayout.PlaceActions(busy).Count, "every button finds room");
            var reversed = HudLayout.PlaceActions(busy.Reverse().ToArray());
            Assert.AreEqual("W1", reversed["PrimaryWeapon"], "the order the engine lists them in does not move a spell");
        }

        [Test]
        public void UpToNineBuildOptionsMakeOneRowOnWideScreens()
        {
            foreach (var l in Every())
            {
                if (l.Percent > 115) continue;
                for (int n = 1; n <= 9; n++)
                {
                    var g = l.Builds(n);
                    Assert.AreEqual(1, g.Rows, $"{n} options at {At(l)}");
                    Assert.IsFalse(g.Paged);
                    Assert.AreEqual(64f, g.CellW);
                }
            }
        }

        [Test]
        public void NothingPagesUpToSixtyFourOptions()
        {
            foreach (int p in HudLayout.ScaleStops)
            {
                var l = new HudLayout(1280, 720, p);
                for (int n = 1; n <= 64; n++)
                {
                    var g = l.Builds(n);
                    Assert.IsFalse(g.Paged, $"{n} options at {At(l)}");
                    Assert.GreaterOrEqual(g.CellW * l.S, 48f, "cells stay big enough to read");
                    var cells = Enumerable.Range(0, n).Select(i => l.BuildCell(g, i)).ToList();
                    foreach (var c in cells)
                    {
                        Assert.IsTrue(c.xMin >= 0 && c.xMax <= l.Play.xMax + 0.01f && c.yMin >= -0.01f && c.yMax <= l.Play.yMax + 0.01f, $"{c} in the play area, {n} at {At(l)}");
                        if (l.MapHangs) Assert.IsFalse(c.Overlaps(l.MapPanel), "clear of the minimap");
                    }
                    Assert.AreEqual(l.Play.yMax, cells[0].yMax, 0.01f, "the first row sits on the strip");
                }
            }
        }

        [Test]
        public void TextIsReadableOnTheSmallestScreenAndScale()
        {
            var l = new HudLayout(1280, 720, HudLayout.ScaleStops[0]);
            foreach (float cp in new[] { 6f, 8f, 9f, 10f, 11f, 12f, 14f })
            {
                Assert.GreaterOrEqual(l.Font(cp, HudLayout.BodyFloor) * l.S, 12f - 0.01f, $"{cp} cp body text");
                Assert.GreaterOrEqual(l.Font(cp, HudLayout.BadgeFloor) * l.S, 12f - 0.01f, $"{cp} cp key letter or cost");
                Assert.GreaterOrEqual(l.Font(cp, HudLayout.NumberFloor) * l.S, 14f - 0.01f, $"{cp} cp number");
            }
            var big = new HudLayout(3840, 2160, 100);
            Assert.AreEqual(14, big.Font(14f, HudLayout.BodyFloor), "sizes stay as drawn where the screen is big");
        }

        [Test]
        public void TheWorldCameraSeesOnlyThePlayArea()
        {
            foreach (var l in Every())
            {
                var v = l.Viewport;
                Assert.AreEqual(0f, v.x);
                Assert.AreEqual(l.Strip.height / l.H, v.y, 1e-4f);
                Assert.AreEqual(1f, v.yMax, 1e-4f, At(l));
                Assert.AreEqual(l.Play.width / l.W, v.width, 1e-4f);
            }
        }

        [Test]
        public void TheMinimapFitsItsPanelByShape()
        {
            foreach (var l in Every())
                foreach (float aspect in new[] { 0.5f, 1f, 2f })
                {
                    var m = l.MapRect(aspect);
                    Assert.AreEqual(aspect, m.width / m.height, 0.01f);
                    Assert.IsTrue(m.xMin >= l.MapPanel.x + HudLayout.BandW && m.xMax <= l.MapPanel.xMax && m.yMax <= l.MapPanel.yMax, $"{m} in {l.MapPanel} at {At(l)}");
                    var filler = l.Filler(m);
                    if (filler.width > 0) Assert.IsFalse(filler.Overlaps(m), "the filler below the map");
                    Assert.LessOrEqual(filler.yMax, l.Block.y, "the filler above the orders");
                }
        }

        static bool Inside(Rect a, Rect b) =>
            a.xMin >= b.xMin - 1e-3f && a.yMin >= b.yMin - 1e-3f && a.xMax <= b.xMax + 1e-3f && a.yMax <= b.yMax + 1e-3f;

        static float Shared(Rect a, Rect b) =>
            Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) * Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));

        [Test]
        public void AKeyLetterSitsInItsButtonsCornerOffThePicture()
        {
            foreach (var l in Every())
            {
                Assert.GreaterOrEqual(l.BadgeEm * l.S, HudLayout.BadgeFloor - 0.01f, "the letter reads at " + At(l));
                foreach (var name in HudLayout.SlotNames)
                {
                    var pic = HudLayout.Slot(name);
                    var tab = l.Badge(name);
                    Assert.IsTrue(Inside(tab, HudLayout.Hit(pic)), $"{name}'s key {tab} inside its hit rect at {At(l)}");
                    Assert.LessOrEqual(Shared(tab, pic), 0.1f * pic.width * pic.height + 1e-3f, $"{name}'s key covers a tenth of its picture at most, {At(l)}");
                    Assert.GreaterOrEqual(tab.width, l.BadgeEm, $"{name}'s tab holds a letter at {At(l)}");
                    Assert.LessOrEqual(tab.width * l.S, HudLayout.Slot(name).width * l.S, "no wider than its button");
                }
            }
            var owner = new HudLayout(1920, 1080, HudLayout.DefaultScale).Badge("O2L");
            Assert.LessOrEqual(owner.width, 11f, "at most 11 cp wide on the owner's screen");
            Assert.LessOrEqual(owner.height, 9f, "and 9 tall");
        }

        [Test]
        public void TheKeyLetterIsOneGlyphAndTheHelpBoxSpellsItOut()
        {
            Assert.AreEqual("M", BattleHud.Badge("m"));
            Assert.AreEqual("P", BattleHud.Badge("P"));
            Assert.AreEqual("^A", BattleHud.Badge("a"), "A pans the camera, so attack is Ctrl A, a caret for Ctrl");
            Assert.AreEqual("^S", BattleHud.Badge("S"));
            Assert.AreEqual("Ctrl A", BattleHud.KeyName("a"));
            Assert.AreEqual("M", BattleHud.KeyName("m"));
        }

        [Test]
        public void TheRosterTakesTheSlotUnderTheMapWhereItIsTallEnough()
        {
            int shown = 0;
            foreach (var l in Every())
                foreach (float aspect in new[] { 0.5f, 1f, 1.25f, 2f })
                {
                    var filler = l.Filler(l.MapRect(aspect));
                    var r = HudLayout.Roster(filler);
                    if (filler.height < HudLayout.RosterMinH)
                    {
                        Assert.AreEqual(Rect.zero, r, $"no roster in a {filler.height} cp slot at {At(l)}");
                        continue;
                    }
                    shown++;
                    Assert.IsTrue(Inside(r, filler), $"{r} inside {filler} at {At(l)}");
                    int rows = HudLayout.RosterRows(r);
                    Assert.That(rows, Is.InRange(1, HudLayout.RosterMaxRows), At(l));
                    for (int i = 0; i < rows; i++)
                    {
                        var row = HudLayout.RosterRow(r, i);
                        Assert.AreEqual(16f, row.height, 1e-4f);
                        Assert.IsTrue(Inside(row, r), $"row {i} inside the roster at {At(l)}");
                    }
                    if (rows < HudLayout.RosterMaxRows) Assert.Greater(HudLayout.RosterRow(r, rows).yMax, r.yMax, "every row that fits is used");
                }
            Assert.Greater(shown, 0, "some screens have a roster");
            var owner = new HudLayout(1920, 1080, HudLayout.DefaultScale);
            var room = HudLayout.Roster(owner.Filler(owner.MapRect(1.25f)));
            Assert.AreEqual(HudLayout.RosterMaxRows, HudLayout.RosterRows(room), "six rows on the owner's screen");
        }

        [Test]
        public void TheWordKillsSitsUnderTheCountWithoutCrowdingTheStrip()
        {
            foreach (var l in Every())
            {
                var label = l.KillsLabel;
                if (label.width <= 0) { Assert.AreEqual(HudLayout.Kills, l.KillsCount, "the count alone where the word has no room, " + At(l)); continue; }
                Assert.IsTrue(Inside(label, HudLayout.Kills), "the word inside Kills at " + At(l));
                Assert.IsTrue(Inside(l.KillsCount, HudLayout.Kills), "the count inside Kills at " + At(l));
                Assert.IsFalse(label.Overlaps(l.KillsCount), "the word under the count at " + At(l));
                foreach (var t in l.StripTexts())
                    if (t.name != "Kills") Assert.IsFalse(label.Overlaps(t.rect), $"the word over {t.name} at {At(l)}");
                foreach (var b in l.StripTroughs()) Assert.IsFalse(label.Overlaps(b.rect), $"the word over the {b.name} bar at {At(l)}");
            }
            Assert.Greater(new HudLayout(1920, 1080, HudLayout.DefaultScale).KillsLabel.width, 0f, "the owner's screen has room for it");
        }

        [Test]
        public void MinimapDotsAreBigEnoughToSee()
        {
            var owner = new HudLayout(1920, 1080, HudLayout.DefaultScale);
            Assert.GreaterOrEqual(owner.DotUnit, 3, "a unit's dot in screen pixels at 1080p");
            Assert.GreaterOrEqual(owner.DotBuilding, 4, "a building's");
            foreach (var l in Every()) Assert.Greater(l.DotBuilding, l.DotUnit, "buildings stand out from units at " + At(l));
        }

        [Test]
        public void TheHelpBoxAndLabelsSayWhatACardOrUnitIs()
        {
            Assert.AreEqual(("Knight, 120 mana", "Shift 5, Ctrl repeat, right click removes"), BattleHud.CardLines("Knight", 120, true, false, true));
            Assert.AreEqual("Click to place, R turns it", BattleHud.CardLines("Lodge", 400, false, true, true).Item2);
            Assert.AreEqual("Click to place", BattleHud.CardLines("Lodestone", 400, false, false, true).Item2);
            Assert.AreEqual("Click to place, Ctrl repeats it", BattleHud.CardLines("Goblin", 173, false, false, true, true).Item2);
            Assert.AreEqual("Not enough mana", BattleHud.CardLines("Lodge", 400, false, true, false).Item2);
            Assert.AreEqual("Not enough mana", BattleHud.CardLines("Knight", 120, true, false, false).Item2);
            Assert.AreEqual("–", BattleHud.SpellCost(0), "a dash under a spell that costs nothing");
            Assert.AreEqual("200", BattleHud.SpellCost(200));
            Assert.AreEqual("", BattleHud.RankWord(0));
            Assert.AreEqual("Veteran", BattleHud.RankWord(1));
            Assert.AreEqual("Champion", BattleHud.RankWord(2));
            Assert.AreEqual("Veteran, 12 kills", BattleHud.Record(1, 12));
            Assert.AreEqual("1 kill", BattleHud.Record(0, 1));
            Assert.AreEqual("Champion", BattleHud.Record(2, 0));
            Assert.AreEqual("", BattleHud.Record(0, 0));
        }

        static readonly OpenKingdomsUnity.Game.SideInfo[] Sides =
        {
            new OpenKingdomsUnity.Game.SideInfo { Id = "ARAMON", Name = "Aramon" },
            new OpenKingdomsUnity.Game.SideInfo { Id = "ZHON", Name = "Zhon" },
        };

        static string Described(string description, string category) =>
            BattleHud.Describe(new OpenKingdomsUnity.Game.UnitDef { Description = description, Category = category, Side = "ARAMON" }, Sides);

        [Test]
        public void AUnitIsDescribedByItsCategoryWhereTheGameOnlyNamesItsKingdom()
        {
            Assert.AreEqual("Monarch of Aramon", Described("Aramon", "ARA Monarch"));
            Assert.AreEqual("Factory", Described("Aramon", "ARA FACTORY"));
            Assert.AreEqual("Melee attack", Described("Aramon", "ARA MELEE ATTACK"));
            Assert.AreEqual("Builder", Described("ZHON", "ZON BUILDER"), "a side's id names it too");
            Assert.AreEqual("", Described("Aramon", "ARA"), "nothing left, so no line");
            Assert.AreEqual("Melee attack", Described("NPC", "ARA MELEE ATTACK"));
            Assert.AreEqual("Factory", Described("", "ARA FACTORY"));
            Assert.AreEqual("A knight of the western realm", Described("A knight of the western realm", "ARA MELEE ATTACK"), "prose from the game wins");
        }

        [Test]
        public void AKeyLetterIsTallerThanItsCapitalAndRoomyAt720p()
        {
            foreach (var l in Every())
                foreach (var name in HudLayout.SlotNames)
                    Assert.GreaterOrEqual(l.Badge(name).height, 0.65f * l.BadgeEm + 0.2f, $"{name}'s tab holds a capital at {At(l)}");
            var small = new HudLayout(1280, 720, HudLayout.DefaultScale);
            var tab = small.Badge("O1L");
            Assert.AreEqual(1.2f * small.BadgeEm + 0.5f, tab.width, 1e-3f, "an order's tab at 720p");
            Assert.AreEqual(0.8f * small.BadgeEm + 0.5f, tab.height, 1e-3f);
        }
    }
}
