// SimDriver.cs - the smallest game loop over the plugin: a fixed 30 Hz
// sim, a capsule per unit, and a right click that sends every unit of
// player 0 to the clicked ground. Drop it on an empty GameObject in a
// scene with a camera looking down at the XZ plane.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class SimDriver : MonoBehaviour
    {
        public uint seed = 7;
        public int mapCells = 64;
        public int unitsPerPlayer = 10;

        OkSim sim;
        OkUnitView[] views = new OkUnitView[4096];
        OkEvent[] events = new OkEvent[256];
        readonly List<Transform> bodies = new List<Transform>();
        float accumulator;

        void Start()
        {
            sim = new OkSim(seed, mapCells, mapCells);
            for (int i = 0; i < unitsPerPlayer; i++)
            {
                sim.Spawn(0, 8 + i, 8);
                sim.Spawn(1, 8 + i, mapCells - 8);
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(mapCells / 10f, 1, mapCells / 10f);
            ground.transform.position = new Vector3(mapCells / 2f, 0, mapCells / 2f);
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(1) && Camera.main != null)
            {
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
                {
                    Vector3 p = ray.GetPoint(d);
                    int n = sim.Snapshot(views);
                    for (int i = 0; i < n; i++)
                        if (views[i].player == 0) sim.Move(0, views[i].id, p.x, p.z);
                }
            }

            // Fixed steps, as lockstep will run them. Rendering reads the
            // latest snapshot and does not feed anything back.
            accumulator += Time.deltaTime;
            float dt = 1f / OkNative.TickRate;
            while (accumulator >= dt)
            {
                sim.Step();
                accumulator -= dt;
            }

            int count = sim.Snapshot(views);
            while (bodies.Count < count)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
                body.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                bodies.Add(body);
            }
            for (int i = 0; i < count; i++)
            {
                var v = views[i];
                bodies[i].position = new Vector3(OkSim.ToCells(v.x), 0.6f, OkSim.ToCells(v.y));
                bodies[i].rotation = Quaternion.Euler(0, -v.heading * 360f / 65536f + 90f, 0);
                var r = bodies[i].GetComponent<Renderer>();
                if (r != null) r.material.color = v.player == 0 ? Color.cyan : Color.red;
            }

            int ne = sim.DrainEvents(events);
            for (int i = 0; i < ne; i++)
                if (events[i].kind == OkNative.EventArrived)
                    Debug.Log($"unit {events[i].unit} arrived at tick {events[i].tick}");
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 400, 20), $"tick {sim?.Tick}  hash {sim?.Hash:x16}");
        }

        void OnDestroy() => sim?.Dispose();
    }
}
