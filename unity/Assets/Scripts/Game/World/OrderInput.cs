// OrderInput.cs - the mouse and keys in a running game, in one of two
// schemes. Classic plays like the original: the engine keeps the
// selection and decides what a left click means (select a friend, attack
// an enemy, move to the ground, or carry out an armed command), and a
// right click or Escape cancels. Modern keeps the selection here, selects
// with the left button and orders with the right, moving in a block.
// In both, a drag selects the player's units (Shift adds), M, A, P and G
// arm move, attack, patrol and guard, S stops, a build armed from the
// menu shows its ghost, green where it can stand, and Ctrl with a digit
// makes a group that the digit brings back.
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

        public OrderInput(IGameBackend backend, WorldView world, bool classic)
        {
            this.backend = backend;
            this.world = world;
            Classic = classic;
        }

        public HashSet<int> Selected => world.Entities.Selected;
        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        public void Arm(CommandKind kind, int def = -1)
        {
            Armed = kind;
            ArmedDef = def;
            if (Classic) backend.Arm(kind, def);
        }

        public void Disarm()
        {
            if (Armed != null && Classic) backend.Cancel();
            Armed = null;
            ArmedDef = -1;
            world.Entities.Ghost = null;
        }

        void DisarmHere()
        {
            Armed = null;
            ArmedDef = -1;
            world.Entities.Ghost = null;
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
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var m = Input.mousePosition;
            var units = world.Entities.Units;
            int count = world.Entities.UnitCount;
            Keys();

            bool onGround = OrderInput.GroundPoint(cam.ScreenPointToRay(m), backend, out var at);
            UpdateGhost(onGround, at);
            PointerOverUi = overUi;
            PointerOnGround = onGround;
            PointerAt = at;
            PointerUnit = overUi ? -1 : Pick(cam, m, units, count, null);

            if (Input.GetMouseButtonDown(1) && !overUi)
            {
                if (Classic) { backend.Cancel(); DisarmHere(); return; }
                if (Armed != null) { DisarmHere(); return; }
                if (Selected.Count > 0)
                {
                    int enemy = Pick(cam, m, units, count, false);
                    if (enemy >= 0) OrderAll(CommandKind.Attack, Vector3.zero, enemy);
                    else if (onGround) MoveBlock(CommandKind.Move, at);
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape) && Armed != null) { Disarm(); }

            if (Input.GetMouseButtonDown(0) && !overUi) { dragFrom = m; dragging = true; }
            if (!Input.GetMouseButtonUp(0) || !dragging) return;
            dragging = false;

            if ((m - dragFrom).magnitude > DragPixels && Armed == null)
            {
                var box = Rect.MinMaxRect(Mathf.Min(m.x, dragFrom.x), Mathf.Min(m.y, dragFrom.y), Mathf.Max(m.x, dragFrom.x), Mathf.Max(m.y, dragFrom.y));
                var inBox = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (units[i].Player != backend.LocalPlayer || (units[i].Flags & UnitFlags.Dying) != 0) continue;
                    var s = cam.WorldToScreenPoint(units[i].Position + Vector3.up * 0.6f);
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

            if (Classic)
            {
                int unit = Pick(cam, m, units, count, null);
                if (unit >= 0 || onGround)
                {
                    if (Armed == CommandKind.Build && !GhostOk) return;
                    backend.Click(Armed == CommandKind.Build ? GhostAt : at, Armed == CommandKind.Build ? -1 : unit, Shift);
                    if (Armed != null && !Shift) DisarmHere();
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

        // The pointer for what it is over: the game decides, as in the
        // original, except for a command armed only here by the modern
        // scheme.
        public GameCursor PointerCursor()
        {
            if (PointerOverUi || (PointerUnit < 0 && !PointerOnGround)) return GameCursor.Normal;
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
            if (Selected.Count == 0) return;
            // A and S pan the camera, so attack and stop take Ctrl.
            if (Input.GetKeyDown(KeyCode.S) && Ctrl) { Stop(); Disarm(); }
            if (Input.GetKeyDown(KeyCode.M)) Arm(CommandKind.Move);
            if (Input.GetKeyDown(KeyCode.A) && Ctrl) Arm(CommandKind.Attack);
            if (Input.GetKeyDown(KeyCode.P)) Arm(CommandKind.Patrol);
            if (Input.GetKeyDown(KeyCode.G)) Arm(CommandKind.Guard);
        }

        void UpdateGhost(bool onGround, Vector3 at)
        {
            if (Armed != CommandKind.Build) { world.Entities.Ghost = null; return; }
            GhostOk = false;
            if (onGround)
            {
                GhostOk = backend.CanBuildAt(ArmedDef, at, out var snapped);
                GhostAt = snapped;
            }
            world.Entities.Ghost = onGround ? new EntityRenderer.GhostState { Def = ArmedDef, At = GhostAt, Ok = GhostOk } : (EntityRenderer.GhostState?)null;
        }

        // The modern scheme's armed command, sent unit by unit.
        void CarryOut(Camera cam, Vector3 m, UnitState[] units, int count, bool onGround, Vector3 at)
        {
            var kind = Armed.Value;
            if (kind == CommandKind.Build)
            {
                if (!GhostOk) return;
                foreach (var h in Selected)
                    backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = h, Target = GhostAt, TargetUnit = -1, BuildDef = ArmedDef, Queue = Shift });
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
            if (!Shift) DisarmHere();
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
        int Pick(Camera cam, Vector3 m, UnitState[] units, int count, bool? own)
        {
            int best = -1;
            float bestD = PickPixels;
            int me = backend.LocalPlayer;
            for (int i = 0; i < count; i++)
            {
                if ((units[i].Flags & UnitFlags.Dying) != 0) continue;
                if (own != null && (units[i].Player == me) != own.Value) continue;
                var s = cam.WorldToScreenPoint(units[i].Position + Vector3.up * 0.6f);
                if (s.z <= 0) continue;
                float d = Vector2.Distance(s, m);
                if (d < bestD) { bestD = d; best = units[i].Handle; }
            }
            return best;
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
