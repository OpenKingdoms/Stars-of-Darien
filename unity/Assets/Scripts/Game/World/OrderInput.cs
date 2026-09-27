// OrderInput.cs - the mouse and keys in a running game. Left click or
// drag selects the player's units (Shift adds). Right click on an enemy
// attacks it and on the ground moves there in a block. A command armed
// from the HUD or a key (M move, A attack, P patrol, G guard) waits for
// the next left click, and a build armed from the build menu shows its
// ghost on the ground, green where it can stand. S stops, Escape or a
// right click disarms.
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

        // The command waiting for a click, if any.
        public CommandKind? Armed { get; private set; }
        public int ArmedDef { get; private set; } = -1;
        public Vector3 GhostAt { get; private set; }
        public bool GhostOk { get; private set; }

        public OrderInput(IGameBackend backend, WorldView world)
        {
            this.backend = backend;
            this.world = world;
        }

        public HashSet<int> Selected => world.Entities.Selected;

        public void Arm(CommandKind kind, int def = -1)
        {
            Armed = kind;
            ArmedDef = def;
        }

        public void Disarm()
        {
            Armed = null;
            ArmedDef = -1;
            world.Entities.Ghost = null;
        }

        static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public void Update()
        {
            var cam = world.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            if (cam == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var m = Input.mousePosition;
            var units = world.Entities.Units;
            int count = world.Entities.UnitCount;
            int me = backend.LocalPlayer;

            Keys();
            if (Armed != null) { UpdateArmed(cam, m, overUi, units, count); return; }

            if (Input.GetMouseButtonDown(0) && !overUi) { dragFrom = m; dragging = true; }
            if (Input.GetMouseButtonUp(0) && dragging)
            {
                dragging = false;
                if (!Shift) Selected.Clear();
                if ((m - dragFrom).magnitude > DragPixels)
                {
                    var box = Rect.MinMaxRect(Mathf.Min(m.x, dragFrom.x), Mathf.Min(m.y, dragFrom.y), Mathf.Max(m.x, dragFrom.x), Mathf.Max(m.y, dragFrom.y));
                    for (int i = 0; i < count; i++)
                    {
                        if (units[i].Player != me || (units[i].Flags & UnitFlags.Dying) != 0) continue;
                        var s = cam.WorldToScreenPoint(units[i].Position + Vector3.up * 0.6f);
                        if (s.z > 0 && box.Contains(s)) Selected.Add(units[i].Handle);
                    }
                }
                else
                {
                    int hit = Pick(cam, m, units, count, true);
                    if (hit >= 0) Selected.Add(hit);
                }
            }

            if (Input.GetMouseButtonDown(1) && !overUi && Selected.Count > 0)
            {
                int enemy = Pick(cam, m, units, count, false);
                if (enemy >= 0) OrderAll(CommandKind.Attack, Vector3.zero, enemy);
                else if (GroundPoint(cam.ScreenPointToRay(m), backend, out var at)) MoveBlock(CommandKind.Move, at);
            }

            // Forget units that are gone.
            if (Time.frameCount % 30 == 0 && Selected.Count > 0)
            {
                var alive = new HashSet<int>();
                for (int i = 0; i < count; i++) if ((units[i].Flags & UnitFlags.Dying) == 0) alive.Add(units[i].Handle);
                Selected.IntersectWith(alive);
            }
        }

        void Keys()
        {
            if (Selected.Count == 0) return;
            if (Input.GetKeyDown(KeyCode.S)) { Stop(); Disarm(); }
            if (Input.GetKeyDown(KeyCode.M)) Arm(CommandKind.Move);
            if (Input.GetKeyDown(KeyCode.A)) Arm(CommandKind.Attack);
            if (Input.GetKeyDown(KeyCode.P)) Arm(CommandKind.Patrol);
            if (Input.GetKeyDown(KeyCode.G)) Arm(CommandKind.Guard);
        }

        public void Stop()
        {
            foreach (var h in Selected) backend.Command(GameCommand.To(CommandKind.Stop, h, Vector3.zero));
        }

        void UpdateArmed(Camera cam, Vector3 m, bool overUi, UnitState[] units, int count)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetMouseButtonDown(1) && !overUi)) { Disarm(); return; }
            bool onGround = GroundPoint(cam.ScreenPointToRay(m), backend, out var at);

            if (Armed == CommandKind.Build)
            {
                GhostOk = false;
                if (onGround)
                {
                    GhostOk = backend.CanBuildAt(ArmedDef, at, out var snapped);
                    GhostAt = snapped;
                }
                world.Entities.Ghost = onGround ? new EntityRenderer.GhostState { Def = ArmedDef, At = GhostAt, Ok = GhostOk } : (EntityRenderer.GhostState?)null;
                if (Input.GetMouseButtonDown(0) && !overUi && GhostOk)
                {
                    foreach (var h in Selected)
                        backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = h, Target = GhostAt, TargetUnit = -1, BuildDef = ArmedDef, Queue = Shift });
                    if (!Shift) Disarm();
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0) || overUi) return;
            var kind = Armed.Value;
            int target = Pick(cam, m, units, count, kind == CommandKind.Guard);
            if (kind == CommandKind.Attack && target < 0) target = Pick(cam, m, units, count, false);
            if (target >= 0 && (kind == CommandKind.Attack || kind == CommandKind.Guard)) OrderAll(kind, Vector3.zero, target);
            else if (onGround)
            {
                if (kind == CommandKind.Attack) OrderAll(CommandKind.AttackGround, at, -1);
                else MoveBlock(kind, at);
            }
            if (!Shift) Disarm();
        }

        void OrderAll(CommandKind kind, Vector3 at, int target)
        {
            foreach (var h in Selected)
                backend.Command(new GameCommand { Kind = kind, Unit = h, Target = at, TargetUnit = target, BuildDef = -1, Queue = Shift });
        }

        // Moves in a square block around the point, turned with the camera.
        public void MoveBlock(CommandKind kind, Vector3 at)
        {
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

        int Pick(Camera cam, Vector3 m, UnitState[] units, int count, bool own)
        {
            int best = -1;
            float bestD = PickPixels;
            int me = backend.LocalPlayer;
            for (int i = 0; i < count; i++)
            {
                if ((units[i].Flags & UnitFlags.Dying) != 0 || (units[i].Player == me) != own) continue;
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
