// GameRoot.cs - the remaster's game: owns the backend, the screen flow,
// the options and the world view, runs loading a slice per frame, and
// runs the simulation at its own tick rate while a game is on. Put it in
// a scene (Assets/Scenes/Remaster.unity has one) or call GameRoot.Boot().
using System;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed class GameRoot : MonoBehaviour
    {
        // The engine binding sets this when the engine and game files are
        // there. Without it, the mock runs.
        public static Func<IGameBackend> BackendFactory;

        public IGameBackend Backend { get; private set; }
        public GameFlow Flow { get; } = new GameFlow();
        public GameOptions Options { get; private set; }
        public SkirmishSetup Setup { get; set; }
        public WorldView World { get; private set; }
        public LoadProgress Loading { get; private set; }
        public string LastError { get; private set; }
        public int FramesPlayed { get; private set; }
        public MenuScreens Screens { get; private set; }
        OrderInput input;
        float clock;
        IGameBackend injected;

        public static GameRoot Boot(IGameBackend backend = null)
        {
            var go = new GameObject("GameRoot");
            go.SetActive(false);
            var root = go.AddComponent<GameRoot>();
            root.injected = backend;
            go.SetActive(true);
            return root;
        }

        void Awake()
        {
            TakeOverScene();
            Backend = injected ?? BackendFactory?.Invoke() ?? new MockBackend();
            Options = GameOptions.Load();
            Setup = DefaultSetup(Backend);
            Flow.Changed += OnFlow;
            Screens = new MenuScreens(this);
            Screens.Show(Flow.State);
        }

        // Another game view may have booted into the scene before this one
        // (a test scene, or GameRoot.Boot after Play): the capsule demo or
        // the engine's own view. Only one runs, so take them and what they
        // built away, the engine's view first so its game ends before ours.
        static void TakeOverScene()
        {
            foreach (var name in new[] { "OpenKingdoms", "SimDriver" })
            {
                var go = GameObject.Find(name);
                if (go != null) DestroyImmediate(go);
            }
            foreach (var name in new[] { "Ground", "Obstacles", "Units", "Sun", "Terrain", "Features" })
            {
                var go = GameObject.Find(name);
                if (go != null && go.transform.parent == null) DestroyImmediate(go);
            }
            var cam = Camera.main;
            if (cam != null)
                foreach (var b in cam.GetComponents<MonoBehaviour>())
                    if (b != null && !(b is GameCamera)) DestroyImmediate(b);
        }

        public static SkirmishSetup DefaultSetup(IGameBackend b)
        {
            var s = new SkirmishSetup { MapId = b.Maps.Count > 0 ? b.Maps[0].Id : "", Seed = (uint)Environment.TickCount };
            string side0 = b.Sides.Count > 0 ? b.Sides[0].Id : "";
            string side1 = b.Sides.Count > 2 ? b.Sides[2].Id : side0;
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = side0, Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = side1, Colour = 1, Team = 1 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 4, Team = 2 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 5, Team = 3 });
            return s;
        }

        public MapInfo CurrentMap()
        {
            foreach (var m in Backend.Maps) if (m.Id == Setup.MapId) return m;
            return Backend.Maps.Count > 0 ? Backend.Maps[0] : null;
        }

        void OnFlow(FlowState was, FlowState now)
        {
            switch (now)
            {
                case FlowState.Loading:
                    LastError = null;
                    Loading = default;
                    Backend.StartSkirmish(Setup);
                    break;
                case FlowState.Playing:
                    clock = 0;
                    break;
                case FlowState.MainMenu:
                case FlowState.Skirmish:
                    if (World != null || GameFlow.InGame(was)) EndGame();
                    break;
                case FlowState.Quit:
                    EndGame();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
            Screens.Show(now);
        }

        void EndGame()
        {
            World?.Dispose();
            World = null;
            input = null;
            Backend.EndGame();
            FramesPlayed = 0;
        }

        void Update()
        {
            switch (Flow.State)
            {
                case FlowState.Loading:
                    Loading = Backend.PumpLoading();
                    if (Loading.Failed)
                    {
                        LastError = string.IsNullOrEmpty(Loading.Error) ? "The map could not be loaded." : Loading.Error;
                        Backend.EndGame();
                        Flow.Fire(FlowEvent.LoadFailed);
                    }
                    else if (Loading.Done)
                    {
                        World = new WorldView(Backend);
                        World.Build(CurrentMap(), Options);
                        input = new OrderInput(Backend, World);
                        Flow.Fire(FlowEvent.Loaded);
                    }
                    break;
                case FlowState.Playing:
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Pause)) { Flow.Fire(FlowEvent.Pause); break; }
                    RunSim(Time.deltaTime);
                    input?.Update();
                    FramesPlayed++;
                    if (Backend.Status == GameStatus.Victory) Flow.Fire(FlowEvent.Won);
                    else if (Backend.Status == GameStatus.Defeat) Flow.Fire(FlowEvent.Lost);
                    break;
                case FlowState.Paused:
                    if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Resume);
                    break;
                case FlowState.Options:
                case FlowState.Skirmish:
                    if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Back);
                    break;
            }
            if (World != null) World.Render();
            Screens.Tick();
        }

        void RunSim(float dt)
        {
            int tps = Mathf.Max(1, Backend.TicksPerSecond);
            clock += dt * Options.GameSpeed;
            int due = Mathf.FloorToInt(clock * tps);
            // After a stall, drop time rather than run a burst of ticks.
            if (due > 8) { due = 8; clock = 0; }
            else clock -= due / (float)tps;
            if (due > 0) Backend.Advance(due);
        }

        void OnGUI() => input?.DrawBox();

        void OnDestroy()
        {
            EndGame();
            Screens?.Dispose();
            Backend?.Dispose();
        }
    }
}
