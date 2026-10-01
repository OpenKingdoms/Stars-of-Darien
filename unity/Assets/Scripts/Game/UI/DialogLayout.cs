// DialogLayout.cs - where every part of the dialogs goes, in canvas units
// from the canvas's top left, y down. A unit is sqrt(w/1920 x h/1080) px,
// so 0.667 at 720p and 2 at 4K. No Unity objects, so tests check every size.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class DialogLayout
    {
        public const float RefW = 1920f, RefH = 1080f;
        // The grid: margins from the screen's edge, the frame, the band of
        // interlace along a dialog's top, its knots and the purple title band.
        public const float Margin = 24f, Inset = 32f, Border = 8f, Interlace = 10f, Knot = 18f;
        public const float TitleBand = 56f, TallTitleBand = 96f, HelpH = 30f;
        public const float PlateH = 56f, PlateMinH = 48f, PlateMinW = 240f, PlateW = 240f, Gap = 16f;
        public const float RowH = 52f, RubricH = 44f, PickerW = 300f, PickerH = 48f, ArrowW = 40f;
        public const float TableRowH = 36f, TableHeadH = 28f, ListRowH = 44f;
        // The top of the folder and notice screens: a band, then a purple
        // strip with the game's name. And the loading screen's bar.
        public const float ScreenBand = 12f, StripH = 104f, BarW = 1040f, BarH = 28f;
        // Text floors in screen pixels, as the HUD's.
        public const float FloorPx = HudLayout.BodyFloor, NumberFloorPx = HudLayout.NumberFloor;

        // Letter sizes in canvas units.
        public const int ScreenTitle = 72, DialogTitle = 44, ResultTitle = 72, LoadingTitle = 80, Rubric = 26;
        public const int Body = 24, Sentence = 26, PlateLabel = 26, PickerValue = 24, Help = 22, Note = 22;
        public const int Heading = 20, Small = 20, Numbers = 24, Row = 22;
        // The smallest letter, 12 px at 720p, to which a long help line shrinks.
        public const int Floor = 18;

        // Every size the dialogs set, and whether it is a number, for the floors.
        public static readonly KeyValuePair<string, int>[] Letters =
        {
            new KeyValuePair<string, int>("screen title", ScreenTitle), new KeyValuePair<string, int>("dialog title", DialogTitle),
            new KeyValuePair<string, int>("result title", ResultTitle), new KeyValuePair<string, int>("loading title", LoadingTitle),
            new KeyValuePair<string, int>("rubric", Rubric), new KeyValuePair<string, int>("body", Body),
            new KeyValuePair<string, int>("sentence", Sentence), new KeyValuePair<string, int>("plate", PlateLabel),
            new KeyValuePair<string, int>("picker number", PickerValue), new KeyValuePair<string, int>("help", Help),
            new KeyValuePair<string, int>("note", Note), new KeyValuePair<string, int>("heading", Heading),
            new KeyValuePair<string, int>("small", Small), new KeyValuePair<string, int>("number", Numbers),
            new KeyValuePair<string, int>("row", Row),
            new KeyValuePair<string, int>("help at its smallest", Floor),
        };

        public static bool IsNumber(string role) => role.Contains("number");

        public readonly Vector2 Canvas;
        public readonly float Scale;

        public DialogLayout(Vector2 canvas, float scale)
        {
            Canvas = new Vector2(Mathf.Max(1f, canvas.x), Mathf.Max(1f, canvas.y));
            Scale = Mathf.Max(0.01f, scale);
        }

        public static float ScaleFor(int w, int h) => Mathf.Sqrt(Mathf.Max(1, w) / RefW * (Mathf.Max(1, h) / RefH));

        public static DialogLayout ForScreen(int w, int h)
        {
            float s = ScaleFor(w, h);
            return new DialogLayout(new Vector2(Mathf.Max(1, w), Mathf.Max(1, h)) / s, s);
        }

        public float Px(float units) => units * Scale;

        // A dialog of w by h centred in the canvas, or in the room under the
        // folder screen's strip, with its band, title and help line.
        DialogBox Make(string name, float w, float h, float band, bool underStrip = false)
        {
            float top = underStrip ? ScreenBand + StripH : 0f, bottom = underStrip ? ScreenBand : 0f;
            float y = top + (Canvas.y - top - bottom - h) / 2f;
            var b = new DialogBox { Name = name, TitleSize = band > TitleBand ? ResultTitle : DialogTitle };
            b.Frame = new Rect((Canvas.x - w) / 2f, y, w, h);
            b.Interlace = b.At(Border, Border, w - 2 * Border, Interlace);
            b.Title = b.At(Border, Border + Interlace, w - 2 * Border, band);
            b.Knots = new[] { b.At(0, 0, Knot, Knot), b.At(w - Knot, 0, Knot, Knot) };
            b.Help = b.At(Border + Gap, h - Border - 12f - HelpH, w - 2 * (Border + Gap), HelpH);
            b.Body = b.At(Border, Border + Interlace + band, w - 2 * Border, h - 2 * Border - Interlace - band);
            return b;
        }

        static float Top(float band) => Border + Interlace + band;

        // Resume, Save game, Options and Quit to menu, or the three without
        // Save game, with the save note under them.
        public DialogBox Pause(bool save)
        {
            int n = save ? 4 : 3;
            const float w = 560f, plateW = 400f;
            float y = Top(TitleBand) + 20f;
            float h = y + n * (PlateH + Gap) - Gap + 8f + 30f + 6f + HelpH + 12f + Border;
            var b = Make("Pause", w, h, TitleBand);
            foreach (var label in save ? new[] { "Resume", "Save game", "Options", "Quit to menu" } : new[] { "Resume", "Options", "Quit to menu" })
            {
                b.Plates.Add(new KeyValuePair<string, Rect>(label, b.At((w - plateW) / 2f, y, plateW, PlateH)));
                y += PlateH + Gap;
            }
            b.Note = b.At(Border + Gap, y - Gap + 8f, w - 2 * (Border + Gap), 30f);
            return b;
        }

        // Leave the battle? with Leave and Stay side by side.
        public DialogBox Leave()
        {
            const float w = 560f;
            float y = Top(TitleBand) + 20f;
            float h = y + 64f + 20f + PlateH + Gap + HelpH + 12f + Border;
            var b = Make("Leave", w, h, TitleBand);
            b.Note = b.At(Border + Gap, y, w - 2 * (Border + Gap), 64f);
            float x = (w - 2 * PlateW - Gap) / 2f, py = y + 64f + 20f;
            b.Plates.Add(new KeyValuePair<string, Rect>("Leave", b.At(x, py, PlateW, PlateH)));
            b.Plates.Add(new KeyValuePair<string, Rect>("Stay", b.At(x + PlateW + Gap, py, PlateW, PlateH)));
            return b;
        }

        // As tall as the screen allows: the rows scroll, and Defaults, Back
        // and the close cross stay in view.
        public DialogBox Options()
        {
            const float w = 920f;
            float h = Mathf.Min(1020f, Canvas.y - 2 * Margin);
            var b = Make("Options", w, h, TitleBand);
            float top = Top(TitleBand);
            b.Close = b.At(w - Border - 8f - 40f, top - TitleBand + 8f, 40f, 40f);
            b.Title = b.At(Border + 56f, Border + Interlace, w - 2 * (Border + 56f), TitleBand);
            float plateY = h - Border - 12f - HelpH - 12f - PlateH;
            b.Plates.Add(new KeyValuePair<string, Rect>("Defaults", b.At(Inset, plateY, PlateW, PlateH)));
            b.Plates.Add(new KeyValuePair<string, Rect>("Back", b.At(w - Inset - PlateW, plateY, PlateW, PlateH)));
            b.List = b.At(Inset, top + Gap, w - 2 * Inset, plateY - Gap - top - Gap);
            return b;
        }

        // An option's row, relative to its own top left: the label, and the
        // picker with its two arrows at the right.
        public static Rect RowLabel(float rowW) => new Rect(12f, 0, rowW - PickerW - 12f - Gap, RowH);
        public static Rect RowPicker(float rowW) => new Rect(rowW - PickerW, (RowH - PickerH) / 2f, PickerW, PickerH);
        public static Rect PickerArrow(bool right) => right ? new Rect(PickerW - ArrowW, 0, ArrowW, PickerH) : new Rect(0, 0, ArrowW, PickerH);

        // Victory or Defeat over a table of the kingdoms, room for four at
        // least, and Return to menu and Look at the field.
        public DialogBox Result(int kingdoms)
        {
            const float w = 820f, plateW = 320f;
            int rows = Mathf.Max(4, kingdoms);
            float y = Top(TallTitleBand) + 12f;
            float h = y + 70f + 10f + TableHeadH + rows * TableRowH + 20f + PlateH + 12f + HelpH + 16f + Border;
            var b = Make("Result", w, h, TallTitleBand);
            b.Note = b.At(Inset + Border, y, w - 2 * (Inset + Border), 70f);
            y += 70f + 10f;
            b.Head = b.At(Inset + Border, y, w - 2 * (Inset + Border), TableHeadH);
            y += TableHeadH;
            b.List = b.At(Inset + Border, y, w - 2 * (Inset + Border), rows * TableRowH);
            for (int i = 0; i < kingdoms; i++) b.Rows.Add(b.At(Inset + Border, y + i * TableRowH, w - 2 * (Inset + Border), TableRowH));
            y += rows * TableRowH + 20f;
            float x = (w - 2 * plateW - Gap) / 2f;
            b.Plates.Add(new KeyValuePair<string, Rect>("Return to menu", b.At(x, y, plateW, PlateH)));
            b.Plates.Add(new KeyValuePair<string, Rect>("Look at the field", b.At(x + plateW + Gap, y, plateW, PlateH)));
            return b;
        }

        // The kingdoms table's columns, relative to a row's left: the emblem,
        // the player, the kingdom, the team and whether it stands.
        public static readonly Rect[] Columns =
        {
            new Rect(4, 4, 28, 28), new Rect(44, 0, 260, TableRowH), new Rect(312, 0, 170, TableRowH),
            new Rect(490, 0, 80, TableRowH), new Rect(578, 0, 162, TableRowH),
        };

        // The folder screen: the intro, the field and Browse, the status
        // line, where the game usually is or the Browse list, then Use this
        // folder and Quit or Cancel. As tall as the room under the strip.
        public DialogBox Folder()
        {
            float w = Mathf.Min(1280f, Canvas.x - 2 * Margin);
            float room = Canvas.y - ScreenBand - StripH - ScreenBand - 2 * Margin;
            float h = Mathf.Clamp(room, 640f, 860f);
            var b = Make("Folder", w, h, TitleBand, true);
            float x = Inset + Border, inner = w - 2 * x, top = Top(TitleBand);
            b.Note = b.At(x, top + 16f, inner, 96f);
            b.Field = b.At(x, top + 120f, inner - PlateW - Gap, PlateH);
            b.Plates.Add(new KeyValuePair<string, Rect>("Browse", b.At(w - x - PlateW, top + 120f, PlateW, PlateH)));
            b.Status = b.At(x, top + 188f, inner, 64f);
            float plateY = h - Border - 12f - HelpH - 12f - PlateH;
            b.List = b.At(x, top + 264f, inner, plateY - Gap - (top + 264f));
            const float use = 320f;
            float px = (w - use - Gap - PlateW) / 2f;
            b.Plates.Add(new KeyValuePair<string, Rect>("Use this folder", b.At(px, plateY, use, PlateH)));
            b.Plates.Add(new KeyValuePair<string, Rect>("Leave", b.At(px + use + Gap, plateY, PlateW, PlateH)));
            return b;
        }

        // The Browse list inside the folder dialog's lower rect, relative to
        // it: Up, the folder it shows, and the rows.
        public static Rect BrowseUp => new Rect(12f, 12f, PlateW, PlateMinH);
        public static Rect BrowseFolder(float w) => new Rect(PlateW + 28f, 12f, w - PlateW - 40f, PlateMinH);
        public static Rect BrowseRows(Vector2 size) => new Rect(12f, 72f, size.x - 24f, size.y - 84f);

        // The engine notice: what is wrong and what to do.
        public DialogBox Notice()
        {
            float w = Mathf.Min(1040f, Canvas.x - 2 * Margin);
            const float h = 360f;
            var b = Make("Notice", w, h, TitleBand, true);
            b.Note = b.At(Inset + Border, Top(TitleBand) + 24f, w - 2 * (Inset + Border), b.Local(b.Help).y - Top(TitleBand) - 32f);
            return b;
        }

        // The loading screen, over the whole canvas: bands top and bottom,
        // the game's name, the map's in Cinzel, who is fighting, the bar
        // with the percent at its right, the stage and a tip.
        public LoadingLayout Loading()
        {
            float W = Canvas.x, H = Canvas.y;
            var l = new LoadingLayout();
            l.Bands = new[] { new Rect(0, 0, W, Interlace), new Rect(0, H - Interlace, W, Interlace) };
            l.Knots = new[] { new Rect(0, 0, Knot, Knot), new Rect(W - Knot, 0, Knot, Knot), new Rect(0, H - Knot, Knot, Knot), new Rect(W - Knot, H - Knot, Knot, Knot) };
            l.Name = new Rect(0, 26f, W, 56f);
            float mid = 0.62f * H;
            l.Title = new Rect(Margin, mid - 56f, W - 2 * Margin, 112f);
            l.Seats = new Rect(Margin, mid + 56f, W - 2 * Margin, 104f);
            float barW = Mathf.Min(BarW, W - 2 * Margin - 2 * 112f);
            float barY = Mathf.Max(0.8f * H, l.Seats.yMax + Gap);
            l.Bar = new Rect((W - barW) / 2f, barY, barW, BarH);
            l.Percent = new Rect(l.Bar.xMax + 12f, barY - 4f, 100f, BarH + 8f);
            l.Stage = new Rect(Margin, barY + 44f, W - 2 * Margin, 36f);
            l.Tip = new Rect(Margin, barY + 96f, W - 2 * Margin, 36f);
            return l;
        }
    }

    // One dialog's parts in canvas units, from the canvas's top left.
    public sealed class DialogBox
    {
        public string Name;
        public Rect Frame, Interlace, Title, Body, Help;
        public Rect[] Knots;
        public int TitleSize;
        // What some dialogs have: the close cross, a line under the plates
        // or the sentence, the list or table, its heading, a text field and
        // a status line.
        public Rect Close, Note, List, Head, Field, Status;
        public readonly List<Rect> Rows = new List<Rect>();
        public readonly List<KeyValuePair<string, Rect>> Plates = new List<KeyValuePair<string, Rect>>();

        // A rect given from the dialog's own top left, put in canvas units.
        public Rect At(float x, float y, float w, float h) => new Rect(Frame.x + x, Frame.y + y, w, h);

        // A rect in canvas units, put back relative to the dialog's top left.
        public Rect Local(Rect r) => new Rect(r.x - Frame.x, r.y - Frame.y, r.width, r.height);

        public Rect Plate(string name)
        {
            foreach (var p in Plates) if (p.Key == name) return p.Value;
            return Rect.zero;
        }
    }

    public sealed class LoadingLayout
    {
        public Rect[] Bands, Knots;
        public Rect Name, Title, Seats, Bar, Percent, Stage, Tip;
    }
}
