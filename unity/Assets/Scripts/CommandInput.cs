// CommandInput.cs - the player's hands: left click or drag to select blue
// units, shift to add, right click the ground to move or an enemy to
// attack, S to stop. Every order becomes command bytes sent to the relay.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class CommandInput : MonoBehaviour
    {
        const float ClickPixels = 6f;
        const float PickPixels = 28f;

        SimDriver driver;
        UnitPresenter presenter;
        readonly HashSet<int> selected = new HashSet<int>();
        readonly List<int> order = new List<int>();
        Vector2 dragStart;
        bool dragging;
        Transform marker;
        float markerUntil;

        void Awake()
        {
            driver = GetComponent<SimDriver>();
            driver.GameStarted += selected.Clear;
        }

        public bool IsSelected(int id) => selected.Contains(id);

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || driver.Sim == null) return;
            if (presenter == null) presenter = GetComponent<UnitPresenter>();
            var views = driver.Views;

            selected.RemoveWhere(id => id >= driver.ViewCount || views[id].state == OkNative.UnitDead);

            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                dragStart = mouse;
                dragging = true;
            }
            if (Input.GetMouseButtonUp(0) && dragging)
            {
                dragging = false;
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!add) selected.Clear();
                if ((mouse - dragStart).magnitude < ClickPixels)
                {
                    int id = Pick(cam, mouse, SimDriver.Human, true);
                    if (id >= 0) selected.Add(id);
                }
                else
                {
                    var box = Rect.MinMaxRect(Mathf.Min(dragStart.x, mouse.x), Mathf.Min(dragStart.y, mouse.y),
                                              Mathf.Max(dragStart.x, mouse.x), Mathf.Max(dragStart.y, mouse.y));
                    for (int i = 0; i < driver.ViewCount; i++)
                    {
                        if (views[i].player != SimDriver.Human || views[i].state == OkNative.UnitDead) continue;
                        Vector3 s = cam.WorldToScreenPoint(presenter.PositionOf(i) + Vector3.up * 0.6f);
                        if (s.z > 0 && box.Contains(new Vector2(s.x, s.y))) selected.Add(i);
                    }
                }
            }

            if (Input.GetMouseButtonDown(1) && selected.Count > 0)
            {
                int enemy = Pick(cam, mouse, SimDriver.Human, false);
                if (enemy >= 0)
                {
                    foreach (int id in selected) driver.Order(OkCmd.Attack(SimDriver.Human, id, enemy));
                    Mark(presenter.PositionOf(enemy), new Color(1f, 0.3f, 0.2f));
                }
                else if (Ground(cam, mouse, out Vector3 p))
                {
                    MoveInFormation(p);
                    Mark(p, new Color(0.3f, 1f, 0.3f));
                }
            }

            if (Input.GetKeyDown(KeyCode.S))
                foreach (int id in selected) driver.Order(OkCmd.Stop(SimDriver.Human, id));

            if (marker != null)
            {
                float left = markerUntil - Time.time;
                marker.gameObject.SetActive(left > 0);
                marker.localScale = new Vector3(1, 0.02f, 1) * Mathf.Max(0, left) * 2f;
            }
        }

        // The living unit nearest the cursor on screen, ours or theirs.
        int Pick(Camera cam, Vector2 mouse, int player, bool ours)
        {
            int best = -1;
            float bestD = PickPixels;
            var views = driver.Views;
            for (int i = 0; i < driver.ViewCount; i++)
            {
                if (views[i].state == OkNative.UnitDead) continue;
                if ((views[i].player == player) != ours) continue;
                Vector3 s = cam.WorldToScreenPoint(presenter.PositionOf(i) + Vector3.up * 0.6f);
                if (s.z <= 0) continue;
                float d = Vector2.Distance(mouse, new Vector2(s.x, s.y));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        static bool Ground(Camera cam, Vector2 mouse, out Vector3 p)
        {
            var ray = cam.ScreenPointToRay(mouse);
            p = default;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d)) return false;
            p = ray.GetPoint(d);
            return true;
        }

        // A square block centred on the click, one cell apart, so a group
        // does not fight over a single point.
        void MoveInFormation(Vector3 p)
        {
            order.Clear();
            order.AddRange(selected);
            order.Sort();
            int cols = Mathf.CeilToInt(Mathf.Sqrt(order.Count));
            float max = driver.mapCells - 0.5f;
            for (int k = 0; k < order.Count; k++)
            {
                float x = p.x + (k % cols - (cols - 1) / 2f);
                float z = p.z + (k / cols - (cols - 1) / 2f);
                driver.Order(OkCmd.Move(SimDriver.Human, order[k], Mathf.Clamp(x, 0.5f, max), Mathf.Clamp(z, 0.5f, max)));
            }
        }

        void Mark(Vector3 p, Color c)
        {
            if (marker == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>());
                go.name = "order marker";
                marker = go.transform;
            }
            ArtLibrary.Tint(marker.GetComponent<Renderer>().material, c);
            marker.position = p + Vector3.up * 0.03f;
            markerUntil = Time.time + 0.4f;
        }

        void OnGUI()
        {
            if (!dragging) return;
            Vector2 mouse = Input.mousePosition;
            if ((mouse - dragStart).magnitude < ClickPixels) return;
            var r = Rect.MinMaxRect(Mathf.Min(dragStart.x, mouse.x), Screen.height - Mathf.Max(dragStart.y, mouse.y),
                                    Mathf.Max(dragStart.x, mouse.x), Screen.height - Mathf.Min(dragStart.y, mouse.y));
            GUI.color = new Color(0.3f, 1f, 0.3f, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnDestroy()
        {
            if (driver != null) driver.GameStarted -= selected.Clear;
        }
    }
}
