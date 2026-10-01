// DialogLayoutTests.cs - the dialogs' geometry at every tested screen: on
// the canvas inside its margin, plates big enough to hit, nothing over
// anything else, and letters over the floors. And the loading screen's lines.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class DialogLayoutTests
    {
        static readonly Vector2Int[] Screens =
        {
            new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440),
            new Vector2Int(3440, 1440), new Vector2Int(3840, 2160),
        };

        static IEnumerable<DialogLayout> Every() => Screens.Select(s => DialogLayout.ForScreen(s.x, s.y));

        static IEnumerable<DialogBox> Dialogs(DialogLayout l)
        {
            yield return l.Pause(true);
            yield return l.Pause(false);
            yield return l.Leave();
            yield return l.Options();
            yield return l.Result(2);
            yield return l.Result(8);
            yield return l.Folder();
            yield return l.Notice();
        }

        static string At(DialogLayout l) => $"{Mathf.RoundToInt(l.Canvas.x * l.Scale)}x{Mathf.RoundToInt(l.Canvas.y * l.Scale)}";

        static bool Inside(Rect inner, Rect outer, float slack = 0.01f) =>
            inner.xMin >= outer.xMin - slack && inner.yMin >= outer.yMin - slack && inner.xMax <= outer.xMax + slack && inner.yMax <= outer.yMax + slack;

        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f && a.yMin < b.yMax - 0.01f && b.yMin < a.yMax - 0.01f;

        [Test]
        public void TheCanvasScalesBySizeAsTheScreensCanvasDoes()
        {
            foreach (var s in Screens)
            {
                var l = DialogLayout.ForScreen(s.x, s.y);
                Assert.AreEqual(Mathf.Sqrt(s.x / 1920f * (s.y / 1080f)), l.Scale, 1e-5f, $"{s}");
                Assert.AreEqual(s.x, l.Canvas.x * l.Scale, 0.01f);
                Assert.AreEqual(s.y, l.Canvas.y * l.Scale, 0.01f);
            }
            Assert.AreEqual(2f / 3f, DialogLayout.ScaleFor(1280, 720), 1e-5f, "a unit is two thirds of a pixel at 720p");
            Assert.AreEqual(1f, DialogLayout.ScaleFor(1920, 1080), 1e-5f);
            Assert.AreEqual(2f, DialogLayout.ScaleFor(3840, 2160), 1e-5f);
        }

        [Test]
        public void EveryDialogLiesOnTheCanvasInsideItsMargin()
        {
            foreach (var l in Every())
            {
                var room = new Rect(DialogLayout.Margin, DialogLayout.Margin, l.Canvas.x - 2 * DialogLayout.Margin, l.Canvas.y - 2 * DialogLayout.Margin);
                foreach (var d in Dialogs(l))
                {
                    Assert.IsTrue(Inside(d.Frame, room), $"{d.Name} {d.Frame} inside {room} at {At(l)}");
                    Assert.IsTrue(Inside(d.Interlace, d.Frame) && Inside(d.Title, d.Frame) && Inside(d.Help, d.Frame), $"{d.Name}'s band, title and help line inside its frame at {At(l)}");
                    foreach (var k in d.Knots) Assert.IsTrue(Inside(k, d.Frame), $"{d.Name}'s knots at {At(l)}");
                }
                var under = new Rect(0, DialogLayout.ScreenBand + DialogLayout.StripH, l.Canvas.x, l.Canvas.y - 2 * DialogLayout.ScreenBand - DialogLayout.StripH);
                Assert.IsTrue(Inside(l.Folder().Frame, under), $"the folder dialog under the strip at {At(l)}");
                Assert.IsTrue(Inside(l.Notice().Frame, under), $"the notice under the strip at {At(l)}");
            }
        }

        [Test]
        public void TheTitleBandAndTheLettersKeepTheirSizes()
        {
            foreach (var l in Every())
                foreach (var d in Dialogs(l))
                {
                    if (d.Name == "Result")
                    {
                        Assert.AreEqual(DialogLayout.TallTitleBand, d.Title.height, 0.01f);
                        Assert.GreaterOrEqual(d.Title.height, DialogLayout.ResultTitle * 1.25f, "the result's title fits its band");
                    }
                    else Assert.AreEqual(DialogLayout.TitleBand, d.Title.height, 0.01f, $"{d.Name} at {At(l)}");
                    Assert.AreEqual(DialogLayout.Border + DialogLayout.Interlace, d.Title.yMin - d.Frame.yMin, 0.01f, "the title band under the interlace");
                    Assert.GreaterOrEqual(d.Title.height, d.TitleSize * 1.25f - 0.01f, $"{d.Name}'s title fits its band");
                    Assert.GreaterOrEqual(d.Help.height, DialogLayout.Help * 1.3f, $"{d.Name}'s help line fits a line");
                }
        }

        [Test]
        public void PlatesAreBigEnoughToHitAndNothingOverlaps()
        {
            foreach (var l in Every())
                foreach (var d in Dialogs(l))
                {
                    var parts = new List<KeyValuePair<string, Rect>>(d.Plates);
                    foreach (var p in d.Plates)
                    {
                        Assert.GreaterOrEqual(p.Value.height, DialogLayout.PlateMinH, $"{d.Name} {p.Key} at {At(l)}");
                        Assert.GreaterOrEqual(p.Value.width, DialogLayout.PlateMinW, $"{d.Name} {p.Key} at {At(l)}");
                        Assert.GreaterOrEqual(l.Px(p.Value.height), 32f, $"{d.Name} {p.Key} is 32 px tall at {At(l)}");
                        Assert.IsTrue(Inside(p.Value, d.Body), $"{d.Name} {p.Key} inside the body at {At(l)}");
                    }
                    parts.Add(new KeyValuePair<string, Rect>("help", d.Help));
                    parts.Add(new KeyValuePair<string, Rect>("title", d.Title));
                    foreach (var extra in new[] { ("close", d.Close), ("note", d.Note), ("list", d.List), ("head", d.Head), ("field", d.Field), ("status", d.Status) })
                        if (extra.Item2.width > 0) parts.Add(new KeyValuePair<string, Rect>(extra.Item1, extra.Item2));
                    for (int i = 0; i < parts.Count; i++)
                        for (int j = i + 1; j < parts.Count; j++)
                        {
                            // The close cross sits in the title band, at its end.
                            if (parts[i].Key == "title" && parts[j].Key == "close") continue;
                            Assert.IsFalse(Overlap(parts[i].Value, parts[j].Value), $"{d.Name}: {parts[i].Key} {parts[i].Value} and {parts[j].Key} {parts[j].Value} at {At(l)}");
                        }
                    foreach (var p in parts) Assert.IsTrue(Inside(p.Value, d.Frame), $"{d.Name} {p.Key} inside the frame at {At(l)}");
                    for (int i = 0; i < d.Rows.Count; i++)
                    {
                        Assert.IsTrue(Inside(d.Rows[i], d.List), $"{d.Name} row {i} in its table at {At(l)}");
                        if (i > 0) Assert.IsFalse(Overlap(d.Rows[i - 1], d.Rows[i]));
                    }
                }
        }

        [Test]
        public void TheOptionsCloseCrossIsInTheTitleBandAndTheRowsFit()
        {
            foreach (var l in Every())
            {
                var o = l.Options();
                Assert.IsTrue(Inside(o.Close, o.Title) || Inside(o.Close, new Rect(o.Frame.x, o.Title.y, o.Frame.width, o.Title.height)), $"the cross in the band at {At(l)}");
                Assert.IsTrue(Inside(o.List, o.Frame), $"the rows' box inside the frame at {At(l)}");
                Assert.GreaterOrEqual(o.List.height, 4 * DialogLayout.RowH, $"the box shows four rows at least at {At(l)}");
                Assert.LessOrEqual(l.Px(o.Frame.height), l.Canvas.y * l.Scale - 32f, $"no taller than the screen less 32 px at {At(l)}");
                Assert.GreaterOrEqual(DialogLayout.RowH, 52f);
                var label = DialogLayout.RowLabel(o.List.width);
                var picker = DialogLayout.RowPicker(o.List.width);
                var row = new Rect(0, 0, o.List.width, DialogLayout.RowH);
                Assert.IsTrue(Inside(label, row) && Inside(picker, row), "the label and picker in their row");
                Assert.IsFalse(Overlap(label, picker));
                Assert.AreEqual(DialogLayout.PickerW, picker.width);
                var left = DialogLayout.PickerArrow(false);
                var right = DialogLayout.PickerArrow(true);
                var box = new Rect(0, 0, picker.width, picker.height);
                Assert.IsTrue(Inside(left, box) && Inside(right, box) && !Overlap(left, right), "both arrows on the plate");
            }
        }

        [Test]
        public void EveryLetterIsOverTheFloorAtEverySize()
        {
            foreach (var l in Every())
                foreach (var kv in DialogLayout.Letters)
                {
                    float floor = DialogLayout.IsNumber(kv.Key) ? DialogLayout.NumberFloorPx : DialogLayout.FloorPx;
                    Assert.GreaterOrEqual(l.Px(kv.Value), floor, $"{kv.Key} at {kv.Value} units is {l.Px(kv.Value):0.0} px at {At(l)}");
                    Assert.GreaterOrEqual(kv.Value, 18, $"{kv.Key} is 18 units at least, 12 px at 720p");
                }
            foreach (var l in Every())
                foreach (var d in Dialogs(l)) Assert.GreaterOrEqual(l.Px(d.TitleSize), DialogLayout.FloorPx);
        }

        [Test]
        public void TheLoadingScreenFitsAndStacks()
        {
            foreach (var l in Every())
            {
                var g = l.Loading();
                var canvas = new Rect(0, 0, l.Canvas.x, l.Canvas.y);
                var parts = new[] { ("name", g.Name), ("title", g.Title), ("seats", g.Seats), ("bar", g.Bar), ("stage", g.Stage), ("tip", g.Tip) };
                foreach (var p in parts) Assert.IsTrue(Inside(p.Item2, canvas), $"{p.Item1} on the canvas at {At(l)}");
                for (int i = 1; i < parts.Length; i++)
                    Assert.LessOrEqual(parts[i - 1].Item2.yMax, parts[i].Item2.yMin + 0.01f, $"{parts[i - 1].Item1} above {parts[i].Item1} at {At(l)}");
                foreach (var b in g.Bands) Assert.IsTrue(Inside(b, canvas));
                Assert.IsTrue(parts.All(p => !Overlap(p.Item2, g.Bands[0]) && !Overlap(p.Item2, g.Bands[1])), $"clear of the bands at {At(l)}");
                Assert.AreEqual(DialogLayout.BarW, g.Bar.width, 0.01f, "the trough is 1040 units");
                Assert.AreEqual(DialogLayout.BarH, g.Bar.height, 0.01f);
                Assert.IsTrue(Inside(g.Percent, canvas) && !Overlap(g.Percent, g.Bar), $"the percent at the bar's right end at {At(l)}");
                Assert.AreEqual(l.Canvas.x / 2f, g.Bar.center.x, 0.01f, "the bar centred");
            }
        }

        [Test]
        public void TheStageEndsInOneEllipsis()
        {
            Assert.AreEqual("Loading terrain textures...", MenuScreens.Stage("Loading terrain textures..."));
            Assert.AreEqual("Reading units...", MenuScreens.Stage("Reading units"));
            Assert.AreEqual("Reading units...", MenuScreens.Stage("Reading units…"));
            Assert.AreEqual("Reading units...", MenuScreens.Stage("Reading units. "));
            Assert.AreEqual("", MenuScreens.Stage(""));
            Assert.AreEqual("", MenuScreens.Stage(null));
        }

        static readonly SideInfo[] Sides =
        {
            new SideInfo { Id = "ARAMON", Name = "Aramon" }, new SideInfo { Id = "VERUNA", Name = "Veruna" },
            new SideInfo { Id = "TAROS", Name = "Taros" }, new SideInfo { Id = "ZHON", Name = "Zhon" },
        };

        [Test]
        public void TheSeatsLineNamesWhoIsFighting()
        {
            var two = new SkirmishSetup();
            two.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Team = 0 });
            two.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Team = 1, Difficulty = AiDifficulty.Normal });
            two.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Team = 2 });
            Assert.AreEqual("You, Aramon  against  Computer (Normal), Taros", MenuScreens.SeatsLine(two, Sides));

            var four = new SkirmishSetup();
            four.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Team = 0 });
            four.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "ZHON", Team = 1, Difficulty = AiDifficulty.Hard });
            four.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "", Team = 0, Difficulty = AiDifficulty.Easy });
            four.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "VERUNA", Team = 1, Difficulty = AiDifficulty.Brutal });
            var lines = MenuScreens.SeatsLine(four, Sides).Split('\n');
            Assert.AreEqual(2, lines.Length, "one line per team");
            Assert.AreEqual("You, Aramon  and  Computer (Easy), Random", lines[0]);
            Assert.AreEqual("against  Computer (Hard), Zhon  and  Computer (Brutal), Veruna", lines[1]);
        }
    }
}
