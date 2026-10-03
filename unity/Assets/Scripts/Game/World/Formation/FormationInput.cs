// FormationInput.cs - the formation drag in a running game. Reads the
// mouse and keys into a PointerFrame (or takes a test's frames), feeds the
// gesture, lays out the formation while the drag is live, and at the
// release sends one MoveFormation per role block, so each block keeps to
// its own slowest unit. A press that stays a click goes back to
// OrderInput, which does what it always did.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FormationInput
    {
        readonly IGameBackend backend;
        readonly WorldView world;
        readonly OrderInput orders;
        public readonly FormationGesture Gesture = new FormationGesture();
        public readonly FormationPreview Preview;

        public bool Pace = true;
        public FormationFacing Facing = FormationFacing.ByDrag;
        public FormationShape Shape { get => Gesture.Shape; set => Gesture.Shape = value; }
        public bool ClassicRightDrag { get => Gesture.ClassicRightDrag; set => Gesture.ClassicRightDrag = value; }
        // Tests feed frames here in place of the mouse and keys.
        public Func<PointerFrame> Source;

        public bool Live => Gesture.Live;
        public bool Busy => Gesture.Busy;
        // The layout per layer, as the preview last showed it.
        public readonly FormationLayout[] Layers =
        {
            new FormationLayout { Layer = FormationLayer.Ground },
            new FormationLayout { Layer = FormationLayer.Water },
            new FormationLayout { Layer = FormationLayer.Air },
        };
        public int SlotCount => Layers[0].Count + Layers[1].Count + Layers[2].Count;
        // The line as drawn: the ground under the press and under the pointer.
        public Vector2 LineFrom => a;
        public Vector2 LineTo => b;
        public int Recomputes { get; private set; }

        int[] handles = new int[64];
        int handleCount;
        readonly Dictionary<int, int> index = new Dictionary<int, int>();
        readonly Dictionary<int, FormationKind> kinds = new Dictionary<int, FormationKind>();
        struct Remembered { public Vector2 Slot; public float Heading; public int Formation; }
        readonly Dictionary<int, Remembered> remembered = new Dictionary<int, Remembered>();
        int formationId;

        readonly FormationAssign assign = new FormationAssign();
        readonly FormationSnap snap = new FormationSnap();
        readonly TerrainGround ground = new TerrainGround();
        Vector2 a, b, lastB;
        FormationShape lastShape;
        bool lastFace, lastAlt, lastPace, computed;
        float lastTime;
        Vector2 pointer;
        Vector3 anchor;
        Camera lastCam;
        string readout = "", readoutQueued = "", readHead;
        int readWide, readDeep;
        bool readPace, readKeys, shapeChanged;
        // Drags this session, for the key hints on the first few.
        static int drags;
        static GUIStyle readoutStyle;
        static Texture2D readoutBack;
        readonly GUIContent readoutContent = new GUIContent();

        public FormationInput(IGameBackend backend, WorldView world, OrderInput orders)
        {
            this.backend = backend;
            this.world = world;
            this.orders = orders;
            var o = GameOptions.Load();
            Pace = o.FormationPace;
            Facing = o.FacingRule;
            Shape = o.Formation;
            ClassicRightDrag = o.ClassicRightDrag;
            Preview = new FormationPreview(backend);
        }

        public void Abort()
        {
            if (Gesture.Busy) Gesture.Abort();
            Preview.Clear();
        }

        // This frame's pointer and keys for OrderInput: a test's frame when
        // Source is set, else the mouse and keyboard.
        public PointerFrame Frame(bool overUi) => Source != null ? Source() : Read(Input.mousePosition, overUi);

        // Once a frame from OrderInput, with the frame it read and the ground
        // under it. mouseTaken: an armed sidebar action has the mouse.
        // Returns true while the formation has the mouse.
        public bool Update(Camera cam, in PointerFrame frame, bool onGround, Vector3 at, bool mouseTaken)
        {
            using (Marker.Auto()) return Step(cam, frame, onGround, at, mouseTaken);
        }

        // For the crowd test's timing.
        static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("Oku.Formation");

        bool Step(Camera cam, in PointerFrame frame, bool onGround, Vector3 at, bool mouseTaken)
        {
            Gesture.Classic = orders.Classic;
            lastCam = cam;
            var f = frame;
            f.OnGround = onGround;
            f.Ground = at;
            pointer = f.Screen;
            if (mouseTaken)
            {
                if (Gesture.Busy) Abort();
                Idle();
                return false;
            }
            bool armed = orders.Armed != null || orders.ArmedAction != null;
            bool canStart = Gesture.State == GestureState.Idle && (f.LeftDown || f.RightDown) && Collect() > 0;
            var shape = Shape;
            var e = Gesture.Feed(f, canStart, armed);
            if (Shape != shape) shapeChanged = true;
            // Tab's choice sticks for later drags and games, kept when a drag ends.
            if (shapeChanged && e != GestureEvent.Pending && e != GestureEvent.Started && e != GestureEvent.Dragging)
            {
                GameOptions.SaveFormation(Shape);
                shapeChanged = false;
            }
            switch (e)
            {
                case GestureEvent.Pending:
                    Idle();
                    return true;
                case GestureEvent.Click:
                    if (Gesture.Button == 1) orders.RightClick(cam, Gesture.Press.Screen, Gesture.Press.OnGround, Gesture.Press.Ground);
                    else orders.LeftClick(cam, f.Screen, f.OnGround, f.Ground);
                    Idle();
                    return true;
                case GestureEvent.Started:
                    drags++;
                    a = Flat(Gesture.Press.Ground);
                    b = f.OnGround && !f.OverUi ? Flat(f.Ground) : a;
                    ground.Prepare(backend, world);
                    computed = false;
                    Refresh(f);
                    Preview.Draw(this, true);
                    return true;
                case GestureEvent.Dragging:
                    Refresh(f);
                    Preview.Draw(this, true);
                    return true;
                case GestureEvent.Committed:
                    // What the preview showed goes, unless Alt changed at the release.
                    if (!computed || Gesture.Alt != lastAlt) Recompute(Pace ^ Gesture.PaceFlip);
                    Send();
                    Preview.Clear();
                    Idle();
                    return true;
                case GestureEvent.Aborted:
                    Preview.Clear();
                    Idle();
                    return true;
                default:
                    Idle();
                    return false;
            }
        }

        // Between drags only queued markers show, and they are tidied now and then.
        void Idle()
        {
            if (Preview.QueuedCount > 0 && Time.frameCount % 15 == 0)
            {
                IndexUnits();
                Preview.Prune(this, Time.unscaledTime);
            }
            Preview.Draw(this, false);
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        static PointerFrame Read(Vector3 mouse, bool overUi) => new PointerFrame
        {
            Screen = mouse,
            LeftDown = Input.GetMouseButtonDown(0), LeftHeld = Input.GetMouseButton(0), LeftUp = Input.GetMouseButtonUp(0),
            RightDown = Input.GetMouseButtonDown(1), RightHeld = Input.GetMouseButton(1), RightUp = Input.GetMouseButtonUp(1),
            Shift = Shift,
            Ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
            Seconds = Time.unscaledTime,
            Alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            TabDown = Input.GetKeyDown(KeyCode.Tab), FDown = Input.GetKeyDown(KeyCode.F), GDown = Input.GetKeyDown(KeyCode.G),
            EscapeDown = Input.GetKeyDown(KeyCode.Escape),
            Focused = Application.isFocused,
            OverUi = overUi,
            Dpi = Screen.dpi,
        };

        // ---- Who takes part ----

        void IndexUnits()
        {
            index.Clear();
            var units = world.Entities.Units;
            for (int i = 0; i < world.Entities.UnitCount; i++) index[units[i].Handle] = i;
        }

        // The selected units that take part, frozen at the press, by handle.
        int Collect()
        {
            IndexUnits();
            handleCount = 0;
            if (handles.Length < orders.Selected.Count) handles = new int[Mathf.NextPowerOfTwo(orders.Selected.Count)];
            foreach (int h in orders.Selected)
                if (index.TryGetValue(h, out int i) && TakesPart(world.Entities.Units[i])) handles[handleCount++] = h;
            Array.Sort(handles, 0, handleCount);
            return handleCount;
        }

        bool TakesPart(in UnitState u)
        {
            if (u.Player != backend.LocalPlayer || (u.Flags & (UnitFlags.Dying | UnitFlags.Building)) != 0) return false;
            var defs = backend.UnitDefs;
            if (u.Def < 0 || u.Def >= defs.Count || defs[u.Def].IsBuilding) return false;
            return world.Entities.IsDrawn(u.Handle);
        }

        public FormationKind KindOf(int def)
        {
            if (kinds.TryGetValue(def, out var k)) return k;
            var d = def >= 0 && def < backend.UnitDefs.Count ? backend.UnitDefs[def] : null;
            k = FormationRoles.Classify(d?.Name, d?.Category, d != null ? d.Footprint.x : 0, canFly: d != null && d.CanFly);
            if (d != null && d.HullCells > 0) k.Spacing = d.HullCells;
            kinds[def] = k;
            return k;
        }

        // ---- Layout ----

        void Refresh(in PointerFrame f)
        {
            if (f.OnGround && !f.OverUi) b = Flat(f.Ground);
            bool pace = Pace ^ Gesture.PaceFlip;
            bool due = !computed || (b - lastB).magnitude >= FormationTuning.RecomputeCells || Shape != lastShape
                || Gesture.FaceAbout != lastFace || Gesture.Alt != lastAlt || pace != lastPace
                || Time.unscaledTime - lastTime >= FormationTuning.RecomputeSeconds;
            if (due) Recompute(pace);
            else IndexUnits();
        }

        void Recompute(bool pace)
        {
            computed = true;
            lastB = b;
            lastShape = Shape;
            lastFace = Gesture.FaceAbout;
            lastAlt = Gesture.Alt;
            lastPace = pace;
            lastTime = Time.unscaledTime;
            Recomputes++;
            IndexUnits();
            var units = world.Entities.Units;
            foreach (var l in Layers) { l.Reserve(handleCount); l.Count = 0; }
            for (int k = 0; k < handleCount; k++)
            {
                if (!index.TryGetValue(handles[k], out int i)) continue;
                var u = units[i];
                if ((u.Flags & UnitFlags.Dying) != 0) continue;
                var kind = KindOf(u.Def);
                var l = Layers[(int)kind.Layer];
                var m = new FormationMember
                {
                    Handle = u.Handle, Position = Flat(u.Position), Heading = u.Heading, Kind = kind,
                    Moving = (u.Flags & UnitFlags.Moving) != 0,
                };
                if (remembered.TryGetValue(u.Handle, out var r)) { m.HasSlot = true; m.Slot = r.Slot; m.SlotHeading = r.Heading; m.SlotFormation = r.Formation; }
                l.Members[l.Count++] = m;
            }
            var cam = world.Camera;
            var right = cam != null ? Flat(cam.transform.right) : Vector2.right;
            var req = new FormationRequest
            {
                A = a, B = b, Shape = Shape, FaceAbout = Gesture.FaceAbout, AsTheyStand = Gesture.Alt, Facing = Facing, CameraRight = right,
            };
            bool groundSnapped = false;
            foreach (var l in Layers)
            {
                if (l.Count == 0) continue;
                FormationPlanner.Plan(l, req);
                assign.Assign(l);
                // Boats keep clear of the cells the ground layer's hovers took.
                snap.Snap(l, ground, l.Layer == FormationLayer.Water && groundSnapped);
                groundSnapped |= l.Layer == FormationLayer.Ground;
            }
            Preview.Build(this);
            var main = Main();
            var mid = Vector2.zero;
            for (int s = 0; s < main.Count; s++) mid += main.Slots[s].World;
            mid /= Mathf.Max(1, main.Count);
            anchor = new Vector3(mid.x, backend.GroundHeight(mid.x, mid.y), mid.y);
            bool keys = drags <= FormationTuning.ReadoutKeyDrags;
            if (Gesture.Alt) SetReadout("As they stand", -1, -1, pace, keys);
            else SetReadout(FormationPlanner.Name(Shape), main.Wide, main.Deep, pace, keys);
        }

        // The readout's text, made again only when what it says changes.
        // A group as it stands has no ranks worth counting.
        void SetReadout(string head, int wide, int deep, bool pace, bool keys)
        {
            if (head == readHead && wide == readWide && deep == readDeep && pace == readPace && keys == readKeys && readout.Length > 0) return;
            readHead = head;
            readWide = wide;
            readDeep = deep;
            readPace = pace;
            readKeys = keys;
            string size = wide >= 0 ? $"   {wide} wide, {deep} deep" : "";
            string first = $"{head}{size}   {(pace ? "block pace" : "own pace")}";
            string hint = keys ? "\nTab shape   F face about   G pace   Alt as they stand   Shift queue" : "";
            readout = first + hint;
            readoutQueued = first + "   queued" + hint;
        }

        // The layer with the most units, for the readout and the arrow.
        public FormationLayout Main()
        {
            var best = Layers[0];
            foreach (var l in Layers) if (l.Count > best.Count) best = l;
            return best;
        }

        // Where a unit stands now, for the preview's lines.
        public bool UnitAt(int handle, out Vector3 at)
        {
            at = default;
            if (!index.TryGetValue(handle, out int i) || i >= world.Entities.UnitCount || world.Entities.Units[i].Handle != handle) return false;
            at = world.Entities.Units[i].Position;
            return true;
        }

        // ---- The order ----

        // With the pace kept, one call per role block: the melee, the
        // archers, the riders, and the casters, siege and command together.
        // Each block marches at its own slowest, as a unit does in Total War.
        static int PaceBlock(FormationRole r) =>
            r == FormationRole.Melee ? 0 : r == FormationRole.Ranged ? 1 : r == FormationRole.Cavalry ? 2 : 3;

        // A boat with no water near its slot stays out of the order.
        public static bool IsSent(FormationLayout l, int s)
        {
            var slot = l.Slots[s];
            if (slot.Member < 0) return false;
            return slot.State != SlotState.Nowhere || l.Members[slot.Member].Kind.Mover != FormationMover.Boat;
        }

        void Send()
        {
            bool pace = Pace ^ Gesture.PaceFlip;
            bool queue = Gesture.Shift;
            foreach (var l in Layers)
            {
                if (l.Count == 0) continue;
                formationId++;
                for (int block = 0; block < (pace ? 4 : 1); block++)
                {
                    int total = 0;
                    for (int s = 0; s < l.Count; s++)
                        if (IsSent(l, s) && (!pace || PaceBlock(l.Slots[s].Role) == block)) total++;
                    for (int s = 0, start = 0; start < total; start += FormationTuning.UnitsPerCall)
                    {
                        int n = Mathf.Min(FormationTuning.UnitsPerCall, total - start);
                        var ids = new int[n];
                        var to = new Vector2[n];
                        for (int j = 0; j < n; s++)
                        {
                            if (!IsSent(l, s) || pace && PaceBlock(l.Slots[s].Role) != block) continue;
                            var slot = l.Slots[s];
                            ids[j] = l.Members[slot.Member].Handle;
                            to[j] = slot.World;
                            remembered[ids[j]] = new Remembered { Slot = slot.World, Heading = l.Heading, Formation = formationId };
                            j++;
                        }
                        backend.MoveFormation(ids, to, l.Heading, pace, queue);
                    }
                }
                if (queue) Preview.AddQueued(l, Time.unscaledTime);
                else Preview.ForgetQueued(l);
            }
            // Handles of units long gone need not be remembered.
            if (remembered.Count > 4096) remembered.Clear();
        }

        // ---- The readout, from OnGUI ----

        public void DrawReadout()
        {
            if (!Live || Event.current == null || Event.current.type != EventType.Repaint) return;
            if (readoutStyle == null || readoutBack == null)
            {
                readoutStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false };
                readoutStyle.normal.textColor = new Color(0.92f, 1f, 0.9f);
                readoutBack = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
                readoutBack.SetPixel(0, 0, Color.white);
                readoutBack.Apply();
            }
            readoutStyle.fontSize = Mathf.RoundToInt(15f * Mathf.Max(1f, Screen.height / 1080f));
            readoutContent.text = Gesture.Shift ? readoutQueued : readout;
            var size = readoutStyle.CalcSize(readoutContent);
            var at = ReadoutBox(size);
            float x = at.x, y = at.y;
            var box = new Rect(x - 6f, y - 3f, size.x + 12f, size.y + 6f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, readoutBack);
            GUI.color = old;
            GUI.Label(new Rect(x, y, size.x, size.y), readoutContent, readoutStyle);
        }

        public string ReadoutText => Gesture.Shift ? readoutQueued : readout;

        // Where a readout of this size goes, in GUI space: beside the
        // pointer on the side away from the formation, so it never sits on
        // the slots being placed.
        public Rect ReadoutBox(Vector2 size)
        {
            bool left = false, above = false;
            if (lastCam != null)
            {
                var c = lastCam.WorldToScreenPoint(anchor);
                if (c.z > 0) { left = c.x > pointer.x; above = c.y < pointer.y; }
            }
            float gy = Screen.height - pointer.y;
            float x = Mathf.Clamp(left ? pointer.x - 22f - size.x : pointer.x + 22f, 8f, Mathf.Max(8f, Screen.width - size.x - 8f));
            float y = Mathf.Clamp(above ? gy - 18f - size.y : gy + 18f, 4f, Mathf.Max(4f, Screen.height - size.y - 8f));
            return new Rect(x, y, size.x, size.y);
        }

        // ---- The ground, as near as the terrain tells it ----

        // Until the backend has a passability read: land and water from the
        // heights against the sea, buildings from the snapshot. Enemy
        // buildings count only while drawn, so the fog keeps its secrets.
        sealed class TerrainGround : IFormationGround
        {
            public int Width { get; private set; }
            public int Height { get; private set; }
            byte[] land = Array.Empty<byte>();
            bool[] blocked = Array.Empty<bool>();
            int[] walkRegions = Array.Empty<int>(), sailRegions = Array.Empty<int>();
            MapTerrain built;

            const byte Walk = 1, Sail = 2;

            public void Prepare(IGameBackend backend, WorldView world)
            {
                var t = backend.Terrain;
                if (t == null) { Width = Height = 0; return; }
                if (!ReferenceEquals(t, built))
                {
                    built = t;
                    Width = Mathf.Max(0, Mathf.FloorToInt(t.Size.x));
                    Height = Mathf.Max(0, Mathf.FloorToInt(t.Size.y));
                    land = new byte[Width * Height];
                    blocked = new bool[Width * Height];
                    bool sea = t.SeaLevel > 0;
                    for (int y = 0; y < Height; y++)
                        for (int x = 0; x < Width; x++)
                        {
                            float h = t.Sample(x + 0.5f, -(y + 0.5f));
                            byte v = 0;
                            if (!sea || h >= t.SeaLevel - 0.3f) v |= Walk;
                            if (sea && h <= t.SeaLevel - 0.5f) v |= Sail;
                            land[y * Width + x] = v;
                        }
                    walkRegions = new int[Width * Height];
                    sailRegions = new int[Width * Height];
                    FormationRegions.Label(Width, Height, land, Walk, walkRegions);
                    FormationRegions.Label(Width, Height, land, Sail, sailRegions);
                }
                Array.Clear(blocked, 0, blocked.Length);
                var units = world.Entities.Units;
                var defs = backend.UnitDefs;
                for (int i = 0; i < world.Entities.UnitCount; i++)
                {
                    var u = units[i];
                    if (u.Def < 0 || u.Def >= defs.Count || !defs[u.Def].IsBuilding || (u.Flags & UnitFlags.Dying) != 0) continue;
                    if (!backend.Allied(u.Player, backend.LocalPlayer) && !world.Entities.IsDrawn(u.Handle)) continue;
                    var fp = defs[u.Def].Footprint;
                    var size = (u.Facing & 1) == 1 ? new Vector2Int(fp.y, fp.x) : fp;
                    int x0 = Mathf.RoundToInt(u.Position.x - size.x / 2f), y0 = Mathf.RoundToInt(-u.Position.z - size.y / 2f);
                    for (int y = Mathf.Max(0, y0); y < Mathf.Min(Height, y0 + size.y); y++)
                        for (int x = Mathf.Max(0, x0); x < Mathf.Min(Width, x0 + size.x); x++)
                            blocked[y * Width + x] = true;
                }
            }

            public bool CanStand(FormationMover mover, int x, int y)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
                if (mover == FormationMover.Flyer) return true;
                int i = y * Width + x;
                if (blocked[i]) return false;
                switch (mover)
                {
                    case FormationMover.Hover: return true;
                    case FormationMover.Boat: return (land[i] & Sail) != 0;
                    default: return (land[i] & Walk) != 0;
                }
            }

            // Hovers and flyers reach anywhere on the map.
            public int Region(FormationMover mover, int x, int y)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
                switch (mover)
                {
                    case FormationMover.Boat: return sailRegions[y * Width + x];
                    case FormationMover.Walker: return walkRegions[y * Width + x];
                    default: return 0;
                }
            }
        }
    }
}
