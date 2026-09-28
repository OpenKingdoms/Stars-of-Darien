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
                Assert.AreEqual(128f * l.S, l.Sidebar.width * l.S, 0.01f, At(l));
                Assert.LessOrEqual(l.Sidebar.width * l.S, l.ScreenW * 0.25f + 0.01f, "the sidebar takes at most a quarter, " + At(l));
                Assert.AreEqual(49f * l.S, l.Strip.height * l.S, 0.01f, At(l));
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
                foreach (var name in HudLayout.SlotNames)
                {
                    var r = HudLayout.Slot(name);
                    Assert.GreaterOrEqual(r.width * l.S, 28f, $"{name} drawn at {At(l)}");
                    var hit = HudLayout.Hit(r);
                    Assert.GreaterOrEqual(hit.width * l.S, 32f, $"{name} hit width at {At(l)}");
                    Assert.GreaterOrEqual(hit.height * l.S, 32f, $"{name} hit height at {At(l)}");
                }
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
            foreach (float cp in new[] { 9f, 10f, 11f, 12f, 14f })
            {
                Assert.GreaterOrEqual(l.Font(cp, HudLayout.BodyFloor) * l.S, 12f - 0.01f, $"{cp} cp body text");
                Assert.GreaterOrEqual(l.Font(cp, HudLayout.BadgeFloor) * l.S, 10f - 0.01f, $"{cp} cp badge");
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
                    var carpet = l.Carpet(m);
                    if (carpet.width > 0) Assert.IsFalse(carpet.Overlaps(m), "the carpet below the map");
                    Assert.LessOrEqual(carpet.yMax, l.Block.y, "the carpet above the orders");
                }
        }
    }
}
