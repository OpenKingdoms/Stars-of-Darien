// ScarSnow.cs - snow settling over the battle's old scars while it snows.
// It keeps the battle second each patch of ground was last scarred, so a
// fresh crater shows dark through the snow and an old one whitens over a
// couple of minutes. The terrain shader lays the snow on (OkuScar.hlsl).
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // What the snow over the scars held when a game was saved: the battle
    // second each patch was last scarred, when the snow began (NaN for no
    // snow) and the clock they were kept by.
    public sealed class SnowState
    {
        public int W, H;
        public float Now, Since;
        public float[] Scarred;
    }

    public sealed class ScarSnow
    {
        // World units a texel spans, and how long snow takes to cover a scar.
        public const float TexelUnits = 2f, CoverSeconds = 120f;
        const float UploadSeconds = 0.25f;

        readonly ScarMap map;
        readonly Vector2 size;
        readonly int w, h;
        readonly float[] scarred;
        Texture2D tex;
        bool dirty;
        float uploaded = float.NegativeInfinity, since = float.NaN, now;

        static readonly int SnowedId = Shader.PropertyToID("_OkuScarSnowed"), SnowingId = Shader.PropertyToID("_OkuScarSnowing");

        public bool Snowing { get; private set; }

        public ScarSnow(IGameBackend backend, ScarMap map)
        {
            this.map = map;
            size = backend.Terrain != null ? backend.Terrain.Size : new Vector2(64f, 64f);
            w = Mathf.Clamp(Mathf.CeilToInt(size.x / TexelUnits), 1, 512);
            h = Mathf.Clamp(Mathf.CeilToInt(size.y / TexelUnits), 1, 512);
            scarred = new float[w * h];
            if (map != null) map.Stamping += Mark;
        }

        // A stamp drawn into the scar map: the ground under it is scarred afresh.
        void Mark(ScarStamp s)
        {
            float t = map != null ? map.Now : now;
            float r = Mathf.Max(0.5f, s.Extent);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((s.X - r) / size.x * w), 0, w - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((s.X + r) / size.x * w), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((1f + (s.Z - r) / size.y) * h), 0, h - 1), y1 = Mathf.Clamp(Mathf.FloorToInt((1f + (s.Z + r) / size.y) * h), 0, h - 1);
            float r2 = (r + TexelUnits) * (r + TexelUnits);
            for (int y = y0; y <= y1; y++)
            {
                float z = ((y + 0.5f) / h - 1f) * size.y;
                for (int x = x0; x <= x1; x++)
                {
                    float px = (x + 0.5f) / w * size.x;
                    float dx = px - s.X, dz = z - s.Z;
                    if (dx * dx + dz * dz <= r2) scarred[y * w + x] = t;
                }
            }
            dirty = true;
        }

        // How much snow lies over the scars at a point now, 0 to 1, as the shader has it.
        public float CoverAt(float x, float z)
        {
            if (!Snowing) return 0f;
            int px = Mathf.Clamp(Mathf.FloorToInt(x / size.x * w), 0, w - 1), py = Mathf.Clamp(Mathf.FloorToInt((1f + z / size.y) * h), 0, h - 1);
            return Mathf.Clamp01((now - Mathf.Max(scarred[py * w + px], since)) / CoverSeconds);
        }

        // A frame: whether it snows and the battle's clock in seconds.
        public void Update(bool snowing, float now)
        {
            this.now = now;
            Snowing = snowing;
            if (!snowing) since = float.NaN;
            else if (float.IsNaN(since)) since = now;
            if (tex == null)
            {
                tex = new Texture2D(w, h, TextureFormat.RFloat, false, true)
                {
                    name = "scar snow", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
                dirty = true;
            }
            if (dirty && snowing && (now - uploaded >= UploadSeconds || now < uploaded))
            {
                tex.SetPixelData(scarred, 0);
                tex.Apply(false);
                dirty = false;
                uploaded = now;
            }
            Shader.SetGlobalTexture(SnowedId, tex);
            Shader.SetGlobalVector(SnowingId, new Vector4(now, snowing ? since : 0f, CoverSeconds, snowing ? 1f : 0f));
        }

        // When each patch was last scarred and when the snow began, for a saved game.
        public SnowState Capture() => new SnowState { W = w, H = h, Now = now, Since = since, Scarred = (float[])scarred.Clone() };

        // Takes what a saved game kept, its times moved onto this battle's
        // clock, so the snow buries the old scars as it did.
        public void Restore(SnowState s, float clock)
        {
            if (s == null) return;
            float shift = clock - s.Now;
            if (s.W == w && s.H == h && s.Scarred != null && s.Scarred.Length == scarred.Length)
                for (int k = 0; k < scarred.Length; k++) scarred[k] = s.Scarred[k] + shift;
            else if (s.Scarred != null && s.W > 0 && s.H > 0 && s.Scarred.Length >= (long)s.W * s.H)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        scarred[y * w + x] = s.Scarred[Mathf.Min(s.H - 1, y * s.H / h) * s.W + Mathf.Min(s.W - 1, x * s.W / w)] + shift;
            since = float.IsNaN(s.Since) ? float.NaN : s.Since + shift;
            now = clock;
            dirty = true;
        }

        public void Dispose()
        {
            if (map != null) map.Stamping -= Mark;
            Shader.SetGlobalVector(SnowingId, Vector4.zero);
            if (tex != null) Looks.Release(tex);
            tex = null;
        }
    }
}
