// UnitPresenter.cs - draws the units: a body per unit id, smoothed between
// ticks, a team disc, a selection ring, health bars, arrows and blows.
// Reads the sim's snapshots and events and never changes the sim.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class UnitPresenter : MonoBehaviour
    {
        sealed class Body
        {
            public Transform root, pose, model;
            public GameObject ring;
            public float hit, lunge, deadAt = -1;
        }

        sealed class Tracer
        {
            public LineRenderer line;
            public float until;
        }

        SimDriver driver;
        CommandInput input;
        Transform unitsRoot;
        Material lineMaterial;
        readonly List<Body> bodies = new List<Body>();
        readonly List<Tracer> tracers = new List<Tracer>();

        void Awake()
        {
            driver = GetComponent<SimDriver>();
            driver.SimEvent += OnSimEvent;
            driver.GameStarted += Clear;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) lineMaterial = new Material(shader);
        }

        void Clear()
        {
            if (unitsRoot != null) Destroy(unitsRoot.gameObject);
            unitsRoot = new GameObject("Units").transform;
            bodies.Clear();
        }

        public static Vector3 ToWorld(int x, int y) => new Vector3(OkSim.ToCells(x), 0, OkSim.ToCells(y));

        // Sim heading turns from +x toward +y. A Unity model faces +Z.
        public static float Yaw(int heading) => 90f - heading * 360f / 65536f;

        public Vector3 PositionOf(int id) =>
            id >= 0 && id < bodies.Count ? bodies[id].root.position : Vector3.zero;

        Body Create(OkUnitView v)
        {
            var b = new Body();
            b.root = new GameObject($"unit {v.id}").transform;
            b.root.SetParent(unitsRoot, false);
            var team = SimDriver.TeamColors[Mathf.Clamp(v.player, 0, SimDriver.TeamColors.Length - 1)];

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(b.root, false);
            disc.transform.localScale = new Vector3(0.8f, 0.01f, 0.8f);
            disc.transform.localPosition = new Vector3(0, 0.01f, 0);
            ArtLibrary.Tint(disc.GetComponent<Renderer>().material, team * 0.8f);

            b.ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(b.ring.GetComponent<Collider>());
            b.ring.transform.SetParent(b.root, false);
            b.ring.transform.localScale = new Vector3(1.05f, 0.005f, 1.05f);
            ArtLibrary.Tint(b.ring.GetComponent<Renderer>().material, new Color(0.3f, 1f, 0.3f));
            b.ring.SetActive(false);

            // pose turns and lunges, model is what the art library built.
            b.pose = new GameObject("pose").transform;
            b.pose.SetParent(b.root, false);
            b.model = new GameObject("visual").transform;
            b.model.SetParent(b.pose, false);
            ArtLibrary.Build(b.model, v.kind, team);
            return b;
        }

        void LateUpdate()
        {
            if (unitsRoot == null) Clear();
            if (input == null) input = GetComponent<CommandInput>();
            var views = driver.Views;
            var prev = driver.PrevViews;
            float a = driver.Alpha;
            while (bodies.Count < driver.ViewCount) bodies.Add(Create(views[bodies.Count]));

            for (int i = 0; i < driver.ViewCount; i++)
            {
                var v = views[i];
                var b = bodies[i];
                Vector3 now = ToWorld(v.x, v.y);
                float yaw = Yaw(v.heading);
                if (i < driver.PrevCount)
                {
                    now = Vector3.Lerp(ToWorld(prev[i].x, prev[i].y), now, a);
                    yaw = Mathf.LerpAngle(Yaw(prev[i].heading), yaw, a);
                }
                b.root.position = now;
                b.pose.localRotation = Quaternion.Euler(0, yaw, 0);

                if (v.state == OkNative.UnitDead)
                {
                    if (b.deadAt < 0) b.deadAt = Time.time;
                    float t = Time.time - b.deadAt;
                    b.ring.SetActive(false);
                    b.model.localRotation = Quaternion.Euler(Mathf.Min(1f, t / 0.4f) * 85f, 0, 0);
                    b.model.localPosition = new Vector3(0, -Mathf.Max(0, t - 1.5f) * 0.5f, 0);
                    if (t > 4f && b.root.gameObject.activeSelf) b.root.gameObject.SetActive(false);
                    continue;
                }

                b.ring.SetActive(input != null && input.IsSelected(i));
                b.hit = Mathf.Max(0, b.hit - Time.deltaTime * 5f);
                b.lunge = Mathf.Max(0, b.lunge - Time.deltaTime * 4f);
                b.model.localScale = Vector3.one * (1f + 0.15f * b.hit);
                b.model.localPosition = new Vector3(0, 0, 0.3f * Mathf.Sin(b.lunge * Mathf.PI));
            }

            for (int i = tracers.Count - 1; i >= 0; i--)
                if (Time.time > tracers[i].until) tracers[i].line.enabled = false;
        }

        void OnSimEvent(OkEvent e)
        {
            if (e.kind != OkNative.EventAttacked) return;
            if (e.unit >= bodies.Count || e.a >= bodies.Count) return;
            bodies[e.a].hit = 1f;
            var attacker = driver.Views[e.unit];
            if (attacker.kind == OkNative.KindArcher)
                Shoot(ToWorld(attacker.x, attacker.y), ToWorld(driver.Views[e.a].x, driver.Views[e.a].y));
            else
                bodies[e.unit].lunge = 1f;
        }

        void Shoot(Vector3 from, Vector3 to)
        {
            Tracer t = null;
            foreach (var x in tracers)
                if (!x.line.enabled) { t = x; break; }
            if (t == null)
            {
                var go = new GameObject("arrow");
                go.transform.SetParent(transform, false);
                t = new Tracer { line = go.AddComponent<LineRenderer>() };
                t.line.positionCount = 2;
                t.line.widthMultiplier = 0.06f;
                if (lineMaterial != null) t.line.sharedMaterial = lineMaterial;
                t.line.startColor = new Color(1f, 0.95f, 0.7f);
                t.line.endColor = new Color(1f, 0.8f, 0.3f);
                tracers.Add(t);
            }
            t.line.SetPosition(0, from + Vector3.up * 1.0f);
            t.line.SetPosition(1, to + Vector3.up * 0.8f);
            t.line.enabled = true;
            t.until = Time.time + 0.15f;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            var views = driver.Views;
            for (int i = 0; i < driver.ViewCount && i < bodies.Count; i++)
            {
                var v = views[i];
                if (v.state == OkNative.UnitDead || v.maxHp <= 0) continue;
                bool selected = input != null && input.IsSelected(i);
                if (v.hp >= v.maxHp && !selected) continue;
                Vector3 s = cam.WorldToScreenPoint(bodies[i].root.position + Vector3.up * 1.7f);
                if (s.z < 0) continue;
                float w = 32, h = 4, frac = Mathf.Clamp01(v.hp / (float)v.maxHp);
                var r = new Rect(s.x - w / 2, Screen.height - s.y, w, h);
                GUI.color = Color.black;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.Lerp(Color.red, Color.green, frac);
                GUI.DrawTexture(new Rect(r.x, r.y, w * frac, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        void OnDestroy()
        {
            if (driver != null)
            {
                driver.SimEvent -= OnSimEvent;
                driver.GameStarted -= Clear;
            }
        }
    }
}
