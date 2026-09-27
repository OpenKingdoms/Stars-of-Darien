// BottomHud.cs - the battle panel along the bottom of the screen: the
// minimap with every unit and the camera's view, what is selected with
// its health and order, and the command and build buttons for it. Build
// buttons arm a placement for builders and queue units at factories,
// with a count of what is queued. Click the minimap to look there, right
// click it to send the selection there.
using System.Collections.Generic;
using System.Linq;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class BottomHud
    {
        public const float Height = 250f, MinimapSize = 236f;

        readonly GameRoot root;
        readonly RectTransform panel;
        readonly RawImage minimap, dots;
        readonly RectTransform view;
        readonly Text title, detail, order;
        readonly Image healthFill;
        Image manaFill;
        RectTransform manaBar;
        Text hover;
        RectTransform hoverBox;
        readonly RectTransform healthBar, grid;
        readonly List<(Button button, Text badge, int def, int factory)> buildButtons = new List<(Button, Text, int, int)>();
        string gridKey = "", minimapFor;
        float nextDots, nextPanel;
        Texture2D mapTex, dotTex;
        Color32[] dotPx;
        readonly UnitState[] units = new UnitState[EntityRenderer.MaxUnits];

        public BottomHud(GameRoot root, Transform parent)
        {
            this.root = root;
            panel = UiKit.Picture(parent, "Bottom", UiKit.Stone, new Color(0.85f, 0.82f, 0.78f, 0.97f)).rectTransform;
            panel.Place(0, 0, 1, 0, 0, 0, 0, -Height);
            var trim = UiKit.Picture(panel, "Trim", UiKit.BarFill, Color.white, true);
            trim.rectTransform.Place(0, 1, 1, 1, 0, -2, 0, -2);

            // Minimap, framed, on the left.
            var mapFrame = UiKit.Picture(panel, "Minimap", UiKit.White, Color.black);
            mapFrame.rectTransform.Place(0, 0.5f, 0, 0.5f, 10, -MinimapSize / 2, -(10 + MinimapSize), -MinimapSize / 2);
            var mapTrim = UiKit.Picture(mapFrame.transform, "Trim", UiKit.Frame, Color.white, true);
            mapTrim.rectTransform.Fill(-5);
            mapTrim.raycastTarget = false;
            minimap = UiKit.Rect(mapFrame.transform, "Map").Fill(3).gameObject.AddComponent<RawImage>();
            dots = UiKit.Rect(minimap.transform, "Units").Fill().gameObject.AddComponent<RawImage>();
            dots.raycastTarget = false;
            view = UiKit.Picture(minimap.transform, "View", UiKit.Frame, new Color(1, 1, 1, 0.9f), true).rectTransform;
            view.GetComponent<Image>().raycastTarget = false;
            view.GetComponent<Image>().pixelsPerUnitMultiplier = 4f;
            minimap.gameObject.AddComponent<MinimapInput>().Clicked = OnMinimap;

            // What is selected, in the middle.
            var info = UiKit.Panel(panel, "Selection", true);
            info.Place(0, 0, 0.5f, 1, 270, 14, 20, 18);
            title = UiKit.Label(info, "", 32, UiKit.Ink, TextAnchor.UpperLeft, true);
            title.GetComponent<Shadow>().enabled = false;
            title.rectTransform.Place(0, 1, 1, 1, 22, -60, 22, 14);
            healthBar = UiKit.Bar(info, "Health", out healthFill).rectTransform;
            healthBar.Place(0, 1, 1, 1, 22, -92, 22, 70);
            // A caster's own mana, in blue under its health.
            manaBar = UiKit.Bar(info, "Mana", out manaFill).rectTransform;
            manaBar.Place(0, 1, 1, 1, 22, -112, 22, 96);
            manaFill.color = new Color(0.45f, 0.65f, 1.3f);
            manaBar.gameObject.SetActive(false);
            // What an enemy under the pointer is, beside the pointer.
            hoverBox = UiKit.Picture(parent, "Hover", UiKit.White, new Color(0.08f, 0.06f, 0.04f, 0.9f)).rectTransform;
            hoverBox.GetComponent<Image>().raycastTarget = false;
            hoverBox.anchorMin = hoverBox.anchorMax = Vector2.zero;
            hoverBox.pivot = new Vector2(0, 1);
            hoverBox.sizeDelta = new Vector2(300, 70);
            hover = UiKit.Label(hoverBox, "", 21, UiKit.Pale, TextAnchor.MiddleLeft);
            hover.rectTransform.Fill(8);
            hoverBox.gameObject.SetActive(false);
            detail = UiKit.Label(info, "", 24, UiKit.Ink, TextAnchor.UpperLeft);
            detail.GetComponent<Shadow>().enabled = false;
            detail.rectTransform.Place(0, 0, 1, 1, 22, 40, 22, 104);
            order = UiKit.Label(info, "", 24, new Color(0.35f, 0.2f, 0.08f), TextAnchor.LowerLeft);
            order.GetComponent<Shadow>().enabled = false;
            order.rectTransform.Place(0, 0, 1, 0, 22, 12, 22, -44);

            // Commands and builds, on the right.
            grid = UiKit.Rect(panel, "Commands").Place(0.5f, 0, 1, 1, 24, 14, 16, 18);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(150, 70);
            g.spacing = new Vector2(8, 6);
            g.startCorner = GridLayoutGroup.Corner.UpperLeft;
            g.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            g.constraintCount = 3;
        }

        public void Tick()
        {
            var world = root.World;
            if (world == null || root.Backend.Terrain == null) return;
            if (minimapFor != root.Setup.MapId) LoadMinimap();
            UpdateView(world);
            if (Time.unscaledTime >= nextDots) { nextDots = Time.unscaledTime + 0.2f; UpdateDots(); }
            if (Time.unscaledTime >= nextPanel) { nextPanel = Time.unscaledTime + 0.1f; UpdatePanel(world); }
            UpdateHover(world);
        }

        // An enemy under the pointer: its name, health and mana, by the pointer.
        void UpdateHover(WorldView world)
        {
            var orders = root.Orders;
            int unit = orders != null && !orders.PointerOverUi ? orders.PointerUnit : -1;
            var b = root.Backend;
            UnitState found = default;
            bool enemy = false;
            if (unit >= 0)
                for (int i = 0; i < world.Entities.UnitCount; i++)
                    if (world.Entities.Units[i].Handle == unit) { found = world.Entities.Units[i]; enemy = found.Player != b.LocalPlayer; break; }
            hoverBox.gameObject.SetActive(enemy);
            if (!enemy) return;
            var def = b.UnitDefs[found.Def];
            string owner = found.Player >= 0 && found.Player < b.Players.Count ? b.Players[found.Player].Name : "";
            hover.text = $"{Nice(def)}, {owner}\nHealth {found.Health} of {found.MaxHealth}" + (found.MaxMana > 0 ? $"   Mana {found.Mana}" : "");
            var canvas = hoverBox.GetComponentInParent<Canvas>();
            var parentRt = (RectTransform)hoverBox.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, Input.mousePosition,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local))
                hoverBox.anchoredPosition = local - parentRt.rect.min + new Vector2(24, -12);
        }

        void LoadMinimap()
        {
            minimapFor = root.Setup.MapId;
            if (mapTex != null) Object.Destroy(mapTex);
            mapTex = UiKit.ToTexture(root.Backend.MapPreview(minimapFor, 256), true);
            if (mapTex == null)
            {
                // No overview: a picture of the ground from its chunks.
                var t = root.Backend.Terrain;
                var img = TerrainBuilder.BakeRegion(t, 0, 0, 128, c => root.Backend.TerrainChunk(c));
                mapTex = UiKit.ToTexture(img, true);
            }
            minimap.texture = mapTex;
            var size = root.Backend.Terrain.Size;
            if (dotTex != null) Object.Destroy(dotTex);
            int w = 128, h = Mathf.Max(8, Mathf.RoundToInt(128 * size.y / Mathf.Max(1, size.x)));
            dotTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            dotPx = new Color32[w * h];
            dots.texture = dotTex;
            // Keep the map's shape inside the square frame.
            float aspect = size.x / Mathf.Max(1, size.y);
            var r = minimap.rectTransform;
            if (aspect >= 1) r.Place(0, 0.5f - 0.5f / aspect, 1, 0.5f + 0.5f / aspect, 3, 0, 3, 0);
            else r.Place(0.5f - 0.5f * aspect, 0, 0.5f + 0.5f * aspect, 1, 0, 3, 0, 3);
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
                Color32 c = u.Player >= 0 && u.Player < b.Players.Count ? b.Players[u.Player].Tint : new Color32(200, 200, 200, 255);
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
            var cam = world.Camera;
            var c = cam.GetComponent<Camera>();
            var size = root.Backend.Terrain.Size;
            float wide = 2f * cam.distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = wide * c.aspect / size.x, d = wide / Mathf.Max(0.3f, Mathf.Sin(cam.pitch * Mathf.Deg2Rad)) / size.y;
            var centre = new Vector2(cam.focus.x / size.x, 1f + cam.focus.z / size.y);
            view.anchorMin = view.anchorMax = centre;
            var r = minimap.rectTransform.rect;
            view.sizeDelta = new Vector2(Mathf.Min(w, 1.5f) * r.width, Mathf.Min(d, 1.5f) * r.height);
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

        void UpdatePanel(WorldView world)
        {
            var b = root.Backend;
            var sel = world.Entities.Selected;
            int n = world.Entities.UnitCount;
            var chosen = new List<UnitState>();
            for (int i = 0; i < n; i++)
                if (sel.Contains(world.Entities.Units[i].Handle)) chosen.Add(world.Entities.Units[i]);

            if (chosen.Count == 0)
            {
                manaBar.gameObject.SetActive(false);
                title.text = "";
                detail.text = "Drag a box or click a unit to select it.";
                order.text = "";
                healthBar.gameObject.SetActive(false);
            }
            else if (chosen.Count == 1)
            {
                var u = chosen[0];
                var def = b.UnitDefs[u.Def];
                title.text = Nice(def);
                healthBar.gameObject.SetActive(true);
                UiKit.SetBar(healthFill, u.MaxHealth > 0 ? (float)u.Health / u.MaxHealth : 1);
                string built = u.BuildProgress < 1f ? $"   Being built, {u.BuildProgress * 100:0}%" : "";
                string manaLine = u.MaxMana > 0 ? $"   Mana {u.Mana} of {u.MaxMana}" : "";
                detail.text = $"Health {u.Health} of {u.MaxHealth}{manaLine}{built}\n{def.Category}";
                manaBar.gameObject.SetActive(u.MaxMana > 0);
                if (u.MaxMana > 0) UiKit.SetBar(manaFill, (float)u.Mana / u.MaxMana);
                var o = b.ReadOrder(u.Handle);
                order.text = OrderText(o, b);
            }
            else
            {
                manaBar.gameObject.SetActive(false);
                title.text = chosen.Count + " units";
                healthBar.gameObject.SetActive(true);
                float hp = 0, max = 0;
                foreach (var u in chosen) { hp += u.Health; max += u.MaxHealth; }
                UiKit.SetBar(healthFill, max > 0 ? hp / max : 1);
                detail.text = string.Join(", ", chosen.GroupBy(u => u.Def).Select(g => $"{g.Count()} {Nice(b.UnitDefs[g.Key])}"));
                order.text = "";
            }
            // While a building is being placed, say how to turn it.
            var orders = root.Orders;
            if (orders != null && orders.Armed == CommandKind.Build)
                order.text = b.CanRotate(orders.ArmedDef) ? "R or ] turns it, Shift R or [ turns it back" : "This building cannot be turned";
            RefreshGrid(chosen);
        }

        static string Nice(UnitDef d) =>
            !string.IsNullOrEmpty(d.Title) ? d.Title : string.IsNullOrEmpty(d.Description) ? d.Name : d.Description;

        readonly Dictionary<int, Texture2D> pictures = new Dictionary<int, Texture2D>();

        Texture2D Picture(int def)
        {
            if (pictures.TryGetValue(def, out var t)) return t;
            t = UiKit.ToTexture(root.Backend.UnitPicture(def), true);
            if (t != null) t.filterMode = FilterMode.Trilinear;
            pictures[def] = t;
            return t;
        }

        static string OrderText(UnitOrder o, IGameBackend b)
        {
            switch (o.Kind)
            {
                case OrderKind.None: return "Waiting for orders";
                case OrderKind.Move: return "Moving";
                case OrderKind.Attack: return "Attacking";
                case OrderKind.AttackGround: return "Attacking the ground";
                case OrderKind.Build: return "Building";
                case OrderKind.Patrol: return "Patrolling";
                case OrderKind.Guard: return "Guarding";
                case OrderKind.Repair: return "Repairing";
                case OrderKind.Reclaim: return "Reclaiming";
                default: return o.Kind.ToString();
            }
        }

        // Rebuilds the buttons when what is selected changes kind, and
        // refreshes the queue badges every time.
        void RefreshGrid(List<UnitState> chosen)
        {
            var b = root.Backend;
            var defs = chosen.Select(u => u.Def).Distinct().OrderBy(d => d).ToList();
            bool mine = chosen.Count > 0 && chosen.All(u => u.Player == b.LocalPlayer);
            actions = mine ? b.SelectionActions() : System.Array.Empty<UnitAction>();
            if (root.Orders != null) root.Orders.Actions = actions;
            string armedId = root.Orders?.ArmedAction?.Id ?? "";
            string key = mine ? string.Join(",", defs) + "|" + string.Join(",", actions.Select(a => a.Id + (a.Enabled ? "1" : "0") + (a.Toggled ? "1" : "0"))) + "|" + buildPage + "|" + armedId : "";
            if (key != gridKey)
            {
                gridKey = key;
                for (int i = grid.childCount - 1; i >= 0; i--) Object.Destroy(grid.GetChild(i).gameObject);
                buildButtons.Clear();
                if (mine) BuildGrid(chosen, defs);
            }
            foreach (var bb in buildButtons)
            {
                if (bb.factory < 0 || bb.badge == null) continue;
                int q = b.QueuedCount(bb.factory, bb.def);
                bb.badge.text = q > 0 ? q.ToString() : "";
            }
        }

        UnitAction[] actions = System.Array.Empty<UnitAction>();
        int buildPage;
        int lastDefsKey;

        // The original's order: the orders, the abilities, the stances and
        // the spells, then whatever the selection builds, a page at a time.
        void BuildGrid(List<UnitState> chosen, List<int> defs)
        {
            var b = root.Backend;
            int defsKey = string.Join(",", defs).GetHashCode();
            if (defsKey != lastDefsKey) { lastDefsKey = defsKey; buildPage = 0; }
            int used = 0;
            if (actions.Length > 0)
            {
                foreach (var a in actions) { ActionButton(a); used++; }
            }
            else if (chosen.Any(u => !b.UnitDefs[u.Def].IsBuilding))
            {
                Command("Move", "M", () => root.Orders?.Arm(CommandKind.Move));
                Command("Attack", "Ctrl A", () => root.Orders?.Arm(CommandKind.Attack));
                Command("Stop", "Ctrl S", () => root.Orders?.Stop());
                Command("Patrol", "P", () => root.Orders?.Arm(CommandKind.Patrol));
                Command("Guard", "G", () => root.Orders?.Arm(CommandKind.Guard));
                used = 5;
            }
            // Build options when everything selected is one kind that builds.
            if (defs.Count != 1) return;
            var def = b.UnitDefs[defs[0]];
            var options = def.BuildOptions.Where(o => o >= 0 && o < b.UnitDefs.Count).ToList();
            if (options.Count == 0) return;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.rect.width + 8) / 158f));
            int room = Mathf.Max(1, cols * 3 - used);
            int per = options.Count > room ? Mathf.Max(1, room - 2) : room;
            int pages = (options.Count + per - 1) / per;
            buildPage = Mathf.Clamp(buildPage, 0, pages - 1);
            int factory = def.IsBuilding ? chosen[0].Handle : -1;
            foreach (int option in options.Skip(buildPage * per).Take(per))
                BuildButton(chosen, option, factory);
            if (pages > 1)
            {
                Command($"Page {buildPage + 1} of {pages}", "<", () => { buildPage = (buildPage + pages - 1) % pages; gridKey = null; }, "Previous page");
                Command("More", ">", () => { buildPage = (buildPage + 1) % pages; gridKey = null; }, "Next page");
            }
        }

        readonly Dictionary<int, Texture2D> actionPictures = new Dictionary<int, Texture2D>();

        void ActionButton(UnitAction a)
        {
            var pic = a.Picture >= 0 ? ActionPictureFor(a.Picture) : null;
            var btn = UiKit.MakeButton(grid, pic != null ? "" : a.Label, () => Pressed(a), a.Label.Length > 9 ? 19 : 22);
            btn.name = "Action " + a.Id;
            if (pic != null)
            {
                var img = UiKit.Rect(btn.transform, "Picture").Fill(4).gameObject.AddComponent<RawImage>();
                img.texture = pic;
                img.raycastTarget = false;
            }
            var face = btn.GetComponent<Image>();
            bool armed = root.Orders?.ArmedAction?.Id == a.Id;
            if (a.Toggled || armed) face.color = new Color(1.35f, 1.15f, 0.6f);
            btn.interactable = a.Enabled;
            if (!string.IsNullOrEmpty(a.Hotkey))
            {
                string key = "WASD".Contains(a.Hotkey.ToUpperInvariant()) ? "Ctrl " + a.Hotkey : a.Hotkey;
                var hint = UiKit.Label(btn.transform, key, 17, UiKit.Dim, TextAnchor.LowerRight);
                hint.rectTransform.Fill(6);
            }
            if (a.ManaCost > 0)
            {
                var cost = UiKit.Label(btn.transform, a.ManaCost.ToString(), 18, new Color(0.55f, 0.75f, 1f), TextAnchor.UpperLeft, true);
                cost.rectTransform.Fill(6);
            }
            string tip = a.Label + (a.ManaCost > 0 ? $", {a.ManaCost} mana" : "") + (!a.Enabled && !string.IsNullOrEmpty(a.Why) ? $". {a.Why}" : "");
            btn.gameObject.AddComponent<HoverHint>().Show = on => ShowHint(on ? tip : null);
        }

        Texture2D ActionPictureFor(int picture)
        {
            if (actionPictures.TryGetValue(picture, out var t)) return t;
            t = UiKit.ToTexture(root.Backend.ActionPicture(picture), true);
            actionPictures[picture] = t;
            return t;
        }

        // A button with nothing to aim at acts at once; the rest wait for
        // a click (or a drag) in the world. A spell is chosen at once too.
        void Pressed(UnitAction a)
        {
            var orders = root.Orders;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (a.Target == ActionTarget.None) { root.Backend.DoAction(a.Id, Vector3.zero, -1, default, shift); gridKey = null; return; }
            if (a.Kind == ActionKind.Spell) root.Backend.DoAction(a.Id, Vector3.zero, -1, default, false);
            orders?.ArmAction(a);
            gridKey = null;
        }

        Text hintText;

        void ShowHint(string text)
        {
            // Nothing to hide yet, and nothing may be made while the panel
            // itself is being switched off.
            if (text == null && hintText == null) return;
            if (hintText == null)
            {
                var back = UiKit.Picture(panel, "Hint", UiKit.White, new Color(0.08f, 0.06f, 0.04f, 0.92f));
                back.raycastTarget = false;
                back.rectTransform.Place(0.5f, 1, 1, 1, 24, 2, 16, -40);
                hintText = UiKit.Label(back.transform, "", 22, UiKit.Pale, TextAnchor.MiddleLeft);
                hintText.rectTransform.Fill(8);
            }
            hintText.transform.parent.gameObject.SetActive(text != null);
            if (text != null) hintText.text = text;
        }

        void BuildButton(List<UnitState> chosen, int option, int factory)
        {
            var b = root.Backend;
            var od = b.UnitDefs[option];
            int id = option;
            var pic = Picture(id);
            var btn = UiKit.MakeButton(grid, pic != null ? "" : $"{Nice(od)}\n{od.ManaCost}", null, 19);
            var text = btn.GetComponentInChildren<Text>();
            text.lineSpacing = 0.85f;
            if (pic != null)
            {
                // The game's own build picture, with the cost on it.
                var img = UiKit.Rect(btn.transform, "Picture").Place(0, 0, 1, 1, 4, 26, 4, 4).gameObject.AddComponent<RawImage>();
                img.texture = pic;
                img.raycastTarget = false;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = (float)pic.width / pic.height;
                var strip = UiKit.Picture(btn.transform, "Cost", UiKit.White, new Color(0, 0, 0, 0.65f));
                strip.raycastTarget = false;
                strip.rectTransform.Place(0, 0, 1, 0, 4, 3, 4, -26);
                var cost = UiKit.Label(strip.transform, od.ManaCost.ToString(), 22, UiKit.GoldBright, TextAnchor.MiddleCenter, true);
                cost.rectTransform.Fill();
            }
            btn.gameObject.AddComponent<HoverHint>().Show = on => ShowHint(on ? $"{Nice(od)}, {od.ManaCost} mana" : null);
            Text badge = null;
            if (factory >= 0)
            {
                badge = UiKit.Label(btn.transform, "", 24, UiKit.GoldBright, TextAnchor.UpperRight, true);
                badge.rectTransform.Fill(6);
                btn.onClick.AddListener(() => Enqueue(chosen, id, CommandKind.FactoryEnqueue));
                btn.gameObject.AddComponent<RightClick>().Clicked = () => Enqueue(chosen, id, CommandKind.FactoryDequeue);
            }
            else btn.onClick.AddListener(() => root.Orders?.Arm(CommandKind.Build, id));
            buildButtons.Add((btn, badge, id, factory));
        }

        void Enqueue(List<UnitState> factories, int def, CommandKind kind)
        {
            foreach (var f in factories)
                root.Backend.Command(new GameCommand { Kind = kind, Unit = f.Handle, TargetUnit = -1, BuildDef = def });
        }

        void Command(string label, string key, System.Action act, string tip = null)
        {
            var btn = UiKit.MakeButton(grid, label, () => act(), label.Length > 9 ? 19 : 24);
            var hint = UiKit.Label(btn.transform, key, 18, UiKit.Dim, TextAnchor.LowerRight);
            hint.rectTransform.Fill(8);
            if (tip != null) btn.gameObject.AddComponent<HoverHint>().Show = on => ShowHint(on ? tip : null);
        }

        public void Dispose()
        {
            if (mapTex != null) Object.Destroy(mapTex);
            if (dotTex != null) Object.Destroy(dotTex);
            foreach (var t in pictures.Values) if (t != null) Object.Destroy(t);
        }
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
