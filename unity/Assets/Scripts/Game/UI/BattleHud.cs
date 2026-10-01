// BattleHud.cs - the battle screen's panels where the original puts them
// (HudLayout). The sidebar has the minimap, the orders, spells and stances,
// the help box and the crystal ball. The strip has the selected unit and
// the unit under the pointer or its target. Every build option shows at
// once over the play area. Its canvas draws at one scale, s screen pixels
// per classic pixel, in HudArt's Carolingian skin, and the world camera
// draws only the play area. The minimap's buttons follow the controls:
// classic sends an own selection there with the left button and looks with
// the right, modern looks with the left and sends with the right.
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
        readonly List<Object> painted = new List<Object>(), mapPainted = new List<Object>();
        readonly Dictionary<int, Texture2D> rawBuild = new Dictionary<int, Texture2D>(), sharpBuild = new Dictionary<int, Texture2D>();
        readonly Dictionary<string, Texture2D> rawAction = new Dictionary<string, Texture2D>(), sharpAction = new Dictionary<string, Texture2D>();
        readonly UnitState[] units = new UnitState[EntityRenderer.MaxUnits];
        readonly List<(Text badge, int def, List<int> factories)> queueBadges = new List<(Text, int, List<int>)>();
        readonly Dictionary<string, ActionParts> parts = new Dictionary<string, ActionParts>();
        readonly List<CardParts> cards = new List<CardParts>();
        readonly List<int> idle = new List<int>();

        HudLayout layout;
        int builtW, builtH, builtPercent = -1;
        bool builtBadges;
        RectTransform content, orders, builds, mapParts, unitPanel, targetPanel, manaTrough, targetManaTrough, view, rosterRows;
        RawImage minimap, dots, ball, ballLiquid, portrait, shield, targetShield, plait;
        Text nameText, status, numbers, kills, killsLabel, rank, group, targetName, help1, help2, income, spend, clock;
        Image healthFill, manaFill, targetHealthFill, targetManaFill, targetPip;
        Texture2D mapTex, dotTex, bossTex;
        Texture2D[] shields;
        Color32[] dotPx;
        int dotUnit = 2, dotBuilding = 3;
        string minimapFor, mapTexFor, gridKey = "", nameShown, targetNameShown, rosterKey;
        float nextDots, nextPanel, nextRoster, ballLevel = -1f;
        Rect rosterRoom;
        int lastSelection, lastIdle = -1;
        int clockSecs = -1;
        System.Func<(string, string)> hoverLines;
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
            if (secs != clockSecs)
            {
                clockSecs = secs;
                clock.text = secs >= 3600 ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}" : $"{secs / 60:00}:{secs % 60:00}";
            }
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
            // At once, so text measured while building sees the new scale.
            canvas.scaleFactor = layout.S;
            Rebuild();
        }

        float Px => 1f / layout.S;

        void Rebuild()
        {
            if (content != null) { content.gameObject.SetActive(false); Looks.Release(content.gameObject); }
            foreach (var o in painted) Looks.Release(o);
            painted.Clear();
            foreach (var o in mapPainted) Looks.Release(o);
            mapPainted.Clear();
            foreach (var kv in sharpBuild) if (kv.Value != null && (!rawBuild.TryGetValue(kv.Key, out var r) || r != kv.Value)) Looks.Release(kv.Value);
            foreach (var kv in sharpAction) if (kv.Value != null && (!rawAction.TryGetValue(kv.Key, out var r) || r != kv.Value)) Looks.Release(kv.Value);
            sharpBuild.Clear();
            sharpAction.Clear();
            queueBadges.Clear();
            parts.Clear();
            cards.Clear();
            rosterRows = null;
            content = UiKit.Rect(canvas.transform, "Content").Fill();
            float d = HudArt.Density(layout.S);
            bossTex = Own(HudArt.Boss(7f, d, HudArt.Sapphire));
            shields = new[] { Own(HudArt.Shield(0, d)), Own(HudArt.Shield(1, d)), Own(HudArt.Shield(2, d)) };
            BuildSidebar(d);
            BuildStrip(d);
            orders = Put(content, "Orders", layout.Block);
            builds = Put(content, "Builds", layout.Play);
            mapParts = null;
            minimapFor = null;
            gridKey = null;
            ballLevel = -1f;
            clockSecs = -1;
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
            Move(rt, r);
            return rt;
        }

        static void Move(RectTransform rt, Rect r)
        {
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
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

        // A cost on a vellum tab sized to it, placed by place from the
        // tab's size. Bold, as thin strokes fade this small.
        Text Tab(Transform parent, string name, string text, float cp, float floor, Color colour, System.Func<Vector2, Rect> place, Font font = null)
        {
            var t = Words(parent, name, Rect.zero, font ?? UiKit.BodyFont, cp, floor, colour, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.text = text;
            var r = place(new Vector2(Mathf.Max(t.fontSize * 0.9f, t.preferredWidth + 3f), t.fontSize + 2f));
            Solid(parent, name + "Ink", Grow(r, Px), HudArt.Ink);
            Solid(parent, name + "Tab", r, HudArt.Vellum);
            Move(t.rectTransform, r);
            t.transform.SetAsLastSibling();
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

        // Four bars inset from the parent's edges that follow its size.
        static Image[] EdgeRing(RectTransform parent, string name, float inset, float t, Color c)
        {
            Image Bar(Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                var rt = UiKit.Rect(parent, name);
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.offsetMin = offMin;
                rt.offsetMax = offMax;
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = UiKit.White;
                img.color = c;
                img.raycastTarget = false;
                return img;
            }
            float a = inset, b = inset + t;
            return new[]
            {
                Bar(new Vector2(0, 1), new Vector2(1, 1), new Vector2(a, -b), new Vector2(-a, -a)),
                Bar(new Vector2(0, 0), new Vector2(1, 0), new Vector2(a, a), new Vector2(-a, b)),
                Bar(new Vector2(0, 0), new Vector2(0, 1), new Vector2(a, b), new Vector2(b, -b)),
                Bar(new Vector2(1, 0), new Vector2(1, 1), new Vector2(-b, b), new Vector2(-a, -b)),
            };
        }

        static void Show(Image[] ring, bool on)
        {
            foreach (var i in ring) if (i.gameObject.activeSelf != on) i.gameObject.SetActive(on);
        }

        static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2 * by, r.height + 2 * by);

        static Rect Local(Rect r, Rect origin) => new Rect(r.x - origin.x, r.y - origin.y, r.width, r.height);

        RawImage BossAt(Transform parent, Vector2 centre, float diam, Texture2D tex) =>
            Tex(parent, "Boss", new Rect(centre.x - diam / 2f, centre.y - diam / 2f, diam + 1f, diam + 1f), tex);

        // The help box reads the hovered control's lines while it is hovered.
        void Hover(GameObject go, System.Func<(string, string)> lines, System.Action<bool> also = null)
        {
            go.AddComponent<HoverHint>().Show = on =>
            {
                also?.Invoke(on);
                // Another control's going away leaves the hovered one's lines.
                if (on) hoverLines = lines;
                else if (hoverLines == lines) hoverLines = null;
                UpdateHelp();
            };
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

            // The header: the game menu, clickable over more than its lozenge, and the clock.
            var loz = Own(HudArt.Lozenge(HudLayout.MenuButton.width, HudLayout.MenuButton.height, d, false));
            var lozLit = Own(HudArt.Lozenge(HudLayout.MenuButton.width, HudLayout.MenuButton.height, d, true));
            var hit = Solid(blk, "Menu", HudLayout.MenuHit, Color.clear, true);
            var menu = Tex(hit.transform, "Lozenge", Local(HudLayout.MenuButton, HudLayout.MenuHit), loz);
            var mb = hit.gameObject.AddComponent<Button>();
            mb.transition = Selectable.Transition.None;
            mb.targetGraphic = hit;
            mb.onClick.AddListener(() => { UiKit.Play("menubutton.wav"); root.Flow.Fire(FlowEvent.Pause); });
            Words(menu.transform, "Label", new Rect(0, 0, HudLayout.MenuButton.width, HudLayout.MenuButton.height), UiKit.UncialFont, 11, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleCenter).text = "Menu";
            Hover(hit.gameObject, () => ("Menu", "F1 or Pause"), on => { if (menu) menu.texture = on ? lozLit : loz; });
            var lozRest = Local(HudLayout.MenuButton, HudLayout.MenuHit);
            hit.gameObject.AddComponent<PressHint>().Pressed = down => { if (menu) Move(menu.rectTransform, down ? Nudge(lozRest) : lozRest); };
            clock = Words(blk, "Clock", HudLayout.Clock, UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.Ink, TextAnchor.MiddleRight);

            // The ornament column between the order pairs, as the original's panel has.
            plait = Tex(blk, "Plait", HudLayout.CentreBand, Own(HudArt.PlaitPanel(HudLayout.CentreBand.width, HudLayout.CentreBand.height, d, HudArt.Azurite)));

            // The help box, a purple inset with a gold keyline.
            var hr = HudLayout.Help;
            Solid(blk, "HelpInk", Grow(hr, Px), HudArt.Ink);
            Solid(blk, "HelpGold", hr, HudArt.Gold);
            Solid(blk, "HelpGround", Grow(hr, -0.75f), HudArt.Purple);
            help1 = FitWords(blk, "Help1", new Rect(hr.x + 3, hr.y + 2, hr.width - 6, 15), UiKit.BodyFont, 12, 8, HudLayout.BodyFloor, HudArt.GoldHi, TextAnchor.MiddleCenter);
            help2 = FitWords(blk, "Help2", new Rect(hr.x + 3, hr.y + 17, hr.width - 6, 15), UiKit.BodyFont, 12, 8, HudLayout.BodyFloor, HudArt.Silver, TextAnchor.MiddleCenter);

            // The player's pool: income, the crystal ball, spending. The ball
            // is painted empty and full once, and the full one is cropped to
            // the level.
            income = Words(blk, "Income", HudLayout.Income, UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.Verdigris, TextAnchor.MiddleCenter);
            spend = Words(blk, "Spend", HudLayout.Spend, UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.MiniumDeep, TextAnchor.MiddleCenter);
            ball = Tex(blk, "Ball", HudLayout.Ball, Own(HudArt.Ball(0f, d)), true);
            var liquid = UiKit.Rect(ball.transform, "Liquid");
            liquid.anchorMin = Vector2.zero;
            liquid.anchorMax = new Vector2(1, 0);
            liquid.pivot = new Vector2(0.5f, 0);
            liquid.anchoredPosition = Vector2.zero;
            liquid.sizeDelta = Vector2.zero;
            ballLiquid = liquid.gameObject.AddComponent<RawImage>();
            ballLiquid.texture = Own(HudArt.Ball(1f, d));
            ballLiquid.raycastTarget = false;
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
            nameText = Words(unitPanel, "Name", HudLayout.Name, UiKit.BodyFont, NameCp, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            Trough(unitPanel, "Health", HudLayout.HealthTrough, HudArt.Minium, out healthFill, HealthLines);
            manaTrough = Trough(unitPanel, "Mana", HudLayout.ManaTrough, HudArt.Azurite, out manaFill, ManaLines);
            kills = Words(unitPanel, "Kills", layout.KillsCount, UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.Ink, TextAnchor.MiddleCenter);
            killsLabel = null;
            if (layout.KillsLabel.width > 0)
            {
                killsLabel = Words(unitPanel, "KillsLabel", layout.KillsLabel, UiKit.BodyFont, 8, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleCenter);
                killsLabel.text = "kills";
                killsLabel.gameObject.SetActive(false);
            }
            status = FitWords(unitPanel, "Status", HudLayout.Status, UiKit.BodyFont, 12, 10, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            numbers = Words(unitPanel, "Numbers", layout.Numbers, UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            group = layout.Group.width > 0 ? FitWords(unitPanel, "Group", layout.Group, UiKit.BodyFont, 12, 10, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft) : null;
            rank = null;
            if (group != null)
            {
                rank = Words(unitPanel, "Rank", layout.Group, UiKit.UncialFont, 12, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleLeft);
                rank.gameObject.SetActive(false);
            }
            unitPanel.gameObject.SetActive(false);

            if (!layout.HasTarget) { targetPanel = null; return; }
            targetPanel = Put(strip, "Target", new Rect(0, 0, st.width, st.height));
            targetPip = Solid(targetPanel, "Owner", layout.TargetPip, Color.white);
            targetName = Words(targetPanel, "Name", layout.TargetName, UiKit.BodyFont, NameCp, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft);
            Trough(targetPanel, "Health", layout.TargetHealth, HudArt.Minium, out targetHealthFill, TargetHealthLines);
            targetManaTrough = Trough(targetPanel, "Mana", layout.TargetMana, HudArt.Azurite, out targetManaFill, TargetManaLines);
            targetShield = Tex(targetPanel, "Shield", layout.TargetShield, null);
            targetPanel.gameObject.SetActive(false);
        }

        const float NameCp = 15f;

        // A vellum-shade trough in an ink keyline, its fill 3 cp tall, and
        // its numbers in the help box while hovered.
        RectTransform Trough(Transform parent, string name, Rect r, Color fill, out Image fillImg, System.Func<(string, string)> lines)
        {
            var ink = Solid(parent, name, r, HudArt.Ink, true);
            Solid(ink.transform, "Ground", new Rect(Px, Px, r.width - 2 * Px, r.height - 2 * Px), HudArt.VellumShade);
            var f = Local(HudLayout.Fill(r), r);
            fillImg = Solid(ink.transform, "Fill", f, fill);
            // A lit top third, stretched along with the fill.
            var lit = Solid(fillImg.transform, "Lit", new Rect(0, 0, 0, f.height / 3f), new Color(1, 1, 1, 0.18f));
            lit.rectTransform.anchorMax = new Vector2(1, 1);
            Hover(ink.gameObject, lines);
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
            // A bigger picture where the map is drawn big.
            int want = layout.S > 2.2f ? 512 : 256;
            string texFor = minimapFor + ":" + want;
            if (mapTexFor != texFor)
            {
                mapTexFor = texFor;
                if (mapTex != null) Looks.Release(mapTex);
                mapTex = UiKit.ToTexture(root.Backend.MapPreview(minimapFor, want), true);
                if (mapTex == null)
                {
                    // No overview: a picture of the ground from its chunks.
                    var t = root.Backend.Terrain;
                    var chunks = new Dictionary<int, RgbaImage>();
                    RgbaImage Chunk(int c) => chunks.TryGetValue(c, out var i) ? i : chunks[c] = root.Backend.TerrainChunk(c);
                    mapTex = UiKit.ToTexture(TerrainView.WholeMap(t, Chunk, want), true);
                }
            }
            foreach (var o in mapPainted) Looks.Release(o);
            mapPainted.Clear();
            var size = root.Backend.Terrain.Size;
            var mr = layout.MapRect(size.x / Mathf.Max(1, size.y));

            // One dot texel per screen pixel, dots grown with the scale.
            if (dotTex != null) Looks.Release(dotTex);
            int w = Mathf.Clamp(Mathf.RoundToInt(mr.width * layout.S), 32, 1024), h = Mathf.Clamp(Mathf.RoundToInt(mr.height * layout.S), 32, 1024);
            dotTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            dotPx = new Color32[w * h];
            dotUnit = layout.DotUnit;
            dotBuilding = layout.DotBuilding;

            // Its own canvas, so the view box moving as the camera pans
            // redraws only this.
            if (mapParts != null) Looks.Release(mapParts.gameObject);
            mapParts = Put(content, "MapParts", new Rect(0, 0, layout.W, layout.H));
            mapParts.SetSiblingIndex(content.Find("Orders").GetSiblingIndex());
            var sub = mapParts.gameObject.AddComponent<Canvas>();
            sub.overridePixelPerfect = true;
            sub.pixelPerfect = false;
            mapParts.gameObject.AddComponent<GraphicRaycaster>();
            Solid(mapParts, "MapInk", Grow(mr, 1.5f + Px), HudArt.Ink);
            Solid(mapParts, "MapGold", Grow(mr, 1.5f), HudArt.Gold);
            Solid(mapParts, "MapGround", mr, HudArt.Purple);
            minimap = Tex(mapParts, "Minimap", mr, mapTex, true);
            minimap.gameObject.AddComponent<MinimapInput>().Clicked = OnMinimap;
            dots = UiKit.Rect(minimap.transform, "Units").Fill().gameObject.AddComponent<RawImage>();
            dots.texture = dotTex;
            dots.raycastTarget = false;
            // The view box in gold between ink lines, sliced so its lines stay 3 cp at any size.
            var boxTex = HudArt.ToTexture(HudArt.ViewBox(HudArt.Density(layout.S)));
            float perCp = boxTex.width / HudArt.ViewBoxCp, border = HudArt.ViewBoxBorderCp * perCp;
            var boxSprite = Sprite.Create(boxTex, new Rect(0, 0, boxTex.width, boxTex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            mapPainted.Add(boxTex);
            mapPainted.Add(boxSprite);
            var box = UiKit.Picture(minimap.transform, "View", boxSprite, Color.white, true);
            box.raycastTarget = false;
            box.pixelsPerUnitMultiplier = perCp;
            view = box.rectTransform;
            foreach (var corner in new[] { new Vector2(mr.xMin - 1f, mr.yMax + 1f), new Vector2(mr.xMax + 1f, mr.yMax + 1f) })
                BossAt(mapParts, corner, 7f, bossTex);
            var filler = layout.Filler(mr);
            var room = HudLayout.Roster(filler);
            rosterRows = null;
            if (room.width > 0) BuildRoster(filler, room);
            else if (filler.width > 0)
            {
                var t = HudArt.ToTexture(HudArt.FillerPanel(filler.width, filler.height, HudArt.Density(layout.S)));
                mapPainted.Add(t);
                Tex(mapParts, "Filler", filler, t);
            }
        }

        // ---- The roster under the minimap ----

        // On the HUD's own canvas, so the view box's canvas stays small.
        void BuildRoster(Rect filler, Rect room)
        {
            var frame = HudArt.ToTexture(HudArt.RosterFrame(filler.width, filler.height, HudArt.Density(layout.S)));
            mapPainted.Add(frame);
            var panel = Put(content, "Roster", filler);
            mapPainted.Add(panel.gameObject);
            panel.SetSiblingIndex(orders.GetSiblingIndex());
            Tex(panel, "Frame", new Rect(0, 0, filler.width, filler.height), frame);
            rosterRoom = new Rect(0, 0, room.width, room.height);
            rosterRows = Put(panel, "Rows", Local(room, filler));
            rosterKey = null;
            nextRoster = 0f;
        }

        // The selection's kinds, or one unit's description and record, or
        // with nothing selected the idle builders. Looked at twice a second
        // or when the selection changes, and made again only when it differs.
        void UpdateRoster(List<UnitState> chosen, bool changed)
        {
            if (rosterRows == null || (!changed && Time.unscaledTime < nextRoster)) return;
            nextRoster = Time.unscaledTime + 0.5f;
            var b = root.Backend;
            string key;
            int rk = 0, k = 0;
            if (chosen.Count == 0) { CountIdle(); key = "idle " + idle.Count; }
            else if (chosen.Count == 1)
            {
                var u = chosen[0];
                bool known = u.BuildProgress >= 1f && b.UnitRecord(u.Handle, out k, out rk);
                if (!known) rk = k = 0;
                if (u.Player != b.LocalPlayer) k = 0;
                key = $"one {u.Handle} {rk} {k}";
            }
            else key = $"many {SelectionHash(chosen)} {chosen.Count}";
            if (key == rosterKey) return;
            rosterKey = key;
            Clear(rosterRows);
            int rows = HudLayout.RosterRows(rosterRoom);
            if (chosen.Count == 0) IdleRows(rows);
            else if (chosen.Count == 1) OneUnit(chosen[0], rk, k, rows);
            else
            {
                var kinds = chosen.GroupBy(u => u.Def).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
                int shown = kinds.Count > rows ? rows - 1 : kinds.Count;
                for (int i = 0; i < shown; i++) KindRow(HudLayout.RosterRow(rosterRoom, i), kinds[i].Key, kinds[i].Select(u => u.Handle).ToArray());
                if (shown < kinds.Count)
                    Words(rosterRows, "More", HudLayout.RosterRow(rosterRoom, shown), UiKit.BodyFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft).text =
                        shown > 0 ? $"and {kinds.Count - shown} more kinds" : $"{kinds.Count} kinds";
            }
        }

        // A kind's picture at the build card's shape in a gold keyline, or purple without one.
        void RowPicture(Transform parent, Rect r, int def)
        {
            Solid(parent, "PictureInk", Grow(r, Px), HudArt.Ink);
            var t = BuildPicture(def);
            Tex(parent, "Picture", r, t).color = t != null ? Color.white : HudArt.Purple;
        }

        // A row for one kind, which narrows the selection to it.
        void KindRow(Rect row, int def, int[] handles)
        {
            var d = root.Backend.UnitDefs[def];
            var rt = Put(rosterRows, "Kind " + d.Name, row);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = UiKit.White;
            face.color = Color.clear;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = face;
            btn.onClick.AddListener(() => { UiKit.Play("menubutton.wav"); SelectOnly(handles); });
            var local = new Rect(0, 0, row.width, row.height);
            Solid(rt, "Rule", new Rect(0, row.height - Px, row.width, Px), new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.06f));
            RowPicture(rt, Local(HudLayout.RosterPicture(row), row), def);
            FitWords(rt, "Title", Local(HudLayout.RosterTitle(row), row), UiKit.BodyFont, 11, 8, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft).text = Nice(d);
            Words(rt, "Count", Local(HudLayout.RosterCount(row), row), UiKit.TitleFont, 11, HudLayout.NumberFloor, HudArt.Ink, TextAnchor.MiddleRight).text = handles.Length.ToString();
            Hover(rt.gameObject, () => (Nice(d), "Click selects only these"), on => { if (face) face.color = on ? new Color(HudArt.GoldHi.r, HudArt.GoldHi.g, HudArt.GoldHi.b, 0.13f) : Color.clear; });
        }

        // One unit: its kind, its description wrapped below, and its rank and kills last.
        void OneUnit(UnitState u, int rk, int k, int rows)
        {
            var d = root.Backend.UnitDefs[u.Def];
            var top = HudLayout.RosterRow(rosterRoom, 0);
            RowPicture(rosterRows, HudLayout.RosterPicture(top), u.Def);
            var title = top;
            title.xMin = HudLayout.RosterTitle(top).x;
            FitWords(rosterRows, "Title", title, UiKit.BodyFont, 11, 8, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft).text = Nice(d);
            string record = Record(rk, k), text = Describe(d, root.Backend.Sides);
            int last = rows - 1;
            if (record != "" && last >= 1)
            {
                Words(rosterRows, "Record", HudLayout.RosterRow(rosterRoom, last), UiKit.BodyFont, 11, HudLayout.BodyFloor, HudArt.MiniumDeep, TextAnchor.MiddleLeft).text = record;
                last--;
            }
            if (text != "" && last >= 1)
            {
                var r = HudLayout.RosterRow(rosterRoom, 1);
                r.yMax = HudLayout.RosterRow(rosterRoom, last).yMax;
                var t = FitWords(rosterRows, "Description", r, UiKit.BodyFont, 11, 8, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.UpperLeft);
                t.text = text;
            }
        }

        // How many builders stand idle, and a plate that finds the next.
        void IdleRows(int rows)
        {
            var top = HudLayout.RosterRow(rosterRoom, 0);
            if (idle.Count == 0)
            {
                Words(rosterRows, "Idle", top, UiKit.BodyFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft).text = "No idle builders";
                return;
            }
            var where = rows >= 2 ? HudLayout.RosterRow(rosterRoom, 1) : top;
            if (rows >= 2) Words(rosterRows, "Idle", top, UiKit.BodyFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleLeft).text = $"Idle builders: {idle.Count}";
            var plate = Plate(rosterRows, "Next idle", new Rect(where.x, where.y + 1f, Mathf.Min(where.width, 84f), where.height - 2f), rows >= 2 ? "Select next" : $"Idle builders: {idle.Count}", NextIdle);
            if (rows < 2) plate.GetComponentInChildren<Text>().name = "Idle";
            Hover(plate.gameObject, () => ("Idle builders", "Selects the next and looks at it"));
        }

        // A small plate in the dialogs' manner: vellum shade in a gold bezel and an ink keyline.
        Button Plate(Transform parent, string name, Rect r, string label, System.Action click)
        {
            var ink = Solid(parent, name, r, HudArt.Ink, true);
            var local = new Rect(0, 0, r.width, r.height);
            var lift = Put(ink.transform, "Plate", local);
            var bezel = Solid(lift, "Bezel", Grow(local, -Px), HudArt.Gold);
            Solid(lift, "Face", Grow(local, -Px - 1f), HudArt.VellumShade);
            Words(lift, "Label", local, UiKit.BodyFont, 11, HudLayout.BodyFloor, HudArt.Ink, TextAnchor.MiddleCenter).text = label;
            var btn = ink.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = ink;
            btn.onClick.AddListener(() => click());
            bool hovered = false;
            ink.gameObject.AddComponent<PressHint>().Pressed = down =>
            {
                if (!lift) return;
                lift.anchoredPosition = down ? new Vector2(1, -1) : Vector2.zero;
                bezel.color = down ? HudArt.Pressed : hovered ? HudArt.GoldHi : HudArt.Gold;
            };
            ink.gameObject.AddComponent<HoverHint>().Show = on => { hovered = on; if (bezel) bezel.color = on ? HudArt.GoldHi : HudArt.Gold; };
            return btn;
        }

        // Own builders standing idle: not buildings, something to build,
        // finished and with no order. At most 512 are asked for their order.
        void CountIdle()
        {
            idle.Clear();
            var b = root.Backend;
            var ents = root.World.Entities;
            int asked = 0;
            for (int i = 0; i < ents.UnitCount && asked < 512; i++)
            {
                var u = ents.Units[i];
                if (u.Player != b.LocalPlayer || (u.Flags & UnitFlags.Dying) != 0 || u.BuildProgress < 1f) continue;
                if (u.Def < 0 || u.Def >= b.UnitDefs.Count) continue;
                var d = b.UnitDefs[u.Def];
                if (d.IsBuilding || d.BuildOptions.Length == 0) continue;
                asked++;
                if (b.ReadOrder(u.Handle).Kind == OrderKind.None) idle.Add(u.Handle);
            }
            idle.Sort();
        }

        // The idle builder after the last one picked, selected and looked at.
        void NextIdle()
        {
            CountIdle();
            if (idle.Count == 0) return;
            int i = idle.FindIndex(h => h > lastIdle);
            lastIdle = idle[i < 0 ? 0 : i];
            UiKit.Play("menubutton.wav");
            SelectOnly(new[] { lastIdle });
            var ents = root.World.Entities;
            for (int j = 0; j < ents.UnitCount; j++)
                if (ents.Units[j].Handle == lastIdle)
                {
                    var at = ents.Units[j].Position;
                    root.World.Camera.focus = new Vector3(at.x, root.Backend.GroundHeight(at.x, at.z), at.z);
                    break;
                }
        }

        // As the input does: the engine's selection and the HUD's together.
        void SelectOnly(int[] handles)
        {
            root.Backend.Select(handles, false);
            var sel = root.World.Entities.Selected;
            sel.Clear();
            sel.UnionWith(handles);
            nextPanel = 0f;
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
                int dd = b.UnitDefs[u.Def].IsBuilding ? dotBuilding : dotUnit;
                for (int dy = 0; dy < dd; dy++)
                    for (int dx = 0; dx < dd; dx++)
                    {
                        int px = x - dd / 2 + dx, py = y - dd / 2 + dy;
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

        // Classic, as the original: a left click sends an own selection
        // there and otherwise looks, and the right button looks. Modern:
        // the left looks and the right sends.
        void OnMinimap(Vector2 uv, PointerEventData.InputButton button, bool drag)
        {
            var world = root.World;
            if (world == null) return;
            var size = root.Backend.Terrain.Size;
            var at = new Vector3(uv.x * size.x, 0, (uv.y - 1f) * size.y);
            at.y = root.Backend.GroundHeight(at.x, at.z);
            var o = root.Orders;
            bool left = button == PointerEventData.InputButton.Left, right = button == PointerEventData.InputButton.Right;
            bool send = o == null || o.Classic ? left && OwnSelection() : right;
            if (send)
            {
                if (!drag && o != null) o.MoveBlock(CommandKind.Move, at);
                return;
            }
            if (left || right) world.Camera.focus = at;
        }

        bool OwnSelection()
        {
            var ents = root.World.Entities;
            int me = root.Backend.LocalPlayer;
            for (int i = 0; i < ents.UnitCount; i++)
                if (ents.Units[i].Player == me && ents.Selected.Contains(ents.Units[i].Handle)) return true;
            return false;
        }

        // ---- The pool and the help box ----

        Economy pool;

        void UpdatePool()
        {
            var b = root.Backend;
            pool = b.ReadEconomy(b.LocalPlayer);
            float fill = pool.Storage > 0 ? Mathf.Clamp01(pool.Mana / pool.Storage) : 0f;
            float level = HudArt.BallLiquidCp(fill);
            if (Mathf.Abs(level - ballLevel) > 0.05f)
            {
                ballLevel = level;
                ballLiquid.rectTransform.sizeDelta = new Vector2(0, level);
                ballLiquid.uvRect = new Rect(0, 0, 1, level / HudArt.BallCp);
            }
            string inc = "+" + Mathf.RoundToInt(pool.Income), exp = "-" + Mathf.RoundToInt(pool.Expense);
            if (income.text != inc) income.text = inc;
            if (spend.text != exp) spend.text = exp;
            foreach (var c in cards) Afford(c, c.Cost <= pool.Mana);
            UpdateHelp();
        }

        (string, string) PoolLines()
        {
            int max = Mathf.FloorToInt(pool.Storage);
            return ("Mana", $"{Mathf.Min(Mathf.FloorToInt(pool.Mana), max)}/{max}");
        }

        UnitState shownUnit, targetUnit;
        int shownCount;
        long groupHp, groupMax;

        (string, string) HealthLines() =>
            shownCount == 1 ? ("Health", $"{shownUnit.Health}/{shownUnit.MaxHealth}")
            : shownCount > 1 ? ("Health", $"{groupHp}/{groupMax}") : ("Health", "");
        (string, string) ManaLines() => shownCount == 1 ? ("Mana", $"{shownUnit.Mana}/{shownUnit.MaxMana}") : PoolLines();
        (string, string) TargetHealthLines() => ("Health", $"{targetUnit.Health}/{targetUnit.MaxHealth}");
        (string, string) TargetManaLines() => ("Mana", $"{targetUnit.Mana}/{targetUnit.MaxMana}");

        // Short display words keep the uncial, anything read at a glance the book hand.
        static bool Rubric(string line) => line == "Mana" || line == "Menu";

        static string ArmedName(CommandKind k) =>
            k == CommandKind.Repair ? "Heal" : k == CommandKind.Reclaim ? "Clear" : k.ToString();

        // The hovered control's label and detail, a placement's keys, the
        // armed order, or the pool (legacy:152100-152110).
        void UpdateHelp()
        {
            if (help1 == null) return;
            (string, string) lines;
            var o = root.Orders;
            if (hoverLines != null) lines = hoverLines();
            else if (o != null && o.Armed == CommandKind.Build && o.ArmedDef >= 0)
                lines = root.Backend.CanRotate(o.ArmedDef) ? ("R or ] turns it", "Shift R or [ turns it back") : ("Placing", "It cannot be turned");
            else if (o != null && o.ArmedAction != null) lines = (o.ArmedAction.Label, "Right click cancels");
            else if (o != null && o.Armed != null) lines = (ArmedName(o.Armed.Value), "Right click cancels");
            else lines = PoolLines();
            var font = Rubric(lines.Item1) ? UiKit.UncialFont : UiKit.BodyFont;
            if (help1.font != font) help1.font = font;
            if (help1.text != lines.Item1) help1.text = lines.Item1;
            if (help2.text != lines.Item2) help2.text = lines.Item2;
            // A refusal in minium, lightened to read on the purple.
            var tone = lines.Item2 == NotEnoughMana ? HudArt.MiniumPale : HudArt.Silver;
            if (help2.color != tone) help2.color = tone;
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
            int max = layout.Font(NameCp, HudLayout.BodyFloor), min = Mathf.Min(max, layout.Font(10, HudLayout.BodyFloor));
            for (int size = max; size >= min; size--)
            {
                t.fontSize = size;
                t.text = Versal(name, size);
                if (t.preferredWidth <= width) break;
            }
        }

        static string Versal(string name, int size) =>
            string.IsNullOrEmpty(name) ? "" : $"<size={Mathf.RoundToInt(size * 1.3f)}><color={Colour(HudArt.Azurite)}>{name.Substring(0, 1)}</color></size>{name.Substring(1)}";

        void UpdatePanel(WorldView world)
        {
            var b = root.Backend;
            var sel = world.Entities.Selected;
            int n = world.Entities.UnitCount;
            var chosen = new List<UnitState>();
            for (int i = 0; i < n; i++)
                if (sel.Contains(world.Entities.Units[i].Handle)) chosen.Add(world.Entities.Units[i]);
            firstSelected = chosen.Count > 0 ? chosen[0].Handle : -1;
            shownCount = chosen.Count;
            if (shownCount == 1) shownUnit = chosen[0];
            unitPanel.gameObject.SetActive(chosen.Count > 0);
            if (chosen.Count == 1)
            {
                var u = chosen[0];
                var def = b.UnitDefs[u.Def];
                // A frame shows nothing but how far it is built.
                bool frame = u.BuildProgress < 1f;
                bool own = u.Player == b.LocalPlayer;
                ShowName(nameText, Nice(def), ref nameShown);
                SetPortrait(u.Def);
                SetFill(healthFill, u.MaxHealth > 0 ? (float)u.Health / u.MaxHealth : 1f);
                bool caster = own && !frame && u.MaxMana > 0;
                manaTrough.gameObject.SetActive(caster);
                if (caster) SetFill(manaFill, (float)u.Mana / u.MaxMana);
                numbers.text = own && !frame
                    ? $"<color={Colour(HudArt.MiniumDeep)}>{u.Health}/{u.MaxHealth}</color>" + (caster ? $"  <color={Colour(HudArt.AzuriteDeep)}>{u.Mana}/{u.MaxMana}</color>" : "")
                    : "";
                status.text = !own ? "" : u.BuildProgress < 1f ? $"Being built, {u.BuildProgress * 100:0}%" : OrderText(b.ReadOrder(u.Handle));
                bool known = b.UnitRecord(u.Handle, out int k, out int rk);
                known &= !frame;
                kills.text = own && known && k > 0 ? k.ToString() : "";
                if (killsLabel != null && killsLabel.gameObject.activeSelf != (kills.text != "")) killsLabel.gameObject.SetActive(kills.text != "");
                shield.enabled = known;
                if (known) shield.texture = shields[Mathf.Clamp(rk, 0, 2)];
                if (group != null) ShowDescription(def, known ? rk : 0);
            }
            else if (chosen.Count > 1)
            {
                ShowName(nameText, chosen.Count + " units", ref nameShown);
                var common = chosen.GroupBy(u => u.Def).OrderByDescending(g => g.Count()).First().Key;
                SetPortrait(common);
                long hp = 0, max = 0;
                foreach (var u in chosen) { hp += u.Health; max += u.MaxHealth; }
                groupHp = hp;
                groupMax = max;
                SetFill(healthFill, max > 0 ? (float)hp / max : 1f);
                manaTrough.gameObject.SetActive(false);
                bool own = chosen.All(u => u.Player == b.LocalPlayer);
                numbers.text = own ? $"<color={Colour(HudArt.MiniumDeep)}>{hp}/{max}</color>" : "";
                string makeUp = string.Join(", ", chosen.GroupBy(u => u.Def).Select(g => $"{g.Count()} {Nice(b.UnitDefs[g.Key])}"));
                if (group != null) { ShowDescription(null, 0); group.text = makeUp; status.text = ""; }
                else status.text = makeUp;
                kills.text = "";
                if (killsLabel != null && killsLabel.gameObject.activeSelf) killsLabel.gameObject.SetActive(false);
                shield.enabled = false;
            }
            RefreshButtons(chosen);
            int selection = SelectionHash(chosen) * 31 + chosen.Count;
            UpdateRoster(chosen, selection != lastSelection);
            lastSelection = selection;
        }

        // One unit's description in the strip's spare width, after its rank
        // word in minium uncial; for several units the width is the group's.
        void ShowDescription(UnitDef def, int rk)
        {
            string word = def != null ? RankWord(rk) : "";
            if (rank.gameObject.activeSelf != (word != "")) rank.gameObject.SetActive(word != "");
            float left = 0f;
            if (word != "")
            {
                rank.text = word + ".";
                left = rank.preferredWidth + 3f;
            }
            var g = layout.Group;
            Move(group.rectTransform, new Rect(g.x + left, g.y, Mathf.Max(0f, g.width - left), g.height));
            group.text = def != null ? Describe(def, root.Backend.Sides) : "";
        }

        public static string RankWord(int rank) => rank >= 2 ? "Champion" : rank == 1 ? "Veteran" : "";

        // "Veteran, 12 kills", either part alone, or nothing.
        public static string Record(int rank, int kills)
        {
            string word = RankWord(rank), count = kills == 1 ? "1 kill" : kills > 1 ? kills + " kills" : "";
            return word != "" && count != "" ? word + ", " + count : word + count;
        }

        // What the game's description says when it tells whose a unit is, not what.
        static readonly string[] Owners = { "NPC", "Mission", "Animal", "Monster" };

        // What a unit is. The game's description mostly names its kingdom, and
        // then the category says it without its side token: "ARA MELEE ATTACK"
        // is "Melee attack", and "ARA Monarch" is "Monarch of Aramon".
        public static string Describe(UnitDef d, IReadOnlyList<SideInfo> sides)
        {
            string text = (d.Description ?? "").Trim();
            var side = SideNamed(text, sides);
            if (text != "" && side == null && !Owners.Any(o => string.Equals(o, text, System.StringComparison.OrdinalIgnoreCase))) return text;
            var words = (d.Category ?? "").Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return "";
            if (string.Equals(words[1], "Monarch", System.StringComparison.OrdinalIgnoreCase))
            {
                side = side ?? SideNamed(d.Side, sides);
                return side != null ? "Monarch of " + side.Name : "Monarch";
            }
            string rest = string.Join(" ", words, 1, words.Length - 1).ToLowerInvariant();
            return char.ToUpperInvariant(rest[0]) + rest.Substring(1);
        }

        static SideInfo SideNamed(string word, IReadOnlyList<SideInfo> sides) =>
            string.IsNullOrEmpty(word) || sides == null ? null
            : sides.FirstOrDefault(sd => string.Equals(sd.Name, word, System.StringComparison.OrdinalIgnoreCase) || string.Equals(sd.Id, word, System.StringComparison.OrdinalIgnoreCase));

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
            targetUnit = found;
            var def = b.UnitDefs[found.Def];
            var owner = b.PlayerById(found.Player);
            targetPip.color = owner != null ? (Color)owner.Tint : Color.grey;
            ShowName(targetName, Nice(def), ref targetNameShown);
            SetFill(targetHealthFill, found.MaxHealth > 0 ? (float)found.Health / found.MaxHealth : 1f);
            targetManaTrough.gameObject.SetActive(found.MaxMana > 0);
            if (found.MaxMana > 0) SetFill(targetManaFill, (float)found.Mana / found.MaxMana);
            bool known = b.UnitRecord(found.Handle, out _, out int rk);
            targetShield.enabled = known;
            if (known) targetShield.texture = shields[Mathf.Clamp(rk, 0, 2)];
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

        // One order button's parts, changed in place as its state moves, so
        // a press is never lost to a rebuild.
        sealed class ActionParts
        {
            public UnitAction A;
            public Button Button;
            public RectTransform Plate;
            public RawImage Picture;
            public Image Bezel, Wash;
            public ColourEase Ease;
            public Image[] Armed, Chosen;
            public bool Hovered, Pressed;
        }

        static int SelectionHash(List<UnitState> chosen)
        {
            unchecked
            {
                int h = 17;
                foreach (var u in chosen) h = h * 31 + u.Handle;
                return h;
            }
        }

        // Rebuilds the buttons when the selection, its list of actions or
        // the armed building changes, and refreshes their state and the
        // queue counts every time.
        void RefreshButtons(List<UnitState> chosen)
        {
            var b = root.Backend;
            bool mine = chosen.Count > 0 && chosen.All(u => u.Player == b.LocalPlayer);
            // Frames take no orders, only a queue.
            bool frames = chosen.All(u => u.BuildProgress < 1f);
            actions = mine && !frames ? b.SelectionActions() : System.Array.Empty<UnitAction>();
            if (root.Orders != null) root.Orders.Actions = actions;
            basicOrders = mine && !frames && actions.Length == 0 && chosen.Any(u => !b.UnitDefs[u.Def].IsBuilding);
            if (basicOrders) actions = BasicOrders();
            var o = root.Orders;
            int armedBuild = o != null && o.Armed == CommandKind.Build ? o.ArmedDef : -1;
            string key = mine ? SelectionHash(chosen) + ":" + chosen.Count + "|" + string.Join(",", actions.Select(a => a.Id)) + "|" + buildPage + "|" + armedBuild : "";
            if (key != gridKey)
            {
                gridKey = key;
                Clear(orders);
                Clear(builds);
                queueBadges.Clear();
                parts.Clear();
                cards.Clear();
                bool centreUsed = false;
                if (mine)
                {
                    var placed = HudLayout.PlaceActions(actions.Select(a => a.Id).ToList());
                    foreach (var a in actions)
                        if (placed.TryGetValue(a.Id, out var slot)) { ActionButton(a, slot); centreUsed |= slot.StartsWith("C"); }
                    PlaceBuilds(chosen);
                }
                plait.gameObject.SetActive(!centreUsed);
            }
            foreach (var a in actions)
                if (parts.TryGetValue(a.Id, out var p)) Refresh(p, a);
            foreach (var qb in queueBadges)
            {
                if (qb.factories == null || qb.badge == null) continue;
                int q = 0;
                bool repeats = false;
                foreach (int f in qb.factories) { q += b.QueuedCount(f, qb.def); repeats |= b.RepeatOf(f) == qb.def; }
                string t = BadgeText(q, repeats);
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

        // A letter that pans the camera alone takes Ctrl.
        static bool WithCtrl(string hotkey) => hotkey.Length == 1 && "WASD".IndexOf(char.ToUpperInvariant(hotkey[0])) >= 0;

        // The key as the help box spells it.
        public static string KeyName(string hotkey) => (WithCtrl(hotkey) ? "Ctrl " : "") + hotkey.ToUpperInvariant();

        // The badge on a button: one glyph, a caret before it for Ctrl.
        public static string Badge(string hotkey) => (WithCtrl(hotkey) ? "^" : "") + hotkey.ToUpperInvariant();

        // The cost under a spell, or a dash where it costs nothing, so the gutter stays even.
        public static string SpellCost(int cost) => cost > 0 ? cost.ToString() : "\u2013";

        static string Detail(UnitAction a) =>
            !a.Enabled && !string.IsNullOrEmpty(a.Why) ? a.Why
            : a.ManaCost > 0 ? a.ManaCost + " mana"
            : !string.IsNullOrEmpty(a.Hotkey) ? KeyName(a.Hotkey) : "";

        static Color Rest(UnitAction a) => a.Enabled ? HudArt.Gold : HudArt.VellumEdge;

        void ActionButton(UnitAction a, string slot)
        {
            var pic = HudLayout.Slot(slot);
            var hit = HudLayout.Hit(pic);
            var rt = Put(orders, "Action " + a.Id, hit);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = UiKit.White;
            face.color = Color.clear;
            var p = new ActionParts { A = a };
            p.Button = rt.gameObject.AddComponent<Button>();
            p.Button.transition = Selectable.Transition.None;
            p.Button.targetGraphic = face;
            p.Button.onClick.AddListener(() => Pressed(p.A));
            var local = Local(pic, hit);
            // The picture and its bezel and rings, which go down together when pressed.
            p.Plate = Put(rt, "Plate", new Rect(0, 0, hit.width, hit.height));
            Solid(p.Plate, "Keyline", Grow(local, 1f + Px), HudArt.Ink);
            p.Bezel = Solid(p.Plate, "Bezel", Grow(local, 1f), Rest(a));
            p.Ease = ColourEase.On(p.Bezel.gameObject, Rest(a), p.Bezel);
            var tex = ActionPictureFor(a.Picture, a.Label);
            if (tex != null) p.Picture = Tex(p.Plate, "Picture", local, tex);
            else
            {
                Solid(p.Plate, "Face", local, HudArt.VellumShade);
                FitWords(p.Plate, "Label", Grow(local, -1.5f), UiKit.BodyFont, 9, 6, HudLayout.BadgeFloor, HudArt.Ink, TextAnchor.MiddleCenter).text = a.Label;
            }
            p.Wash = Solid(p.Plate, "Wash", local, new Color(HudArt.Vellum.r, HudArt.Vellum.g, HudArt.Vellum.b, tex != null ? 0.5f : 0.35f));
            // A chosen stance or weapon in azurite, an order waiting for its target in minium.
            p.Chosen = Ring(p.Plate, "Chosen", Grow(local, 1.5f), 1.5f, HudArt.Azurite);
            p.Armed = Ring(p.Plate, "Armed", Grow(local, 1.5f), 1.5f, HudArt.Minium);
            if (builtBadges && !string.IsNullOrEmpty(a.Hotkey)) KeyTab(rt, Local(layout.Badge(slot), hit), a.Hotkey);
            bool spell = slot == "W1" || slot == "W2" || slot == "W3";
            if (spell || (a.ManaCost > 0 && slot.StartsWith("W")))
            {
                var cost = Words(rt, "Cost", new Rect(local.x - 2f, HudLayout.GutterY - hit.y, local.width + 4f, HudLayout.GutterH), UiKit.TitleFont, 9, HudLayout.NumberFloor, HudArt.AzuriteDeep, TextAnchor.MiddleCenter);
                cost.fontStyle = FontStyle.Bold;
                cost.text = SpellCost(a.ManaCost);
            }
            Hover(rt.gameObject, () => (p.A.Label, Detail(p.A)), on =>
            {
                p.Hovered = on;
                if (p.Plate) Look(p, false);
            });
            rt.gameObject.AddComponent<PressHint>().Pressed = down =>
            {
                if (!p.Plate || (down && !p.Button.interactable)) return;
                p.Pressed = down;
                Look(p, true);
            };
            parts[a.Id] = p;
        }

        // Down and right by 1 cp, the button's pressed place.
        static Rect Nudge(Rect r) => new Rect(r.x + 1f, r.y + 1f, r.width, r.height);

        // Pressed: down by 1 cp with the bezel in shadow, at once. Hovered:
        // the bezel lit, over 0.1 s. Else at rest.
        void Look(ActionParts p, bool now)
        {
            p.Plate.anchoredPosition = p.Pressed ? new Vector2(1f, -1f) : Vector2.zero;
            var c = p.Pressed ? HudArt.Pressed : p.Hovered ? HudArt.GoldHi : Rest(p.A);
            if (now) p.Ease.Snap(c);
            else p.Ease.To(c);
        }

        // The key letter on a vellum tab in an ink keyline, in the button's
        // corner, bold only from 14 px where strokes have room. A Ctrl chord's
        // caret is smaller, in minium, so the letter keeps the room.
        void KeyTab(Transform parent, Rect r, string hotkey)
        {
            Solid(parent, "KeyInk", r, HudArt.Ink);
            Solid(parent, "KeyTab", Grow(r, -Px), HudArt.Vellum);
            var t = Words(parent, "Key", r, UiKit.BodyFont, layout.BadgeEm, HudLayout.BadgeFloor, HudArt.Ink, TextAnchor.MiddleCenter);
            t.fontStyle = t.fontSize * layout.S >= 14f ? FontStyle.Bold : FontStyle.Normal;
            string glyph = Badge(hotkey);
            t.text = glyph.StartsWith("^")
                ? $"<size={Mathf.RoundToInt(t.fontSize * 0.6f)}><color={Colour(HudArt.Minium)}>^</color></size>{glyph.Substring(1)}"
                : glyph;
        }

        void Refresh(ActionParts p, UnitAction a)
        {
            p.A = a;
            if (p.Button.interactable != a.Enabled) p.Button.interactable = a.Enabled;
            bool armed = IsArmed(a);
            if (p.Picture != null)
            {
                int picture = armed && a.Picture >= 0 ? a.Picture / 4 * 4 + 1 : a.Picture;
                var tex = ActionPictureFor(picture, a.Label);
                if (tex != null && p.Picture.texture != tex) p.Picture.texture = tex;
            }
            // The game's own disabled picture is faded already.
            bool wash = !a.Enabled && !(p.Picture != null && a.Picture % 4 == 0);
            if (p.Wash.enabled != wash) p.Wash.enabled = wash;
            Show(p.Armed, armed);
            Show(p.Chosen, a.Toggled && !armed);
            if (!p.Hovered && !p.Pressed) p.Ease.To(Rest(a));
        }

        // A button with nothing to aim at acts at once, and so does a spell,
        // which only chooses the weapon as the original's buttons do. The
        // rest wait for a click or a drag in the world.
        void Pressed(UnitAction a)
        {
            UiKit.Play(OrderSound(a));
            var orders = root.Orders;
            nextPanel = 0f;
            if (basicOrders)
            {
                if (a.Command == CommandKind.Stop) orders?.Stop();
                else orders?.Arm(a.Command);
                return;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (a.Target == ActionTarget.None) { root.Backend.DoAction(a.Id, Vector3.zero, -1, default, shift); return; }
            if (a.Kind == ActionKind.Spell) { root.Backend.DoAction(a.Id, Vector3.zero, -1, default, false); return; }
            orders?.ArmAction(a);
        }

        // ---- The build row ----

        // The list of the first selected unit that builds, as the original
        // shows the lead unit's, for a mixed selection too.
        void PlaceBuilds(List<UnitState> chosen)
        {
            var b = root.Backend;
            int lead = chosen.FindIndex(u => b.UnitDefs[u.Def].BuildOptions.Length > 0);
            if (lead < 0) return;
            var leadUnit = chosen[lead];
            var def = b.UnitDefs[leadUnit.Def];
            var options = def.BuildOptions.Where(o => o >= 0 && o < b.UnitDefs.Count).ToList();
            if (options.Count == 0) return;
            int defsKey = leadUnit.Def + 1;
            if (defsKey != lastDefsKey) { lastDefsKey = defsKey; buildPage = 0; }
            var g = layout.Builds(options.Count);
            int pages = g.Paged ? (options.Count + g.PerPage - 1) / g.PerPage : 1;
            buildPage = Mathf.Clamp(buildPage, 0, pages - 1);
            // Every selected factory of the lead's kind takes the queue.
            var factories = def.IsBuilding ? chosen.Where(u => u.Def == leadUnit.Def).Select(u => u.Handle).ToList() : null;
            var shown = options.Skip(buildPage * g.PerPage).Take(g.PerPage).ToList();
            for (int i = 0; i < shown.Count; i++) BuildCard(layout.BuildCell(g, i), b.UnitDefs[shown[i]], factories);
            if (g.Paged) PageTurner(layout.BuildCell(g, shown.Count), pages);
        }

        // A build card's parts that change with the pool and the pointer.
        sealed class CardParts
        {
            public int Cost;
            public Vector2 Size;
            public RectTransform Plate;
            public Image Wash;
            public Text CostText;
            public ColourEase Ease;
            public bool Afford = true, Hovered, Pressed;
        }

        public const string NotEnoughMana = "Not enough mana";

        // A build card's lines in the help box: its name and cost, then what
        // the clicks do, or that the pool cannot pay for it.
        public static (string, string) CardLines(string name, int cost, bool factory, bool canRotate, bool afford) =>
            ($"{name}, {cost} mana", !afford ? NotEnoughMana
                : factory ? "Shift 5, Ctrl repeat, right click removes"
                : canRotate ? "Click to place, R turns it" : "Click to place");

        void BuildCard(Rect cell, UnitDef od, List<int> factories)
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
            var c = new CardParts { Cost = od.ManaCost, Size = cell.size };
            // The picture, its wash and its bezel, which go in by 1 cp when pressed.
            c.Plate = Put(rt, "Plate", local);
            var pic = BuildPicture(id);
            if (pic != null)
            {
                var picture = UiKit.Rect(c.Plate, "Picture").Fill().gameObject.AddComponent<RawImage>();
                picture.texture = pic;
                picture.raycastTarget = false;
            }
            else FitWords(c.Plate, "Name", Grow(local, -4f), UiKit.BodyFont, 11, 6, HudLayout.BodyFloor, HudArt.Silver, TextAnchor.MiddleCenter).text = Nice(od);
            var wash = UiKit.Rect(c.Plate, "Wash").Fill().gameObject.AddComponent<Image>();
            wash.sprite = UiKit.White;
            wash.color = HudArt.Unaffordable;
            wash.raycastTarget = false;
            c.Wash = wash;
            var bezel = EdgeRing(c.Plate, "Bezel", Px, 1.5f, HudArt.Gold);
            c.Ease = ColourEase.On(c.Plate.gameObject, HudArt.Gold, bezel);
            Ring(rt, "Keyline", local, Px, HudArt.Ink);
            var o = root.Orders;
            if (o != null && o.Armed == CommandKind.Build && o.ArmedDef == id) Ring(rt, "Armed", local, 2f, HudArt.Minium);
            Text badge = null;
            if (factories != null)
            {
                badge = Words(rt, "Queued", new Rect(3f + 1.5f, 2f + 1.5f, cell.width - 8f, 16f), UiKit.TitleFont, 13, HudLayout.NumberFloor, HudArt.GoldHi, TextAnchor.UpperLeft);
                var outline = badge.gameObject.AddComponent<Outline>();
                outline.effectColor = HudArt.Ink;
                outline.effectDistance = new Vector2(Mathf.Max(0.5f, Px), -Mathf.Max(0.5f, Px));
            }
            c.CostText = Tab(rt, "Cost", od.ManaCost.ToString(), 9, HudLayout.NumberFloor, HudArt.AzuriteDeep, size => new Rect(cell.width - size.x - 2.5f, cell.height - size.y - 2.5f, size.x, size.y), UiKit.TitleFont);
            Afford(c, od.ManaCost <= pool.Mana);
            bool rotates = factories == null && root.Backend.CanRotate(id);
            Hover(rt.gameObject, () => CardLines(Nice(od), od.ManaCost, factories != null, rotates, c.Afford), on => { c.Hovered = on; if (c.Plate) Look(c, false); });
            var press = rt.gameObject.AddComponent<PressHint>();
            press.AnyButton = true;
            press.Pressed = down => { c.Pressed = down; if (c.Plate) Look(c, true); };
            cards.Add(c);
            if (factories != null)
            {
                btn.onClick.AddListener(() => BuildClicked(factories, id, false));
                rt.gameObject.AddComponent<RightClick>().Clicked = () => BuildClicked(factories, id, true);
            }
            else btn.onClick.AddListener(() => { UiKit.Play("menubutton.wav"); root.Orders?.Arm(CommandKind.Build, id); nextPanel = 0f; });
            queueBadges.Add((badge, id, factories));
        }

        // In by 1 cp at the top left with the bezel in shadow while pressed,
        // the bezel lit while hovered.
        static void Look(CardParts c, bool now)
        {
            c.Plate.anchoredPosition = c.Pressed ? new Vector2(1f, -1f) : Vector2.zero;
            c.Plate.sizeDelta = c.Pressed ? c.Size - Vector2.one : c.Size;
            var colour = c.Pressed ? HudArt.Pressed : c.Hovered ? HudArt.GoldHi : HudArt.Gold;
            if (now) c.Ease.Snap(colour);
            else c.Ease.To(colour);
        }

        // A card the pool cannot pay for: its cost in minium and its picture
        // washed. It stays clickable, and the engine refuses and says why.
        static void Afford(CardParts c, bool afford)
        {
            c.Afford = afford;
            if (c.Wash.enabled == afford) c.Wash.enabled = !afford;
            var colour = afford ? HudArt.AzuriteDeep : HudArt.Minium;
            if (c.CostText.color != colour) c.CostText.color = colour;
        }

        // Shift and Ctrl as a build click sees them, which tests replace.
        public static System.Func<bool> ShiftKey = () => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static System.Func<bool> CtrlKey = () => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // The count on a factory's build card, or the original's +++ for
        // one it makes over and over.
        public static string BadgeText(int queued, bool repeats) => repeats ? "+++" : queued > 0 ? queued.ToString() : "";

        // The original's build card: a left click queues one, five with
        // Shift, and Ctrl makes the factory repeat it. A right click takes
        // one off, five with Shift, or stops a repeat.
        void BuildClicked(List<int> factories, int def, bool right)
        {
            var b = root.Backend;
            bool repeating = factories.Any(f => b.RepeatOf(f) == def);
            int n = ShiftKey() ? 5 : 1;
            if (!right && CtrlKey())
                foreach (int f in factories) b.SetRepeat(f, def, !repeating);
            else if (right && repeating)
                foreach (int f in factories) b.SetRepeat(f, def, false);
            else
                foreach (int f in factories) b.AddToQueue(f, def, right ? -n : n);
            if (!UiKit.Play(right ? "subbuild" : "addbuild")) UiKit.Play("menubutton.wav");
            nextPanel = 0f;
        }

        // The sound the original's .gui gives each order button.
        static string OrderSound(UnitAction a)
        {
            if (a.Kind != ActionKind.Order) return "menubutton.wav";
            switch (a.Command)
            {
                case CommandKind.Move: return "move.wav";
                case CommandKind.Attack: return "attack.wav";
                case CommandKind.Stop: return "stop.wav";
                case CommandKind.Patrol: return "patrol.wav";
                case CommandKind.Guard: return "guard.wav";
                default: return "menubutton.wav";
            }
        }

        // Only past what the original could ever show on one screen.
        void PageTurner(Rect cell, int pages)
        {
            var rt = Put(builds, "More", cell);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = UiKit.White;
            face.color = HudArt.Vellum;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.onClick.AddListener(() => { UiKit.Play("menubutton.wav"); buildPage = (buildPage + 1) % pages; nextPanel = 0f; });
            Ring(rt, "Keyline", new Rect(0, 0, cell.width, cell.height), 1.5f, HudArt.Gold);
            Words(rt, "Label", new Rect(0, 0, cell.width, cell.height), UiKit.UncialFont, 12, HudLayout.BodyFloor, HudArt.Minium, TextAnchor.MiddleCenter).text = $"{buildPage + 1} of {pages}";
        }

        public void Dispose()
        {
            ResetViewport();
            if (canvas) Looks.Release(canvas.gameObject);
            foreach (var o in painted) Looks.Release(o);
            painted.Clear();
            foreach (var o in mapPainted) Looks.Release(o);
            mapPainted.Clear();
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

    // A press or a drag on the minimap, as a point from its bottom left.
    public sealed class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public System.Action<Vector2, PointerEventData.InputButton, bool> Clicked;

        public void OnPointerDown(PointerEventData e) => Send(e, false);
        public void OnDrag(PointerEventData e) => Send(e, true);

        void Send(PointerEventData e, bool drag)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            var uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            Clicked?.Invoke(new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)), e.button, drag);
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

    // Tells a button when the pointer goes down on it, and when it comes up
    // or leaves, for its pressed look. The left button only, unless AnyButton.
    public sealed class PressHint : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action<bool> Pressed;
        public bool AnyButton;
        bool down;

        public void OnPointerDown(PointerEventData e) { if (AnyButton || e.button == PointerEventData.InputButton.Left) Set(true); }
        public void OnPointerUp(PointerEventData e) => Set(false);
        public void OnPointerExit(PointerEventData e) => Set(false);
        void OnDisable() => Set(false);

        void Set(bool on)
        {
            if (down == on) return;
            down = on;
            Pressed?.Invoke(on);
        }
    }

    // Takes graphics to a colour over 0.1 s of real time, or at once.
    public sealed class ColourEase : MonoBehaviour
    {
        public const float Seconds = 0.1f;
        Graphic[] graphics;
        Color from, to;
        float start;

        public static ColourEase On(GameObject go, Color colour, params Graphic[] graphics)
        {
            var e = go.AddComponent<ColourEase>();
            e.graphics = graphics;
            e.Snap(colour);
            return e;
        }

        public void To(Color colour)
        {
            if (colour == to) return;
            from = graphics.Length > 0 && graphics[0] ? graphics[0].color : colour;
            to = colour;
            start = Time.unscaledTime;
            enabled = true;
        }

        public void Snap(Color colour)
        {
            to = colour;
            Paint(colour);
            enabled = false;
        }

        void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - start) / Seconds);
            Paint(Color.Lerp(from, to, t));
            if (t >= 1f) enabled = false;
        }

        void Paint(Color colour)
        {
            foreach (var g in graphics) if (g && g.color != colour) g.color = colour;
        }
    }
}
