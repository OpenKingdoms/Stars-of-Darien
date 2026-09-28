// BattleHud.cs - the battle screen's panels where the original puts them
// (HudLayout). The sidebar has the minimap, the orders, spells and stances,
// the help box and the crystal ball. The strip has the selected unit and
// the unit under the pointer or its target. Every build option shows at
// once over the play area. Its canvas draws at one scale, s screen pixels
// per classic pixel, in HudArt's Carolingian skin, and the world camera
// draws only the play area. Click the minimap to look there, right click
// it to send the selection there.
using System.Collections.Generic;
using System.Linq;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class BattleHud
    {
        // Lay out for this size instead of the screen's, for captures that
        // render off screen.
        public static Vector2Int? SizeOverride;

        readonly GameRoot root;
        readonly Canvas canvas;
        readonly CanvasScaler scaler;
        readonly Texture2D vellum;
        readonly List<Object> painted = new List<Object>();
        readonly Dictionary<int, Texture2D> rawBuild = new Dictionary<int, Texture2D>(), sharpBuild = new Dictionary<int, Texture2D>();
        readonly Dictionary<string, Texture2D> rawAction = new Dictionary<string, Texture2D>(), sharpAction = new Dictionary<string, Texture2D>();
        readonly UnitState[] units = new UnitState[EntityRenderer.MaxUnits];
        readonly List<(Text badge, int def, int factory)> queueBadges = new List<(Text, int, int)>();

        HudLayout layout;
        int builtW, builtH, builtPercent = -1;
        bool builtBadges;
        RectTransform content, orders, builds, mapParts, unitPanel, targetPanel, manaTrough, targetManaTrough, view;
        RawImage minimap, dots, ball, portrait, shield, targetShield, plait;
        Text nameText, status, numbers, kills, group, targetName, help1, help2, income, spend, clock;
        Image healthFill, manaFill, targetHealthFill, targetManaFill, targetPip;
        Texture2D mapTex, dotTex, ballTex, bossTex;
        Texture2D[] shields;
        Color32[] dotPx;
        string minimapFor, mapTexFor, gridKey = "", nameShown, targetNameShown;
        float nextDots, nextPanel, ballFill = -1f;
        (string a, string b)? hoverHelp;
        UnitAction[] actions = System.Array.Empty<UnitAction>();
        bool basicOrders;
        int buildPage, lastDefsKey, firstSelected = -1;

        public BattleHud(GameRoot root)
        {
            this.root = root;
            var go = new GameObject("BattleHud", typeof(RectTransform));
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            canvas.pixelPerfect = true;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<HudViewport>().Off = ResetViewport;
            vellum = HudArt.ToTexture(HudArt.VellumTile(HudArt.Vellum));
            EnsureLayout();
        }

        public Canvas Canvas => canvas;
        public GameObject Root => canvas.gameObject;
        public HudLayout Layout => layout;
        public string HelpLine1 => help1 != null ? help1.text : "";
        public string HelpLine2 => help2 != null ? help2.text : "";

        public void Tick()
        {
            EnsureLayout();
            var world = root.World;
            if (world == null || root.Backend.Terrain == null || !canvas.gameObject.activeInHierarchy) return;
            var cam = world.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            if (cam != null && cam.rect != layout.Viewport) cam.rect = layout.Viewport;
            if (minimapFor != root.Setup.MapId) LoadMinimap();
            UpdateView(world);
            if (Time.unscaledTime >= nextDots) { nextDots = Time.unscaledTime + 0.2f; UpdateDots(); }
            if (Time.unscaledTime >= nextPanel) { nextPanel = Time.unscaledTime + 0.1f; UpdatePanel(world); UpdatePool(); }
            UpdateTarget(world);
            var b = root.Backend;
            int secs = (int)(b.Tick / (uint)Mathf.Max(1, b.TicksPerSecond));
            string c = secs >= 3600 ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}" : $"{secs / 60:00}:{secs % 60:00}";
            if (clock.text != c) clock.text = c;
        }

        // The whole screen for the world again, when the HUD goes away.
        static void ResetViewport()
        {
            var cam = Camera.main;
            if (cam != null) cam.rect = new Rect(0, 0, 1, 1);
        }

        // ---- Building the panels ----

        void EnsureLayout()
        {
            var size = SizeOverride ?? new Vector2Int(Screen.width, Screen.height);
            // Never smaller than the original's own screen.
            size = new Vector2Int(Mathf.Max(size.x, 640), Mathf.Max(size.y, 480));
            int percent = HudLayout.NearestStop(root.Options != null ? root.Options.UiScale : HudLayout.DefaultScale);
            bool badges = root.Options == null || root.Options.HotkeyLetters;
            if (layout != null && size.x == builtW && size.y == builtH && percent == builtPercent && badges == builtBadges) return;
            builtW = size.x;
            builtH = size.y;
            builtPercent = percent;
            builtBadges = badges;
            layout = new HudLayout(size.x, size.y, percent);
            scaler.scaleFactor = layout.S;
            Rebuild();
        }

        float Px => 1f / layout.S;

        void Rebuild()
        {
            if (content != null) { content.gameObject.SetActive(false); Looks.Release(content.gameObject); }
            foreach (var o in painted) Looks.Release(o);
            painted.Clear();
            foreach (var kv in sharpBuild) if (kv.Value != null && (!rawBuild.TryGetValue(kv.Key, out var r) || r != kv.Value)) Looks.Release(kv.Value);
            foreach (var kv in sharpAction) if (kv.Value != null && (!rawAction.TryGetValue(kv.Key, out var r) || r != kv.Value)) Looks.Release(kv.Value);
            sharpBuild.Clear();
            sharpAction.Clear();
            ballTex = null;
            queueBadges.Clear();
            content = UiKit.Rect(canvas.transform, "Content").Fill();
            float d = HudArt.Density(layout.S);
            bossTex = Own(HudArt.Boss(7f, d, HudArt.Sapphire));
            shields = new[] { Own(HudArt.Shield(0, d)), Own(HudArt.Shield(1, d)), Own(HudArt.Shield(2, d)) };
            BuildSidebar(d);
            BuildStrip(d);
            orders = Put(content, "Orders", layout.Block);
            builds = Put(content, "Builds", layout.Play);
            minimapFor = null;
            gridKey = null;
            ballFill = -1f;
            nameShown = targetNameShown = null;
            nextPanel = 0;
        }

        Texture2D Own(HudArt.Sheet sheet)
        {
            var t = HudArt.ToTexture(sheet);
            painted.Add(t);
            return t;
        }

        static RectTransform Put(Transform parent, string name, Rect r)
        {
            var rt = UiKit.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
            return rt;
        }

        static Image Solid(Transform parent, string name, Rect r, Color c, bool ray = false)
        {
            var img = Put(parent, name, r).gameObject.AddComponent<Image>();
            img.sprite = UiKit.White;
            img.color = c;
            img.raycastTarget = ray;
            return img;
        }

        static RawImage Tex(Transform parent, string name, Rect r, Texture t, bool ray = false)
        {
            var img = Put(parent, name, r).gameObject.AddComponent<RawImage>();
            img.texture = t;
            img.raycastTarget = ray;
            return img;
        }

        // Vellum over a rect, tiled from the canvas origin so panels meet.
        RawImage Vellum(Transform parent, string name, Rect r, Rect canvasRect)
        {
            var img = Tex(parent, name, r, vellum, true);
            const float t = HudArt.VellumTileCp;
            img.uvRect = new Rect(canvasRect.x / t, -canvasRect.yMax / t, canvasRect.width / t, canvasRect.height / t);
            return img;
        }

        Text Words(Transform parent, string name, Rect r, Font font, float cp, float floor, Color colour, TextAnchor align)
        {
            var t = Put(parent, name, r).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = layout.Font(cp, floor);
            t.color = colour;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        // Shrinks to fit its box, never under the floor.
        Text FitWords(Transform parent, string name, Rect r, Font font, float cp, float minCp, float floor, Color colour, TextAnchor align)
        {
            var t = Words(parent, name, r, font, cp, floor, colour, align);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Min(t.fontSize, layout.Font(minCp, floor));
            return t;
        }

        // A hollow rect of four bars.
        static Image[] Ring(Transform parent, string name, Rect r, float t, Color c)
        {
            return new[]
            {
                Solid(parent, name, new Rect(r.x, r.y, r.width, t), c),
                Solid(parent, name, new Rect(r.x, r.yMax - t, r.width, t), c),
                Solid(parent, name, new Rect(r.x, r.y + t, t, r.height - 2 * t), c),
                Solid(parent, name, new Rect(r.xMax - t, r.y + t, t, r.height - 2 * t), c),
            };
        }

        static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2 * by, r.height + 2 * by);

        static Rect Local(Rect r, Rect origin) => new Rect(r.x - origin.x, r.y - origin.y, r.width, r.height);

        RawImage BossAt(Transform parent, Vector2 centre, float diam, Texture2D tex) =>
            Tex(parent, "Boss", new Rect(centre.x - diam / 2f, centre.y - diam / 2f, diam + 1f, diam + 1f), tex);

        void SetHover((string, string)? lines)
        {
            hoverHelp = lines;
            UpdateHelp();
        }

        void Hover(GameObject go, System.Func<(string, string)> lines, System.Action<bool> also = null)
        {
            go.AddComponent<HoverHint>().Show = on => { also?.Invoke(on); SetHover(on ? lines() : ((string, string)?)null); };
        }

        void BuildSidebar(float d)
        {
            var sb = layout.Sidebar;
            var side = Vellum(content, "Sidebar", sb, sb).transform;
            // The left edge: two-strand interlace on minium, an ink keyline toward the play area.
            var band = Tex(side, "Band", new Rect(0, 0, HudLayout.BandW, sb.height), Own(HudArt.TwistTile(HudLayout.BandW, 10f, d, true, HudArt.Minium)));
            band.uvRect = new Rect(0, 0, 1, sb.height / 10f);
            Solid(side, "Keyline", new Rect(0, 0, Px, sb.height), HudArt.Ink);

            if (layout.MapHangs)
            {
                var mp = layout.MapPanel;
                var hang = Vellum(content, "MapPanel", mp, mp).transform;
                var hb = Tex(hang, "Band", new Rect(0, 0, HudLayout.BandW, mp.height), band.texture);
                hb.uvRect = new Rect(0, 0, 1, mp.height / 10f);
                Solid(hang, "Keyline", new Rect(0, 0, Px, mp.height), HudArt.Ink);
                Solid(hang, "Keyline", new Rect(0, mp.height - Px, mp.width, Px), HudArt.Ink);
            }

            var blk = Put(content, "Block", layout.Block);
            var top = Tex(blk, "TopBand", HudLayout.TopBand, Own(HudArt.TwistTile(4f, 10f, d, false, HudArt.Minium)));
            top.uvRect = new Rect(0, 0, HudLayout.TopBand.width / 10f, 1);
            var knot = Own(HudArt.SolomonKnot(12f, d));
            var knotBoss = Own(HudArt.Boss(4f, d, HudArt.Garnet));
            foreach (var k in HudLayout.Knots)
            {
                Tex(blk, "Knot", k, knot);
                BossAt(blk, k.center, 4f, knotBoss);
            }

            // The header: the game menu and the clock.
            var loz = Own(HudArt.Lozenge(HudLayout.MenuButton.width, HudLayout.MenuButton.height, d, false));
            var lozLit = Own(HudArt.Lozenge(HudLayout.MenuButton.width, HudLayout.MenuButton.height, d, true));
            var menu = Tex(blk, "Menu", HudLayout.MenuButton, loz, true);
            var mb = menu.gameObject.AddComponent<Button>();
            mb.transition = Selectable.Transition.None;
            mb.targetGraphic = menu;
            mb.onClick.AddListener(() => root.Flow.Fire(FlowEvent.Pause));
            Words(menu.transform, "Label", new Rect(0, 0, HudLayout.MenuButton.width, HudLayout.MenuButton.height), UiKit.UncialFont, 11, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleCenter).text = "Menu";
            Hover(menu.gameObject, () => ("Menu", "F1 or Pause"), on => { if (menu) menu.texture = on ? lozLit : loz; });
            clock = Words(blk, "Clock", HudLayout.Clock, UiKit.TitleFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleRight);

            // The ornament column between the order pairs, as the original's panel has.
            plait = Tex(blk, "Plait", HudLayout.CentreBand, Own(HudArt.PlaitPanel(HudLayout.CentreBand.width, HudLayout.CentreBand.height, d, HudArt.Azurite)));

            // The help box, a purple inset with a gold keyline.
            var hr = HudLayout.Help;
            Solid(blk, "HelpInk", Grow(hr, Px), HudArt.Ink);
            Solid(blk, "HelpGold", hr, HudArt.Gold);
            Solid(blk, "HelpGround", Grow(hr, -0.75f), HudArt.Purple);
            help1 = FitWords(blk, "Help1", new Rect(hr.x + 3, hr.y + 2, hr.width - 6, 15), UiKit.UncialFont, 12, 8, HudLayout.BodyFloor, HudArt.GoldHi, TextAnchor.MiddleCenter);
            help2 = FitWords(blk, "Help2", new Rect(hr.x + 3, hr.y + 17, hr.width - 6, 15), UiKit.BodyFont, 12, 8, HudLayout.BodyFloor, HudArt.Silver, TextAnchor.MiddleCenter);

            // The player's pool: income, the crystal ball, spending.
            income = Words(blk, "Income", HudLayout.Income, UiKit.TitleFont, 11, HudLayout.BodyFloor, HudArt.Verdigris, TextAnchor.MiddleCenter);
            spend = Words(blk, "Spend", HudLayout.Spend, UiKit.TitleFont, 11, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleCenter);
            ball = Tex(blk, "Ball", HudLayout.Ball, null, true);
            Hover(ball.gameObject, PoolLines);
            var bc = HudLayout.Ball.center;
            foreach (var o in new[] { new Vector2(0, -16), new Vector2(16, 0), new Vector2(0, 16), new Vector2(-16, 0) })
                BossAt(blk, bc + o, 7f, bossTex);
        }

        void BuildStrip(float d)
        {
            var st = layout.Strip;
            var strip = Vellum(content, "Strip", st, st).transform;
            var rule = new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.06f);
            Solid(strip, "Rule", new Rect(108, 25.5f, st.width - 112, Px), rule);
            Solid(strip, "Rule", new Rect(108, 43.5f, st.width - 112, Px), rule);
            var band = Tex(strip, "Band", new Rect(0, 0, st.width, HudLayout.StripBand), Own(HudArt.TwistTile(HudLayout.StripBand, 10f, d, false, HudArt.Azurite)));
            band.uvRect = new Rect(0, 0, st.width / 10f, 1);
            Solid(strip, "Keyline", new Rect(0, 0, st.width, Px), HudArt.Ink);
            var cap = HudLayout.EndCap;
            var rc = new Rect(cap.x + (cap.width - 40f) / 2f, cap.y + (cap.height - 40f) / 2f, 40f, 40f);
            Tex(strip, "EndCap", rc, Own(HudArt.Roundel(40f, d)));
            BossAt(strip, rc.center, 12f, Own(HudArt.Boss(12f, d, HudArt.Garnet)));

            unitPanel = Put(strip, "Unit", new Rect(0, 0, st.width, st.height));
            var pr = HudLayout.Portrait;
            Solid(unitPanel, "PortraitInk", Grow(pr, 1f + Px), HudArt.Ink);
            Solid(unitPanel, "PortraitGold", Grow(pr, 1f), HudArt.Gold);
            portrait = Tex(unitPanel, "Portrait", pr, null);
            shield = Tex(unitPanel, "Shield", HudLayout.Shield, null);
            nameText = Words(unitPanel, "Name", HudLayout.Name, UiKit.UncialFont, 14, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            Trough(unitPanel, "Health", HudLayout.HealthTrough, HudArt.Minium, out healthFill);
            manaTrough = Trough(unitPanel, "Mana", HudLayout.ManaTrough, HudArt.Azurite, out manaFill);
            kills = Words(unitPanel, "Kills", HudLayout.Kills, UiKit.TitleFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleCenter);
            status = FitWords(unitPanel, "Status", HudLayout.Status, UiKit.BodyFont, 12, 10, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            numbers = Words(unitPanel, "Numbers", HudLayout.Numbers, UiKit.TitleFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            group = layout.Group.width > 0 ? FitWords(unitPanel, "Group", layout.Group, UiKit.BodyFont, 12, 10, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft) : null;
            unitPanel.gameObject.SetActive(false);

            if (!layout.HasTarget) { targetPanel = null; return; }
            targetPanel = Put(strip, "Target", new Rect(0, 0, st.width, st.height));
            targetPip = Solid(targetPanel, "Owner", layout.TargetPip, Color.white);
            targetName = Words(targetPanel, "Name", layout.TargetName, UiKit.UncialFont, 14, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            Trough(targetPanel, "Health", layout.TargetHealth, HudArt.Minium, out targetHealthFill);
            targetManaTrough = Trough(targetPanel, "Mana", layout.TargetMana, HudArt.Azurite, out targetManaFill);
            targetShield = Tex(targetPanel, "Shield", layout.TargetShield, null);
            targetPanel.gameObject.SetActive(false);
        }

        // A vellum-shade trough in an ink keyline, its fill 3 cp tall.
        RectTransform Trough(Transform parent, string name, Rect r, Color fill, out Image fillImg)
        {
            var ink = Solid(parent, name, r, HudArt.Ink, true);
            Solid(ink.transform, "Ground", new Rect(Px, Px, r.width - 2 * Px, r.height - 2 * Px), HudArt.VellumShade);
            var f = Local(HudLayout.Fill(r), r);
            fillImg = Solid(ink.transform, "Fill", f, fill);
            // A lit top third, stretched along with the fill.
            var lit = Solid(fillImg.transform, "Lit", new Rect(0, 0, 0, f.height / 3f), new Color(1, 1, 1, 0.18f));
            lit.rectTransform.anchorMax = new Vector2(1, 1);
            bool health = name == "Health";
            Hover(ink.gameObject, () => health ? HealthLines() : ManaLines());
            return ink.rectTransform;
        }

        static void SetFill(Image fill, float fraction)
        {
            var rt = fill.rectTransform;
            rt.sizeDelta = new Vector2(97f * Mathf.Clamp01(fraction), rt.sizeDelta.y);
        }

        // ---- The minimap ----

        void LoadMinimap()
        {
            minimapFor = root.Setup.MapId;
            if (mapTexFor != minimapFor)
            {
                mapTexFor = minimapFor;
                if (mapTex != null) Looks.Release(mapTex);
                mapTex = UiKit.ToTexture(root.Backend.MapPreview(minimapFor, 256), true);
                if (mapTex == null)
                {
                    // No overview: a picture of the ground from its chunks.
                    var t = root.Backend.Terrain;
                    var chunks = new Dictionary<int, RgbaImage>();
                    RgbaImage Chunk(int c) => chunks.TryGetValue(c, out var i) ? i : chunks[c] = root.Backend.TerrainChunk(c);
                    mapTex = UiKit.ToTexture(TerrainView.WholeMap(t, Chunk, 256), true);
                }
            }
            var size = root.Backend.Terrain.Size;
            if (dotTex != null) Looks.Release(dotTex);
            int w = 128, h = Mathf.Max(8, Mathf.RoundToInt(128 * size.y / Mathf.Max(1, size.x)));
            dotTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            dotPx = new Color32[w * h];

            if (mapParts != null) Looks.Release(mapParts.gameObject);
            mapParts = Put(content, "MapParts", new Rect(0, 0, layout.W, layout.H));
            mapParts.SetSiblingIndex(content.Find("Orders").GetSiblingIndex());
            var mr = layout.MapRect(size.x / Mathf.Max(1, size.y));
            Solid(mapParts, "MapInk", Grow(mr, 1.5f + Px), HudArt.Ink);
            Solid(mapParts, "MapGold", Grow(mr, 1.5f), HudArt.Gold);
            Solid(mapParts, "MapGround", mr, HudArt.Purple);
            minimap = Tex(mapParts, "Minimap", mr, mapTex, true);
            minimap.gameObject.AddComponent<MinimapInput>().Clicked = OnMinimap;
            dots = UiKit.Rect(minimap.transform, "Units").Fill().gameObject.AddComponent<RawImage>();
            dots.texture = dotTex;
            dots.raycastTarget = false;
            var box = UiKit.Picture(minimap.transform, "View", UiKit.Frame, new Color(1, 1, 1, 0.9f), true);
            box.raycastTarget = false;
            box.pixelsPerUnitMultiplier = 14f * layout.S / 1.5f;
            view = box.rectTransform;
            foreach (var corner in new[] { new Vector2(mr.xMin - 1f, mr.yMax + 1f), new Vector2(mr.xMax + 1f, mr.yMax + 1f) })
                BossAt(mapParts, corner, 7f, bossTex);
            var carpet = layout.Carpet(mr);
            if (carpet.width > 0)
            {
                Tex(mapParts, "Carpet", carpet, Own(HudArt.CarpetPanel(carpet.width, carpet.height, HudArt.Density(layout.S))));
                var c = carpet.center;
                Solid(mapParts, "Roundel", new Rect(c.x - 5.5f, c.y - 5.5f, 11f, 11f), HudArt.Purple);
                BossAt(mapParts, c, 7f, Own(HudArt.Boss(7f, HudArt.Density(layout.S), HudArt.Emerald)));
            }
        }

        void UpdateDots()
        {
            if (dotTex == null) return;
            var b = root.Backend;
            var size = b.Terrain.Size;
            for (int i = 0; i < dotPx.Length; i++) dotPx[i] = new Color32(0, 0, 0, 0);
            int n = b.ReadUnits(units);
            var selected = root.World.Entities.Selected;
            for (int i = 0; i < n; i++)
            {
                var u = units[i];
                if ((u.Flags & UnitFlags.Dying) != 0) continue;
                if (root.World.Entities.Hidden != null && root.World.Entities.Hidden(u)) continue;
                int x = Mathf.FloorToInt(u.Position.x / size.x * dotTex.width);
                int y = Mathf.FloorToInt((1f + u.Position.z / size.y) * dotTex.height);
                var owner = b.PlayerById(u.Player);
                Color32 c = owner != null ? owner.Tint : new Color32(200, 200, 200, 255);
                if (selected.Contains(u.Handle)) c = new Color32(255, 255, 255, 255);
                c.a = 255;
                bool big = b.UnitDefs[u.Def].IsBuilding;
                for (int dy = 0; dy < (big ? 3 : 2); dy++)
                    for (int dx = 0; dx < (big ? 3 : 2); dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px >= 0 && py >= 0 && px < dotTex.width && py < dotTex.height) dotPx[py * dotTex.width + px] = c;
                    }
            }
            dotTex.SetPixels32(dotPx);
            dotTex.Apply(false);
        }

        void UpdateView(WorldView world)
        {
            if (view == null) return;
            var cam = world.Camera;
            var c = cam.GetComponent<Camera>();
            var size = root.Backend.Terrain.Size;
            float wide = 2f * cam.distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = wide * c.aspect / size.x, dd = wide / Mathf.Max(0.3f, Mathf.Sin(cam.pitch * Mathf.Deg2Rad)) / size.y;
            view.anchorMin = view.anchorMax = new Vector2(cam.focus.x / size.x, 1f + cam.focus.z / size.y);
            var r = minimap.rectTransform.rect;
            view.sizeDelta = new Vector2(Mathf.Min(w, 1.5f) * r.width, Mathf.Min(dd, 1.5f) * r.height);
            view.localRotation = Quaternion.Euler(0, 0, -cam.yaw);
        }

        void OnMinimap(Vector2 uv, PointerEventData.InputButton button)
        {
            var world = root.World;
            if (world == null) return;
            var size = root.Backend.Terrain.Size;
            var at = new Vector3(uv.x * size.x, 0, (uv.y - 1f) * size.y);
            at.y = root.Backend.GroundHeight(at.x, at.z);
            if (button == PointerEventData.InputButton.Left) world.Camera.focus = at;
            else if (button == PointerEventData.InputButton.Right && root.Orders != null) root.Orders.MoveBlock(CommandKind.Move, at);
        }

        // ---- The pool and the help box ----

        Economy pool;

        void UpdatePool()
        {
            var b = root.Backend;
            pool = b.ReadEconomy(b.LocalPlayer);
            float fill = pool.Storage > 0 ? Mathf.Clamp01(pool.Mana / pool.Storage) : 0f;
            float q = Mathf.Round(fill * 48f) / 48f;
            if (q != ballFill || ballTex == null)
            {
                ballFill = q;
                if (ballTex != null) { painted.Remove(ballTex); Looks.Release(ballTex); }
                ballTex = Own(HudArt.Ball(q, HudArt.Density(layout.S)));
                ball.texture = ballTex;
            }
            income.text = "+" + Mathf.RoundToInt(pool.Income);
            spend.text = "-" + Mathf.RoundToInt(pool.Expense);
            UpdateHelp();
        }

        (string, string) PoolLines()
        {
            int max = Mathf.FloorToInt(pool.Storage);
            return ("Mana", $"{Mathf.Min(Mathf.FloorToInt(pool.Mana), max)}/{max}");
        }

        UnitState shownUnit;
        bool shownOne;

        (string, string) HealthLines() => shownOne ? ("Health", $"{shownUnit.Health}/{shownUnit.MaxHealth}") : ("Health", "");
        (string, string) ManaLines() => shownOne ? ("Mana", $"{shownUnit.Mana}/{shownUnit.MaxMana}") : PoolLines();

        // The hovered control's label and detail, a placement's keys, or
        // the pool (legacy:152100-152110).
        void UpdateHelp()
        {
            if (help1 == null) return;
            (string, string) lines;
            var orders = root.Orders;
            if (hoverHelp != null) lines = hoverHelp.Value;
            else if (orders != null && orders.Armed == CommandKind.Build && orders.ArmedDef >= 0)
                lines = root.Backend.CanRotate(orders.ArmedDef) ? ("R or ] turns it", "Shift R or [ turns it back") : ("Placing", "It cannot be turned");
            else lines = PoolLines();
            if (help1.text != lines.Item1) help1.text = lines.Item1;
            if (help2.text != lines.Item2) help2.text = lines.Item2;
        }

        // ---- The strip ----

        static string Nice(UnitDef d) =>
            !string.IsNullOrEmpty(d.Title) ? d.Title : string.IsNullOrEmpty(d.Description) ? d.Name : d.Description;

        static string Colour(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // The name opens with a versal in azurite, a size up, and shrinks to fit.
        void ShowName(Text t, string name, ref string shown)
        {
            if (shown == name) return;
            shown = name;
            float width = t.rectTransform.rect.width;
            int max = layout.Font(14, HudLayout.BodyFloor), min = Mathf.Min(max, layout.Font(10, HudLayout.BodyFloor));
            for (int size = max; size >= min; size--)
            {
                t.fontSize = size;
                t.text = Versal(name, size);
                if (t.preferredWidth <= width) break;
            }
        }

        static string Versal(string name, int size) =>
            string.IsNullOrEmpty(name) ? "" : $"<size={Mathf.RoundToInt(size * 1.25f)}><color={Colour(HudArt.Azurite)}>{name.Substring(0, 1)}</color></size>{name.Substring(1)}";

        void UpdatePanel(WorldView world)
        {
            var b = root.Backend;
            var sel = world.Entities.Selected;
            int n = world.Entities.UnitCount;
            var chosen = new List<UnitState>();
            for (int i = 0; i < n; i++)
                if (sel.Contains(world.Entities.Units[i].Handle)) chosen.Add(world.Entities.Units[i]);
            firstSelected = chosen.Count > 0 ? chosen[0].Handle : -1;
            shownOne = chosen.Count == 1;
            if (shownOne) shownUnit = chosen[0];
            unitPanel.gameObject.SetActive(chosen.Count > 0);
            if (chosen.Count == 1)
            {
                var u = chosen[0];
                var def = b.UnitDefs[u.Def];
                bool own = u.Player == b.LocalPlayer;
                ShowName(nameText, Nice(def), ref nameShown);
                SetPortrait(u.Def);
                SetFill(healthFill, u.MaxHealth > 0 ? (float)u.Health / u.MaxHealth : 1f);
                bool caster = own && u.MaxMana > 0;
                manaTrough.gameObject.SetActive(caster);
                if (caster) SetFill(manaFill, (float)u.Mana / u.MaxMana);
                numbers.text = own
                    ? $"<color={Colour(HudArt.Minium)}>{u.Health}/{u.MaxHealth}</color>" + (caster ? $"  <color={Colour(HudArt.Azurite)}>{u.Mana}/{u.MaxMana}</color>" : "")
                    : "";
                status.text = !own ? "" : u.BuildProgress < 1f ? $"Being built, {u.BuildProgress * 100:0}%" : OrderText(b.ReadOrder(u.Handle));
                bool known = b.UnitRecord(u.Handle, out int k, out int rank);
                kills.text = own && known && k > 0 ? k.ToString() : "";
                shield.enabled = known;
                if (known) shield.texture = shields[Mathf.Clamp(rank, 0, 2)];
                if (group != null) group.text = "";
            }
            else if (chosen.Count > 1)
            {
                ShowName(nameText, chosen.Count + " units", ref nameShown);
                var common = chosen.GroupBy(u => u.Def).OrderByDescending(g => g.Count()).First().Key;
                SetPortrait(common);
                long hp = 0, max = 0;
                foreach (var u in chosen) { hp += u.Health; max += u.MaxHealth; }
                SetFill(healthFill, max > 0 ? (float)hp / max : 1f);
                manaTrough.gameObject.SetActive(false);
                bool own = chosen.All(u => u.Player == b.LocalPlayer);
                numbers.text = own ? $"<color={Colour(HudArt.Minium)}>{hp}/{max}</color>" : "";
                string makeUp = string.Join(", ", chosen.GroupBy(u => u.Def).Select(g => $"{g.Count()} {Nice(b.UnitDefs[g.Key])}"));
                if (group != null) { group.text = makeUp; status.text = ""; }
                else status.text = makeUp;
                kills.text = "";
                shield.enabled = false;
            }
            RefreshButtons(chosen);
        }

        void SetPortrait(int def)
        {
            var t = BuildPicture(def);
            portrait.texture = t;
            portrait.color = t != null ? Color.white : HudArt.Purple;
        }

        static string OrderText(UnitOrder o)
        {
            switch (o.Kind)
            {
                case OrderKind.None: return "Standby";
                case OrderKind.Move: return "Moving";
                case OrderKind.Attack: return "Attacking";
                case OrderKind.AttackGround: return "Attacking the ground";
                case OrderKind.Build: return "Building";
                case OrderKind.Patrol: return "Patrolling";
                case OrderKind.Guard: return "Guarding";
                case OrderKind.Repair: return "Repairing";
                case OrderKind.Reclaim: return "Reclaiming";
                case OrderKind.Load: return "Loading";
                case OrderKind.Unload: return "Unloading";
                default: return o.Kind.ToString();
            }
        }

        // The unit under the pointer, else what the selection is after.
        void UpdateTarget(WorldView world)
        {
            if (targetPanel == null) return;
            var b = root.Backend;
            var orders = root.Orders;
            int unit = orders != null && !orders.PointerOverUi ? orders.PointerUnit : -1;
            if (unit >= 0 && unit == firstSelected && world.Entities.Selected.Count == 1) unit = -1;
            if (unit < 0 && firstSelected >= 0)
            {
                var o = b.ReadOrder(firstSelected);
                if (o.TargetUnit >= 0 && o.TargetUnit != firstSelected) unit = o.TargetUnit;
            }
            UnitState found = default;
            bool any = false;
            if (unit >= 0)
                for (int i = 0; i < world.Entities.UnitCount; i++)
                    if (world.Entities.Units[i].Handle == unit) { found = world.Entities.Units[i]; any = true; break; }
            if (any && world.Entities.Hidden != null && world.Entities.Hidden(found)) any = false;
            if (any && (found.Flags & UnitFlags.Dying) != 0) any = false;
            targetPanel.gameObject.SetActive(any);
            if (!any) return;
            var def = b.UnitDefs[found.Def];
            var owner = b.PlayerById(found.Player);
            targetPip.color = owner != null ? (Color)owner.Tint : Color.grey;
            ShowName(targetName, Nice(def), ref targetNameShown);
            SetFill(targetHealthFill, found.MaxHealth > 0 ? (float)found.Health / found.MaxHealth : 1f);
            targetManaTrough.gameObject.SetActive(found.MaxMana > 0);
            if (found.MaxMana > 0) SetFill(targetManaFill, (float)found.Mana / found.MaxMana);
            bool known = b.UnitRecord(found.Handle, out _, out int rank);
            targetShield.enabled = known;
            if (known) targetShield.texture = shields[Mathf.Clamp(rank, 0, 2)];
        }

        // ---- Pictures ----

        Texture2D BuildPicture(int def)
        {
            if (sharpBuild.TryGetValue(def, out var t)) return t;
            if (!rawBuild.TryGetValue(def, out var raw)) rawBuild[def] = raw = UiKit.ToTexture(root.Backend.UnitPicture(def), false);
            return sharpBuild[def] = HudArt.Sharp(raw, layout.S);
        }

        Texture2D ActionPictureFor(int picture, string label)
        {
            if (picture < 0) return null;
            string key = picture + ":" + label;
            if (sharpAction.TryGetValue(key, out var t)) return t;
            if (!rawAction.TryGetValue(key, out var raw)) rawAction[key] = raw = UiKit.ToTexture(root.Backend.ActionPicture(picture), false);
            return sharpAction[key] = HudArt.Sharp(raw, layout.S);
        }

        // ---- Orders, spells and stances ----

        // For a mobile selection the backend lists nothing for.
        static UnitAction[] BasicOrders() => new[]
        {
            new UnitAction { Id = "MOVE", Label = "Move", Command = CommandKind.Move, Target = ActionTarget.Point, Hotkey = "M" },
            new UnitAction { Id = "ATTACK", Label = "Attack", Command = CommandKind.Attack, Target = ActionTarget.PointOrUnit, Hotkey = "A" },
            new UnitAction { Id = "STOP", Label = "Stop", Command = CommandKind.Stop, Target = ActionTarget.None, Hotkey = "S" },
            new UnitAction { Id = "PATROL", Label = "Patrol", Command = CommandKind.Patrol, Target = ActionTarget.Point, Hotkey = "P" },
            new UnitAction { Id = "GUARD", Label = "Guard", Command = CommandKind.Guard, Target = ActionTarget.Unit, Hotkey = "G" },
        };

        // Rebuilds the buttons when what is selected or armed changes, and
        // refreshes the queue counts every time.
        void RefreshButtons(List<UnitState> chosen)
        {
            var b = root.Backend;
            var defs = chosen.Select(u => u.Def).Distinct().OrderBy(d => d).ToList();
            bool mine = chosen.Count > 0 && chosen.All(u => u.Player == b.LocalPlayer);
            actions = mine ? b.SelectionActions() : System.Array.Empty<UnitAction>();
            if (root.Orders != null) root.Orders.Actions = actions;
            basicOrders = mine && actions.Length == 0 && chosen.Any(u => !b.UnitDefs[u.Def].IsBuilding);
            if (basicOrders) actions = BasicOrders();
            var o = root.Orders;
            string armed = o == null ? "" : o.ArmedAction?.Id ?? (o.Armed != null ? o.Armed + ":" + o.ArmedDef : "");
            string key = mine ? string.Join(",", chosen.Select(u => u.Handle)) + "|" + string.Join(",", defs) + "|" +
                string.Join(",", actions.Select(a => a.Id + (a.Enabled ? "1" : "0") + (a.Toggled ? "1" : "0") + a.Picture)) + "|" + buildPage + "|" + armed : "";
            if (key != gridKey)
            {
                gridKey = key;
                Clear(orders);
                Clear(builds);
                queueBadges.Clear();
                bool centreUsed = false;
                if (mine)
                {
                    var placed = HudLayout.PlaceActions(actions.Select(a => a.Id).ToList());
                    foreach (var a in actions)
                        if (placed.TryGetValue(a.Id, out var slot)) { ActionButton(a, slot); centreUsed |= slot.StartsWith("C"); }
                    PlaceBuilds(chosen, defs);
                }
                plait.gameObject.SetActive(!centreUsed);
            }
            foreach (var qb in queueBadges)
            {
                if (qb.factory < 0 || qb.badge == null) continue;
                int q = b.QueuedCount(qb.factory, qb.def);
                string t = q > 0 ? q.ToString() : "";
                if (qb.badge.text != t) qb.badge.text = t;
            }
        }

        // Hidden at once and gone by the end of the frame, so a count of
        // the children sees only the new ones.
        static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Looks.Release(child);
            }
            parent.DetachChildren();
        }

        bool IsArmed(UnitAction a)
        {
            var o = root.Orders;
            if (o == null) return false;
            if (o.ArmedAction != null) return o.ArmedAction.Id == a.Id;
            return basicOrders && o.Armed != null && o.Armed == a.Command && a.Target != ActionTarget.None;
        }

        static string KeyName(string hotkey) =>
            hotkey.Length == 1 && "WASD".IndexOf(char.ToUpperInvariant(hotkey[0])) >= 0 ? "Ctrl " + hotkey.ToUpperInvariant() : hotkey.ToUpperInvariant();

        // The badge on a button: the live key, ^ for Ctrl.
        public static string Badge(string hotkey) =>
            hotkey.Length == 1 && "WASD".IndexOf(char.ToUpperInvariant(hotkey[0])) >= 0 ? "^" + hotkey.ToUpperInvariant() : hotkey.ToUpperInvariant();

        void ActionButton(UnitAction a, string slot)
        {
            var pic = HudLayout.Slot(slot);
            var hit = HudLayout.Hit(pic);
            var rt = Put(orders, "Action " + a.Id, hit);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = UiKit.White;
            face.color = Color.clear;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = face;
            btn.interactable = a.Enabled;
            btn.onClick.AddListener(() => Pressed(a));
            var local = Local(pic, hit);
            bool armed = IsArmed(a);
            Solid(rt, "Keyline", Grow(local, 1f + Px), HudArt.Ink);
            Color rest = a.Enabled ? HudArt.Gold : HudArt.VellumEdge;
            var bezel = Solid(rt, "Bezel", Grow(local, 1f), rest);
            int picture = a.Picture;
            if (armed && picture >= 0) picture = picture / 4 * 4 + 1;
            var tex = ActionPictureFor(picture, a.Label);
            if (tex != null) Tex(rt, "Picture", local, tex);
            else
            {
                Solid(rt, "Face", local, HudArt.VellumShade);
                FitWords(rt, "Label", Grow(local, -1.5f), UiKit.UncialFont, 9, 4, 6, HudArt.Ink, TextAnchor.MiddleCenter).text = a.Label;
            }
            // The game's disabled picture is faded already, so a light wash.
            if (!a.Enabled) Solid(rt, "Wash", local, new Color(HudArt.Vellum.r, HudArt.Vellum.g, HudArt.Vellum.b, tex != null ? 0.15f : 0.35f));
            if (a.Toggled || armed) Ring(rt, "Armed", Grow(local, 1.5f), 1.5f, HudArt.Minium);
            if (builtBadges && !string.IsNullOrEmpty(a.Hotkey))
            {
                string text = Badge(a.Hotkey);
                float w = text.Length > 1 ? 13f : 9f, h = 10f;
                var tab = new Rect(local.xMax - w + 1f, local.yMax - h + 1f, w, h);
                Solid(rt, "BadgeInk", Grow(tab, Px), HudArt.Ink);
                Solid(rt, "BadgeTab", tab, HudArt.Vellum);
                // Square capitals: an uncial A reads as a lambda at this size.
                Words(rt, "Key", tab, UiKit.TitleFont, 8, HudLayout.BadgeFloor, HudArt.Minium, TextAnchor.MiddleCenter).text = text;
            }
            if (a.ManaCost > 0 && slot.StartsWith("W"))
                Words(rt, "Cost", new Rect(local.x, HudLayout.GutterY - hit.y, local.width, HudLayout.GutterH), UiKit.TitleFont, 8, HudLayout.BadgeFloor, HudArt.Azurite, TextAnchor.MiddleCenter).text = a.ManaCost.ToString();
            string detail = !a.Enabled && !string.IsNullOrEmpty(a.Why) ? a.Why
                : a.ManaCost > 0 ? a.ManaCost + " mana"
                : !string.IsNullOrEmpty(a.Hotkey) ? KeyName(a.Hotkey) : "";
            Hover(rt.gameObject, () => (a.Label, detail), on => { if (bezel) bezel.color = on ? HudArt.GoldHi : rest; });
        }

        // A button with nothing to aim at acts at once. The rest wait for a
        // click or a drag in the world. A spell is chosen at once too.
        void Pressed(UnitAction a)
        {
            var orders = root.Orders;
            if (basicOrders)
            {
                if (a.Command == CommandKind.Stop) orders?.Stop();
                else orders?.Arm(a.Command);
                gridKey = null;
                return;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (a.Target == ActionTarget.None) { root.Backend.DoAction(a.Id, Vector3.zero, -1, default, shift); gridKey = null; return; }
            if (a.Kind == ActionKind.Spell) root.Backend.DoAction(a.Id, Vector3.zero, -1, default, false);
            orders?.ArmAction(a);
            gridKey = null;
        }

        // ---- The build row ----

        void PlaceBuilds(List<UnitState> chosen, List<int> defs)
        {
            var b = root.Backend;
            if (defs.Count != 1) return;
            var def = b.UnitDefs[defs[0]];
            var options = def.BuildOptions.Where(o => o >= 0 && o < b.UnitDefs.Count).ToList();
            if (options.Count == 0) return;
            int defsKey = defs[0] + 1;
            if (defsKey != lastDefsKey) { lastDefsKey = defsKey; buildPage = 0; }
            var g = layout.Builds(options.Count);
            int pages = g.Paged ? (options.Count + g.PerPage - 1) / g.PerPage : 1;
            buildPage = Mathf.Clamp(buildPage, 0, pages - 1);
            int factory = def.IsBuilding ? chosen[0].Handle : -1;
            var shown = options.Skip(buildPage * g.PerPage).Take(g.PerPage).ToList();
            for (int i = 0; i < shown.Count; i++) BuildCard(layout.BuildCell(g, i), b.UnitDefs[shown[i]], factory, chosen);
            if (g.Paged) PageTurner(layout.BuildCell(g, shown.Count), pages);
        }

        void BuildCard(Rect cell, UnitDef od, int factory, List<UnitState> chosen)
        {
            int id = od.Id;
            var rt = Put(builds, "Build " + od.Name, cell);
            var ground = rt.gameObject.AddComponent<Image>();
            ground.sprite = UiKit.White;
            ground.color = HudArt.Purple;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = ground;
            var local = new Rect(0, 0, cell.width, cell.height);
            var pic = BuildPicture(id);
            if (pic != null) Tex(rt, "Picture", local, pic);
            else FitWords(rt, "Name", Grow(local, -4f), UiKit.BodyFont, 11, 6, HudLayout.BodyFloor, HudArt.Silver, TextAnchor.MiddleCenter).text = Nice(od);
            var bezel = Ring(rt, "Bezel", Grow(local, -Px), 1.5f, HudArt.Gold);
            Ring(rt, "Keyline", local, Px, HudArt.Ink);
            var o = root.Orders;
            if (o != null && o.Armed == CommandKind.Build && o.ArmedDef == id) Ring(rt, "Armed", local, 2f, HudArt.Minium);
            Text badge = null;
            if (factory >= 0)
            {
                badge = Words(rt, "Queued", new Rect(3f + 1.5f, 2f + 1.5f, cell.width - 8f, 16f), UiKit.TitleFont, 12, HudLayout.BodyFloor, HudArt.GoldHi, TextAnchor.UpperLeft);
                var outline = badge.gameObject.AddComponent<Outline>();
                outline.effectColor = HudArt.Ink;
                outline.effectDistance = new Vector2(Mathf.Max(0.5f, Px), -Mathf.Max(0.5f, Px));
            }
            string cost = od.ManaCost.ToString();
            float tw = 4f + cost.Length * 5.5f, th = 11f;
            var tab = new Rect(cell.width - tw - 2.5f, cell.height - th - 2.5f, tw, th);
            Solid(rt, "CostInk", Grow(tab, Px), HudArt.Ink);
            Solid(rt, "CostTab", tab, HudArt.Vellum);
            Words(rt, "Cost", tab, UiKit.TitleFont, 9, HudLayout.BadgeFloor, HudArt.Azurite, TextAnchor.MiddleCenter).text = cost;
            Hover(rt.gameObject, () => (Nice(od), od.ManaCost + " mana"), on => { foreach (var bz in bezel) if (bz) bz.color = on ? HudArt.GoldHi : HudArt.Gold; });
            if (factory >= 0)
            {
                btn.onClick.AddListener(() => Enqueue(chosen, id, CommandKind.FactoryEnqueue, Shift ? 5 : 1));
                rt.gameObject.AddComponent<RightClick>().Clicked = () => Enqueue(chosen, id, CommandKind.FactoryDequeue, 1);
            }
            else btn.onClick.AddListener(() => { root.Orders?.Arm(CommandKind.Build, id); gridKey = null; });
            queueBadges.Add((badge, id, factory));
        }

        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Only past what the original could ever show on one screen.
        void PageTurner(Rect cell, int pages)
        {
            var rt = Put(builds, "More", cell);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = UiKit.White;
            face.color = HudArt.Vellum;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.onClick.AddListener(() => { buildPage = (buildPage + 1) % pages; gridKey = null; });
            Ring(rt, "Keyline", new Rect(0, 0, cell.width, cell.height), 1.5f, HudArt.Gold);
            Words(rt, "Label", new Rect(0, 0, cell.width, cell.height), UiKit.UncialFont, 12, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleCenter).text = $"{buildPage + 1} of {pages}";
        }

        void Enqueue(List<UnitState> factories, int def, CommandKind kind, int times)
        {
            for (int n = 0; n < times; n++)
                foreach (var f in factories)
                    root.Backend.Command(new GameCommand { Kind = kind, Unit = f.Handle, TargetUnit = -1, BuildDef = def });
        }

        public void Dispose()
        {
            ResetViewport();
            if (canvas) Looks.Release(canvas.gameObject);
            foreach (var o in painted) Looks.Release(o);
            painted.Clear();
            foreach (var t in sharpBuild.Values) Looks.Release(t);
            foreach (var t in rawBuild.Values) Looks.Release(t);
            foreach (var t in sharpAction.Values) Looks.Release(t);
            foreach (var t in rawAction.Values) Looks.Release(t);
            Looks.Release(mapTex);
            Looks.Release(dotTex);
            Looks.Release(vellum);
        }
    }

    // Tells the HUD when its canvas goes away, so the world camera gets
    // the whole screen back.
    public sealed class HudViewport : MonoBehaviour
    {
        public System.Action Off;
        void OnDisable() => Off?.Invoke();
    }

    public sealed class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public System.Action<Vector2, PointerEventData.InputButton> Clicked;

        public void OnPointerDown(PointerEventData e) => Send(e);
        public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Send(e); }

        void Send(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            var uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            Clicked?.Invoke(new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)), e.button);
        }
    }

    public sealed class HoverHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<bool> Show;
        public void OnPointerEnter(PointerEventData e) => Show?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Show?.Invoke(false);
        void OnDisable() => Show?.Invoke(false);
    }

    public sealed class RightClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Clicked;
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Right) Clicked?.Invoke(); }
    }
}
