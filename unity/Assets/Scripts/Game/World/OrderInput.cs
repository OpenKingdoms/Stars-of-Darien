// OrderInput.cs - the mouse and keys in a running game, in one of two
// schemes. Classic plays like the original: the engine keeps the
// selection and decides what a left click means (select a friend, attack
// an enemy, move to the ground, or carry out an armed command), and a
// right click or Escape cancels. Modern keeps the selection here, selects
// with the left button and orders with the right, moving in a block.
// In both, a drag selects the player's units (Shift adds), M, A, P and G
// arm move, attack, patrol and guard, S stops, a build armed from the
// menu shows its ghost, green where it can stand, and Ctrl with a digit
// makes a group that the digit brings back. A drag with the order button
// lays out a formation (FormationInput).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class OrderInput
    {
        const float PickPixels = 28f, DragPixels = 6f;

        readonly IGameBackend backend;
        readonly WorldView world;
        Vector3 dragFrom;
        bool dragging;
        Texture2D boxTex;
        readonly int[] selectionBuffer = new int[EntityRenderer.MaxUnits];

        public bool Classic = true;
        // Held still, for scripted captures that place things themselves.
        public bool Frozen;
        // The command waiting for a click, if any, mirrored here for the ghost.
        public CommandKind? Armed { get; private set; }
        public int ArmedDef { get; private set; } = -1;
        public Vector3 GhostAt { get; private set; }
        public bool GhostOk { get; private set; }
        // What the pointer was over at the last Update.
        public bool PointerOverUi { get; private set; } = true;
        public bool PointerOnGround { get; private set; }
        public Vector3 PointerAt { get; private set; }
        public int PointerUnit { get; private set; } = -1;

        // The formation drag, which has the mouse while a drag is live.
        public readonly FormationInput Formation;

        public OrderInput(IGameBackend backend, WorldView world, bool classic)
        {
            this.backend = backend;
            this.world = world;
            Classic = classic;
            Formation = new FormationInput(backend, world, this);
        }

        public HashSet<int> Selected => world.Entities.Selected;
        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // The facing a building is placed at, 0 to 3 clockwise, remembered
        // per building type for the next placement.
        public int Facing { get; private set; }
        readonly Dictionary<int, int> facings = new Dictionary<int, int>();
        // Shift-queued sites, drawn as ghosts until the builder gets to them.
        public readonly List<EntityRenderer.GhostState> Queued = new List<EntityRenderer.GhostState>();
        // Set when a turn is refused, for the ghost to shake.
        public float RefusedAt { get; private set; } = -10f;

        // The selection's sidebar actions, kept fresh by the HUD, and the one
        // waiting for a click or a drag in the world.
        public UnitAction[] Actions = System.Array.Empty<UnitAction>();
        public UnitAction ArmedAction { get; private set; }

        public void ArmAction(UnitAction a)
        {
            DisarmHere();
            ArmedAction = a;
        }

        public void Arm(CommandKind kind, int def = -1)
        {
            ArmedAction = null;
            Armed = kind;
            ArmedDef = def;
            Facing = kind == CommandKind.Build && facings.TryGetValue(def, out var f) ? f : 0;
            if (Classic) backend.Arm(kind, def, Facing);
        }

        // Turns the armed building a quarter clockwise (+1) or back (-1).
        public bool Rotate(int by)
        {
            if (Armed != CommandKind.Build) return false;
            if (!backend.CanRotate(ArmedDef)) { RefusedAt = Time.unscaledTime; return false; }
            Facing = ((Facing + by) % 4 + 4) % 4;
            facings[ArmedDef] = Facing;
            // The engine keeps the armed facing for the placing click.
            if (Classic) backend.Arm(CommandKind.Build, ArmedDef, Facing);
            return true;
        }

        public void Disarm()
        {
            if (Armed != null && Classic) backend.Cancel();
            ArmedAction = null;
            Armed = null;
            ArmedDef = -1;
            world.Entities.Ghost = null;
            Queued.Clear();
        }

        void DisarmHere()
        {
            ArmedAction = null;
            Armed = null;
            ArmedDef = -1;
            world.Entities.Ghost = null;
        }

        // Escape and the original's right click: let go of an armed command,
        // or else deselect.
        public void Cancel()
        {
            if (Formation.Busy) { Formation.Abort(); return; }
            if (ArmedAction != null || Armed != null) { Disarm(); return; }
            if (Classic) { backend.Cancel(); PullSelection(); }
            else { Selected.Clear(); PushSelection(); }
        }

        public void Stop()
        {
            if (Classic) backend.OrderSelection(CommandKind.Stop);
            else foreach (var h in Selected) backend.Command(GameCommand.To(CommandKind.Stop, h, Vector3.zero));
        }

        // The selection as the engine keeps it, for the rings and panels.
        void PullSelection()
        {
            int n = backend.ReadSelection(selectionBuffer);
            Selected.Clear();
            for (int i = 0; i < n && i < selectionBuffer.Length; i++) Selected.Add(selectionBuffer[i]);
        }

        // A selection made here, told to the engine so its rules and unit
        // voices know it.
        void PushSelection()
        {
            var list = new int[Selected.Count];
            Selected.CopyTo(list);
            backend.Select(list, false);
        }

        public void Update()
        {
            var cam = world.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            if (cam == null || Frozen) return;
            if (Classic) PullSelection();
            // The mouse and keys this frame, or a test's frame in their place.
            var p = Formation.Frame(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            bool overUi = p.OverUi;
            var m = (Vector3)p.Screen;
            var units = world.Entities.Units;
            int count = world.Entities.UnitCount;
            if (!Formation.Live) Keys();

            // A queued site's ghost goes once its building stands there.
            if (Queued.Count > 0 && Time.frameCount % 15 == 0)
                Queued.RemoveAll(q =>
                {
                    for (int i = 0; i < count; i++)
                        if (units[i].Def == q.Def && (units[i].Position - q.At).sqrMagnitude < 0.5f) return true;
                    return false;
                });
            bool onGround = OrderInput.GroundPoint(cam.ScreenPointToRay(m), backend, out var at);
            UpdateGhost(onGround, at);
            PointerOverUi = overUi;
            PointerOnGround = onGround;
            PointerAt = at;
            PointerUnit = overUi ? -1 : Pick(cam, m, units, count, null);
            // A press on an enemy stays an attack, never a formation.
            if ((p.LeftDown || p.RightDown) && !overUi) p.OnEnemy = EnemyUnder(cam, m, units, count);

            bool armedHasMouse = ArmedAction != null && UpdateArmedAction(cam, m, overUi, onGround, at, units, count);
            if (Formation.Update(cam, p, onGround, at, armedHasMouse) || armedHasMouse) return;

            if (p.RightDown && !overUi && RightClick(cam, m, onGround, at)) return;

            if (p.LeftDown && !overUi) { dragFrom = m; dragging = true; }
            if (!p.LeftUp || !dragging) return;
            dragging = false;

            if ((m - dragFrom).magnitude > DragPixels && Armed == null)
            {
                var box = Rect.MinMaxRect(Mathf.Min(m.x, dragFrom.x), Mathf.Min(m.y, dragFrom.y), Mathf.Max(m.x, dragFrom.x), Mathf.Max(m.y, dragFrom.y));
                var inBox = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (units[i].Player != backend.LocalPlayer || (units[i].Flags & UnitFlags.Dying) != 0) continue;
                    if (!world.Entities.IsDrawn(units[i].Handle)) continue;
                    var s = cam.WorldToScreenPoint(units[i].Position + Vector3.up * (0.6f + world.Entities.VisualLift(units[i].Handle)));
                    if (s.z > 0 && box.Contains(s)) inBox.Add(units[i].Handle);
                }
                if (Classic) { backend.Select(inBox.ToArray(), Shift); PullSelection(); }
                else
                {
                    if (!Shift) Selected.Clear();
                    Selected.UnionWith(inBox);
                    PushSelection();
                }
                return;
            }
            LeftClick(cam, m, onGround, at);
        }

        // The right button's press when it is no drag: the classic scheme
        // cancels, the modern one attacks the enemy under it or moves there.
        // True when the frame ends with it.
        internal bool RightClick(Camera cam, Vector3 m, bool onGround, Vector3 at)
        {
            if (Classic) { backend.Cancel(); DisarmHere(); return true; }
            if (Armed != null) { DisarmHere(); return true; }
            if (Selected.Count > 0)
            {
                int enemy = Pick(cam, m, world.Entities.Units, world.Entities.UnitCount, false);
                if (enemy >= 0) OrderAll(CommandKind.Attack, Vector3.zero, enemy);
                else if (onGround) MoveBlock(CommandKind.Move, at);
            }
            return false;
        }

        // The left button's click when it was no drag.
        internal void LeftClick(Camera cam, Vector3 m, bool onGround, Vector3 at)
        {
            var units = world.Entities.Units;
            int count = world.Entities.UnitCount;
            if (Classic)
            {
                int unit = Pick(cam, m, units, count, null);
                if (unit >= 0 || onGround)
                {
                    if (Armed == CommandKind.Build && !GhostOk) return;
                    if (Armed == CommandKind.Build && Shift)
                        Queued.Add(new EntityRenderer.GhostState { Def = ArmedDef, At = GhostAt, Ok = true, Facing = Facing });
                    backend.Click(Armed == CommandKind.Build ? GhostAt : at, Armed == CommandKind.Build ? -1 : unit, Shift);
                    // The engine disarms after a click, so a Shift placement arms again.
                    if (Armed == CommandKind.Build && Shift) backend.Arm(CommandKind.Build, ArmedDef, Facing);
                    if (Armed != null && !Shift) { DisarmHere(); Queued.Clear(); }
                    PullSelection();
                }
                return;
            }

            if (Armed != null) { CarryOut(cam, m, units, count, onGround, at); return; }
            if (!Shift) Selected.Clear();
            int hit = Pick(cam, m, units, count, true);
            if (hit >= 0) Selected.Add(hit);
            PushSelection();
        }

        // An armed sidebar action: a click on a unit or the ground carries it
        // out, a drag covers an area where the action takes one, Shift keeps
        // it armed for another, and a right click or Escape lets it go.
        // Returns true while it has the mouse.
        bool UpdateArmedAction(Camera cam, Vector3 m, bool overUi, bool onGround, Vector3 at, UnitState[] units, int count)
        {
            var a = ArmedAction;
            if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetMouseButtonDown(1) && !overUi)) { ArmedAction = null; return true; }
            if (Input.GetMouseButtonDown(0) && !overUi) { dragFrom = m; dragging = true; return true; }
            if (!Input.GetMouseButtonUp(0) || !dragging) return true;
            dragging = false;
            bool areaAction = a.Target == ActionTarget.Area || a.Id == "ATTACK";
            bool done;
            if ((m - dragFrom).magnitude > DragPixels && areaAction)
            {
                if (!GroundPoint(cam.ScreenPointToRay(dragFrom), backend, out var p0) || !GroundPoint(cam.ScreenPointToRay(m), backend, out var p1)) return true;
                var area = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.z, p1.z), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.z, p1.z));
                done = backend.DoAction(a.Id, (p0 + p1) * 0.5f, -1, area, Shift);
            }
            else
            {
                int unit = a.Target == ActionTarget.Point ? -1 : Pick(cam, m, units, count, null);
                if (a.Target == ActionTarget.Unit && unit < 0) return true;
                if (unit < 0 && !onGround) return true;
                done = backend.DoAction(a.Id, at, unit, default, Shift);
            }
            if (done && !Shift) ArmedAction = null;
            return true;
        }

        // A drag with an armed area action is drawn like a selection box.
        public bool DraggingArea => dragging && ArmedAction != null;

        // The pointer for what it is over: the game decides, as in the
        // original, except for a command armed only here by the modern
        // scheme.
        public GameCursor PointerCursor()
        {
            if (Formation.Live) return GameCursor.Move;
            if (PointerOverUi || (PointerUnit < 0 && !PointerOnGround)) return GameCursor.Normal;
            // A building armed on a spot that cannot take it.
            if (Armed == CommandKind.Build && !GhostOk) return GameCursor.Cannot;
            if (ArmedAction != null) return GameCursors.For(ArmedAction.Command);
            if (!Classic && Armed != null) return GameCursors.For(Armed.Value);
            return backend.CursorAt(PointerAt, PointerUnit, out _);
        }

        void Keys()
        {
            // Groups: Ctrl and a digit makes one, the digit brings it back.
            for (int g = 0; g <= 9; g++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + g)) continue;
                if (Ctrl) { if (!Classic) PushSelection(); backend.AssignGroup(g); }
                else if (backend.RecallGroup(g) > 0) PullSelection();
            }
            // R or ] turns a building being placed clockwise, Shift R or [ back.
            if (Armed == CommandKind.Build)
            {
                if (Input.GetKeyDown(KeyCode.RightBracket) || (Input.GetKeyDown(KeyCode.R) && !Shift)) Rotate(1);
                if (Input.GetKeyDown(KeyCode.LeftBracket) || (Input.GetKeyDown(KeyCode.R) && Shift)) Rotate(-1);
            }
            if (Selected.Count == 0) return;
            if (Actions.Length > 0)
            {
                foreach (var a in Actions)
                {
                    if (string.IsNullOrEmpty(a.Hotkey) || a.Hotkey.Length != 1 || !a.Enabled) continue;
                    char c = char.ToLowerInvariant(a.Hotkey[0]);
                    if (c < 'a' || c > 'z' || !Input.GetKeyDown(KeyCode.A + (c - 'a'))) continue;
                    // W, A, S and D pan the camera, so their commands take Ctrl.
                    bool camKey = "wasd".IndexOf(c) >= 0;
                    if (camKey != Ctrl) continue;
                    if (a.Target == ActionTarget.None) backend.DoAction(a.Id, Vector3.zero, -1, default, Shift);
                    else ArmAction(a);
                }
                return;
            }
            // A and S pan the camera, so attack and stop take Ctrl.
            if (Input.GetKeyDown(KeyCode.S) && Ctrl) { Stop(); Disarm(); }
            if (Input.GetKeyDown(KeyCode.M)) Arm(CommandKind.Move);
            if (Input.GetKeyDown(KeyCode.A) && Ctrl) Arm(CommandKind.Attack);
            if (Input.GetKeyDown(KeyCode.P)) Arm(CommandKind.Patrol);
            if (Input.GetKeyDown(KeyCode.G)) Arm(CommandKind.Guard);
        }

        void UpdateGhost(bool onGround, Vector3 at)
        {
            world.Entities.QueuedGhosts = Queued;
            if (Armed != CommandKind.Build) { world.Entities.Ghost = null; return; }
            GhostOk = false;
            if (onGround)
            {
                GhostOk = backend.CanBuildAt(ArmedDef, at, Facing, out var snapped);
                GhostAt = snapped;
            }
            world.Entities.Ghost = onGround
                ? new EntityRenderer.GhostState { Def = ArmedDef, At = GhostAt, Ok = GhostOk, Facing = Facing, ShakeFrom = RefusedAt }
                : (EntityRenderer.GhostState?)null;
        }

        // The modern scheme's armed command, sent unit by unit.
        void CarryOut(Camera cam, Vector3 m, UnitState[] units, int count, bool onGround, Vector3 at)
        {
            var kind = Armed.Value;
            if (kind == CommandKind.Build)
            {
                if (!GhostOk) return;
                foreach (var h in Selected)
                    backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = h, Target = GhostAt, TargetUnit = -1, BuildDef = ArmedDef, Queue = Shift, Facing = Facing });
                if (Shift) Queued.Add(new EntityRenderer.GhostState { Def = ArmedDef, At = GhostAt, Ok = true, Facing = Facing });
            }
            else
            {
                int target = Pick(cam, m, units, count, kind == CommandKind.Guard ? true : (bool?)null);
                if (target >= 0 && (kind == CommandKind.Attack || kind == CommandKind.Guard)) OrderAll(kind, Vector3.zero, target);
                else if (onGround)
                {
                    if (kind == CommandKind.Attack) OrderAll(CommandKind.AttackGround, at, -1);
                    else MoveBlock(kind, at);
                }
            }
            if (!Shift) { DisarmHere(); Queued.Clear(); }
        }

        void OrderAll(CommandKind kind, Vector3 at, int target)
        {
            foreach (var h in Selected)
                backend.Command(new GameCommand { Kind = kind, Unit = h, Target = at, TargetUnit = target, BuildDef = -1, Queue = Shift });
        }

        // Moves in a square block around the point, turned with the camera.
        public void MoveBlock(CommandKind kind, Vector3 at)
        {
            if (Classic && kind == CommandKind.Move)
            {
                backend.Arm(CommandKind.Move);
                backend.Click(at, -1, Shift);
                return;
            }
            int k = 0, side = Mathf.CeilToInt(Mathf.Sqrt(Selected.Count));
            foreach (var h in Selected)
            {
                var offset = new Vector3((k % side - (side - 1) * 0.5f) * 1.4f, 0, (k / side - (side - 1) * 0.5f) * 1.4f);
                var c = GameCommand.To(kind, h, at + Quaternion.Euler(0, world.Camera.yaw, 0) * offset);
                c.Queue = Shift;
                backend.Command(c);
                k++;
            }
        }

        // The unit nearest the pointer: the player's own, anyone else's, or
        // either when own is null.
        // The unit under the pointer: the player's own, anyone else's, or
        // either when own is null. A unit is hit anywhere in its drawn
        // bounds on screen (at least a small ring round its feet), the
        // nearest to the camera first. Units not drawn, such as enemies in
        // the fog, cannot be picked.
        // The unit under a screen point, of any owner, as a click would pick it.
        public int UnitAt(Vector2 screen)
        {
            var cam = world.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            return cam == null ? -1 : Pick(cam, screen, world.Entities.Units, world.Entities.UnitCount, null);
        }

        bool EnemyUnder(Camera cam, Vector3 m, UnitState[] units, int count)
        {
            int h = Pick(cam, m, units, count, false);
            for (int i = 0; h >= 0 && i < count; i++)
                if (units[i].Handle == h) return !backend.Allied(units[i].Player, backend.LocalPlayer);
            return false;
        }

        int Pick(Camera cam, Vector3 m, UnitState[] units, int count, bool? own)
        {
            int best = -1;
            float bestDepth = float.MaxValue;
            int me = backend.LocalPlayer;
            var drawn = world.Entities.DrawnSize;
            for (int i = 0; i < count; i++)
            {
                if ((units[i].Flags & UnitFlags.Dying) != 0) continue;
                if (own != null && (units[i].Player == me) != own.Value) continue;
                if (!drawn.TryGetValue(units[i].Handle, out var size)) continue;
                var feet = units[i].Position + Vector3.up * world.Entities.VisualLift(units[i].Handle);
                if (!OnScreen(cam, feet, size.x, size.y, out var box, out float depth)) continue;
                if (!box.Contains(m) || depth >= bestDepth) continue;
                bestDepth = depth;
                best = units[i].Handle;
            }
            return best;
        }

        // A unit's bounds on screen, from its feet to its height and out to
        // its radius, never smaller than a ring of MinPickPixels.
        const float MinPickPixels = 14f;

        public static bool OnScreen(Camera cam, Vector3 feet, float height, float radius, out Rect box, out float depth)
        {
            box = default;
            depth = 0;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool any = false;
            var right = cam.transform.right * radius;
            foreach (var p in new[] { feet - right, feet + right, feet + Vector3.up * height - right, feet + Vector3.up * height + right })
            {
                var s = cam.WorldToScreenPoint(p);
                if (s.z <= 0) continue;
                any = true;
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x);
                y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
            }
            if (!any) return false;
            var c = cam.WorldToScreenPoint(feet + Vector3.up * height * 0.5f);
            depth = c.z;
            if (x1 - x0 < 2 * MinPickPixels) { float cx = (x0 + x1) / 2; x0 = cx - MinPickPixels; x1 = cx + MinPickPixels; }
            if (y1 - y0 < 2 * MinPickPixels) { float cy = (y0 + y1) / 2; y0 = cy - MinPickPixels; y1 = cy + MinPickPixels; }
            box = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        // Where a ray meets the ground: march, then halve the last step.
        public static bool GroundPoint(Ray ray, IGameBackend backend, out Vector3 at)
        {
            at = default;
            float step = 0.5f;
            for (float t = step; t < 2000f; t += step)
            {
                var p = ray.GetPoint(t);
                if (p.y <= backend.GroundHeight(p.x, p.z))
                {
                    float lo = t - step, hi = t;
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var q = ray.GetPoint(mid);
                        if (q.y <= backend.GroundHeight(q.x, q.z)) hi = mid; else lo = mid;
                    }
                    at = ray.GetPoint(hi);
                    return true;
                }
                if (t > 50f) step = 1f;
            }
            return false;
        }

        // The drag box, drawn from OnGUI.
        public void DrawBox()
        {
            Formation.DrawReadout();
            if (!dragging) return;
            var m = Input.mousePosition;
            if ((m - dragFrom).magnitude <= DragPixels) return;
            if (boxTex == null) { boxTex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave }; boxTex.SetPixel(0, 0, Color.white); boxTex.Apply(); }
            float x0 = Mathf.Min(m.x, dragFrom.x), x1 = Mathf.Max(m.x, dragFrom.x);
            float y0 = Screen.height - Mathf.Max(m.y, dragFrom.y), y1 = Screen.height - Mathf.Min(m.y, dragFrom.y);
            var old = GUI.color;
            GUI.color = new Color(0.5f, 1f, 0.5f, 0.15f);
            GUI.DrawTexture(new Rect(x0, y0, x1 - x0, y1 - y0), boxTex);
            GUI.color = new Color(0.5f, 1f, 0.5f, 0.9f);
            GUI.DrawTexture(new Rect(x0, y0, x1 - x0, 1), boxTex);
            GUI.DrawTexture(new Rect(x0, y1 - 1, x1 - x0, 1), boxTex);
            GUI.DrawTexture(new Rect(x0, y0, 1, y1 - y0), boxTex);
            GUI.DrawTexture(new Rect(x1 - 1, y0, 1, y1 - y0), boxTex);
            GUI.color = old;
        }
    }
}
