// GameCursorView.cs - the original's pointers from the game's own art. The
// backend says which one shows, the frames play at their own pace, and a
// whole-number scale keeps the pixels sharp at any resolution.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class GameCursorView : IDisposable
    {
        readonly Func<GameCursor, CursorFrame[]> source;
        readonly Dictionary<GameCursor, CursorFrame[]> art = new Dictionary<GameCursor, CursorFrame[]>();
        readonly Dictionary<(GameCursor, int, int), Texture2D> textures = new Dictionary<(GameCursor, int, int), Texture2D>();
        float since;
        bool custom;
        GameCursor shown;
        int shownFrame = -1, shownScale;

        // 0 picks a scale from the screen height, 1 to 4 fixes it.
        public int ScaleSetting;
        public GameCursor Current { get; private set; } = GameCursor.Normal;
        public int Frame => shownFrame;
        public Texture2D Shown { get; private set; }

        // The art comes from source, IGameBackend.CursorArt in the game.
        public GameCursorView(Func<GameCursor, CursorFrame[]> source, int scaleSetting)
        {
            this.source = source;
            ScaleSetting = scaleSetting;
        }

        // The original drew its pointers for screens near 720 lines high.
        public static int AutoScale(int screenHeight) => Mathf.Clamp(screenHeight / 720, 1, 4);

        public int Scale => ScaleSetting > 0 ? Mathf.Clamp(ScaleSetting, 1, 4) : AutoScale(Screen.height);

        public CursorFrame[] Art(GameCursor cursor)
        {
            if (!art.TryGetValue(cursor, out var frames))
            {
                frames = source(cursor);
                art[cursor] = frames;
            }
            return frames;
        }

        // The frame showing `seconds` after the cursor came up, looping.
        public static int FrameAt(CursorFrame[] frames, float seconds)
        {
            if (frames == null || frames.Length <= 1) return 0;
            int loop = 0;
            foreach (var f in frames) loop += Mathf.Max(1, f.Millis);
            int ms = (int)(Mathf.Max(0f, seconds) * 1000f) % loop;
            for (int i = 0; i < frames.Length; i++)
            {
                int span = Mathf.Max(1, frames[i].Millis);
                if (ms < span) return i;
                ms -= span;
            }
            return 0;
        }

        // Shows `cursor` at `time` seconds, swapping the pointer only when
        // its frame or scale changes. Without art the system pointer stays.
        public void Show(GameCursor cursor, float time)
        {
            if (cursor != Current || shownFrame < 0) { Current = cursor; since = time; }
            var frames = Art(cursor);
            if (frames == null || frames.Length == 0)
            {
                if (custom) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                custom = false;
                Shown = null;
                shownFrame = -1;
                return;
            }
            int f = FrameAt(frames, time - since), s = Scale;
            if (custom && cursor == shown && f == shownFrame && s == shownScale) return;
            if (!textures.TryGetValue((cursor, f, s), out var tex))
            {
                tex = Upscale(frames[f].Image, s);
                textures[(cursor, f, s)] = tex;
            }
            Cursor.SetCursor(tex, new Vector2(frames[f].Hotspot.x * s, frames[f].Hotspot.y * s), CursorMode.Auto);
            custom = true;
            shown = cursor;
            shownFrame = f;
            shownScale = s;
            Shown = tex;
        }

        // Every pixel becomes a scale-by-scale block, and the rows turn
        // over, since a texture's first row is its bottom.
        public static Texture2D Upscale(RgbaImage image, int scale)
        {
            int w = image.Width * scale, h = image.Height * scale;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int src = (image.Height - 1 - y / scale) * image.Width;
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(image.Pixels, (src + x / scale) * 4, px, (y * w + x) * 4, 4);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixelData(px, 0);
            tex.Apply(false, false);
            return tex;
        }

        public void Dispose()
        {
            if (custom) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            custom = false;
            foreach (var t in textures.Values) UnityEngine.Object.DestroyImmediate(t);
            textures.Clear();
        }
    }
}
