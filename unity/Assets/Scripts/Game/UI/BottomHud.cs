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
                detail.text = $"Health {u.Health} of {u.MaxHealth}{built}\n{def.Category}";
                var o = b.ReadOrder(u.Handle);
                order.text = OrderText(o, b);
            }
            else
            {
                title.text = chosen.Count + " units";
                healthBar.gameObject.SetActive(true);
                float hp = 0, max = 0;
                foreach (var u in chosen) { hp += u.Health; max += u.MaxHealth; }
                UiKit.SetBar(healthFill, max > 0 ? hp / max : 1);
                detail.text = string.Join(", ", chosen.GroupBy(u => u.Def).Select(g => $"{g.Count()} {Nice(b.UnitDefs[g.Key])}"));
                order.text = "";
            }
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
            string key = mine ? string.Join(",", defs) : "";
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

        void BuildGrid(List<UnitState> chosen, List<int> defs)
        {
            var b = root.Backend;
            bool mobile = chosen.Any(u => !b.UnitDefs[u.Def].IsBuilding);
            if (mobile)
            {
                Command("Move", "M", () => root.Orders?.Arm(CommandKind.Move));
                Command("Attack", "A", () => root.Orders?.Arm(CommandKind.Attack));
                Command("Stop", "S", () => root.Orders?.Stop());
                Command("Patrol", "P", () => root.Orders?.Arm(CommandKind.Patrol));
                Command("Guard", "G", () => root.Orders?.Arm(CommandKind.Guard));
            }
            // Build options when everything selected is one kind that builds.
            if (defs.Count != 1) return;
            var def = b.UnitDefs[defs[0]];
            int factory = def.IsBuilding ? chosen[0].Handle : -1;
            foreach (int option in def.BuildOptions)
            {
                if (option < 0 || option >= b.UnitDefs.Count) continue;
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
                    // The cost on a dark strip across the bottom.
                    var strip = UiKit.Picture(btn.transform, "Cost", UiKit.White, new Color(0, 0, 0, 0.65f));
                    strip.raycastTarget = false;
                    strip.rectTransform.Place(0, 0, 1, 0, 4, 3, 4, -26);
                    var cost = UiKit.Label(strip.transform, od.ManaCost.ToString(), 22, UiKit.GoldBright, TextAnchor.MiddleCenter, true);
                    cost.rectTransform.Fill();
                }
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
        }

        void Enqueue(List<UnitState> factories, int def, CommandKind kind)
        {
            foreach (var f in factories)
                root.Backend.Command(new GameCommand { Kind = kind, Unit = f.Handle, TargetUnit = -1, BuildDef = def });
        }

        void Command(string label, string key, System.Action act)
        {
            var btn = UiKit.MakeButton(grid, label, () => act(), 24);
            var hint = UiKit.Label(btn.transform, key, 18, UiKit.Dim, TextAnchor.LowerRight);
            hint.rectTransform.Fill(8);
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

    public sealed class RightClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action Clicked;
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Right) Clicked?.Invoke(); }
    }
}
