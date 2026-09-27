// SimDriver.cs - the demo game. Builds the scene from code, runs the core
// at a fixed 30 Hz through local lockstep, and lets a wave AI attack the
// player. Press Play in any scene, even an empty one, and it starts.
using System;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class SimDriver : MonoBehaviour
    {
        public const int Human = 0, Computer = 1;

        public uint seed = 7;
        public int mapCells = 64;
        public int aiWaves = 6;

        // Blocked ground, in cells. The sim and the scene are both built
        // from this list, so what you see is what the units path around.
        public static readonly RectInt[] Obstacles =
        {
            new RectInt(6, 30, 24, 3),
            new RectInt(34, 30, 24, 3),
            new RectInt(14, 14, 4, 4),
            new RectInt(46, 46, 4, 4),
            new RectInt(44, 12, 3, 6),
            new RectInt(17, 46, 3, 6),
        };
        public static readonly Vector2Int HumanBase = new Vector2Int(8, 8);
        public static readonly Vector2Int ComputerBase = new Vector2Int(56, 56);

        public static readonly Color[] TeamColors =
        {
            new Color(0.2f, 0.5f, 1f),
            new Color(0.95f, 0.25f, 0.2f),
        };

        public OkSim Sim { get; private set; }
        public OkUnitView[] Views { get; } = new OkUnitView[OkNative.MaxUnits];
        public OkUnitView[] PrevViews { get; } = new OkUnitView[OkNative.MaxUnits];
        public int ViewCount { get; private set; }
        public int PrevCount { get; private set; }
        // How far the render frame sits between the last two ticks.
        public float Alpha { get; private set; }
        public event Action<OkEvent> SimEvent;
        public event Action GameStarted;

        OkLocalLockstep lockstep;
        OkAi ai;
        readonly OkEvent[] events = new OkEvent[512];
        float accumulator;
        int lastWavesLeft;
        string outcome;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<SimDriver>() == null)
                new GameObject("SimDriver").AddComponent<SimDriver>();
        }

        void Awake()
        {
            BuildScene();
            gameObject.AddComponent<UnitPresenter>();
            gameObject.AddComponent<CommandInput>();
        }

        void Start() => NewGame();

        void BuildScene()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            // Not ??, which misses Unity's fake null in the editor.
            var rig = cam.GetComponent<RtsCamera>();
            if (rig == null) rig = cam.gameObject.AddComponent<RtsCamera>();
            rig.mapSize = mapCells;
            rig.focus = new Vector3(HumanBase.x + 8, 0, HumanBase.y + 8);

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(mapCells / 10f, 1, mapCells / 10f);
            ground.transform.position = new Vector3(mapCells / 2f, 0, mapCells / 2f);
            ground.GetComponent<Renderer>().material.color = new Color(0.33f, 0.45f, 0.28f);

            var rocks = new GameObject("Obstacles").transform;
            foreach (var r in Obstacles)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(block.GetComponent<Collider>());
                block.transform.SetParent(rocks);
                block.transform.localScale = new Vector3(r.width, 1.5f, r.height);
                block.transform.position = new Vector3(r.x + r.width / 2f, 0.75f, r.y + r.height / 2f);
                block.GetComponent<Renderer>().material.color = new Color(0.45f, 0.4f, 0.36f);
            }
        }

        public void NewGame()
        {
            EndGame();
            Sim = new OkSim(seed, mapCells, mapCells);
            foreach (var r in Obstacles)
                for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++)
                        Sim.SetBlocked(x, y, true);
            lockstep = new OkLocalLockstep(Sim);
            ai = new OkAi(Computer, ComputerBase.x, ComputerBase.y, aiWaves);
            lastWavesLeft = ai.WavesLeft;
            outcome = null;
            accumulator = 0;
            ViewCount = PrevCount = 0;

            for (int i = 0; i < 12; i++)
            {
                int kind = i % 3 == 2 ? OkNative.KindArcher : OkNative.KindSoldier;
                Order(OkCmd.Spawn(Human, kind, HumanBase.x + 0.5f + i % 4, HumanBase.y + 0.5f + i / 4));
            }
            GameStarted?.Invoke();
        }

        void EndGame()
        {
            lockstep?.Dispose();
            ai?.Dispose();
            Sim?.Dispose();
            lockstep = null;
            ai = null;
            Sim = null;
        }

        // Every order from the local player goes through the relay.
        public void Order(byte[] cmd) => lockstep?.Send(Human, cmd);

        void BeforeTurn()
        {
            ai.Think(Sim, cmd => lockstep.Send(Computer, cmd));
            // Each wave the computer raises, the player gets a few more.
            if (ai.WavesLeft < lastWavesLeft)
            {
                lastWavesLeft = ai.WavesLeft;
                for (int i = 0; i < 3; i++)
                {
                    int kind = i == 2 ? OkNative.KindArcher : OkNative.KindSoldier;
                    Order(OkCmd.Spawn(Human, kind, HumanBase.x + 0.5f + i, HumanBase.y - 3.5f));
                }
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R) && outcome != null) NewGame();
            if (Sim == null) return;

            // Fixed steps, as lockstep runs them. Drawing reads snapshots
            // and never feeds anything back.
            const float dt = 1f / OkNative.TickRate;
            accumulator += Time.deltaTime;
            int steps = 0;
            while (accumulator >= dt && steps < 5 && outcome == null)
            {
                lockstep.Tick(BeforeTurn);
                Array.Copy(Views, PrevViews, ViewCount);
                PrevCount = ViewCount;
                ViewCount = Math.Min(Sim.Snapshot(Views), Views.Length);
                int n = Sim.DrainEvents(events);
                for (int i = 0; i < n; i++) SimEvent?.Invoke(events[i]);
                accumulator -= dt;
                steps++;
                CheckOutcome();
            }
            if (steps == 5 || outcome != null) accumulator = 0;
            Alpha = Mathf.Clamp01(accumulator / dt);
        }

        void CheckOutcome()
        {
            if (Sim.Tick < 10) return;
            int mine = CountAlive(Human), theirs = CountAlive(Computer);
            if (mine == 0) outcome = "Defeat";
            else if (theirs == 0 && ai.WavesLeft == 0) outcome = "Victory";
        }

        public int CountAlive(int player)
        {
            int n = 0;
            for (int i = 0; i < ViewCount; i++)
                if (Views[i].player == player && Views[i].state != OkNative.UnitDead) n++;
            return n;
        }

        void OnGUI()
        {
            if (Sim == null) return;
            GUI.color = Color.white;
            string wave = ai.WavesLeft > 0 ? $"{ai.WavesLeft} waves to come" : "last wave sent";
            GUI.Label(new Rect(10, 10, 600, 20),
                $"tick {Sim.Tick}   turn {lockstep.Turns}   hash {Sim.Hash:x16}");
            GUI.Label(new Rect(10, 28, 600, 20),
                $"blue {CountAlive(Human)}   red {CountAlive(Computer)}   {wave}");
            GUI.Label(new Rect(10, Screen.height - 26, 900, 20),
                "left drag: select   right click: move or attack   S: stop   arrows or screen edge: pan   wheel: zoom");
            if (outcome != null)
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height / 2f - 60, Screen.width, 60), outcome, style);
                GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 30), "press R to play again",
                    new GUIStyle(style) { fontSize = 18 });
            }
        }

        void OnDestroy() => EndGame();
    }
}
