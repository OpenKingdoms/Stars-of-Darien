// GameRoot.cs - the remaster's game: owns the backend, the screen flow,
// the options and the world view, runs loading a slice per frame, and
// runs the simulation at its own tick rate while a game is on. Put it in
// a scene (Assets/Scenes/Remaster.unity has one) or call GameRoot.Boot().
using System;
using System.Collections.Generic;
using System.IO;
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
        // The binding sets these in a built player: the game folder in use,
        // and the way Options picks another.
        public static Func<string> GameFolder;
        public static Action ChangeGameFolder;

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
        public OrderInput Orders => input;
        public MapEditTool Editor { get; private set; }
        public GameCursorView Pointer { get; private set; }
        float clock, loadingFrom, builtFrom;
        int buildTotal;
        long loadFrameAt;

        // The engine's loading fills this much of the bar, and the world's
        // build the rest. BuildSliceMs is about how long a frame builds.
        public const float EngineShare = 0.4f;
        public const string LandStage = "Laying out the land", PreparingStage = "Preparing the kingdoms' models";
        public static double BuildSliceMs = 25;

        // The longest frame of the last load, for the log and the map sweep.
        public double LoadWorstFrameMs { get; private set; }
        string pendingLoad;
        bool loadRefused;
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

        public const string Title = "Darien Reforged";

        // Saves and maps made before the game had its name lived under
        // DefaultCompany/unity. They move to the new folder once.
        static void MoveOldData()
        {
            string now = Application.persistentDataPath;
            string parent = Path.GetDirectoryName(Path.GetDirectoryName(now));
            MoveOldData(Path.Combine(parent, "DefaultCompany", "unity"), now);
        }

        public const string MovedMark = "MOVED.txt";

        // Only the game's own Saves and User folders move, since any Unity
        // project left with the default names writes to the old folder too,
        // and MOVED.txt there says it was done. Returns the files moved.
        public static int MoveOldData(string old, string now)
        {
            int moved = 0;
            try
            {
                if (!Directory.Exists(old) || File.Exists(Path.Combine(old, MovedMark))) return 0;
                if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(now), StringComparison.OrdinalIgnoreCase)) return 0;
                foreach (var part in new[] { "Saves", "User" })
                {
                    string from = Path.Combine(old, part);
                    if (!Directory.Exists(from)) continue;
                    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    {
                        string to = Path.Combine(now, file.Substring(old.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (File.Exists(to)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Move(file, to);
                        moved++;
                    }
                }
                File.WriteAllText(Path.Combine(old, MovedMark), "Moved to " + now);
            }
            catch (Exception e) { Debug.LogWarning("The old saves were not moved: " + e.Message); }
            return moved;
        }

        Func<string, bool> playSound;

        void Awake()
        {
            MoveOldData();
            TakeOverScene();
            Backend = injected ?? StartBackend();
            GlbLoader.SetPainter(Backend);
            Options = GameOptions.Load();
            UiKit.Sound = playSound = wav => Backend != null && Backend.PlaySound(wav, Options.Volume);
            Setup = DefaultSetup(Backend);
            Pointer = new GameCursorView(Backend.CursorArt, Options.CursorScale);
            Flow.Changed += OnFlow;
            Screens = new MenuScreens(this);
            Screens.Show(Flow.State);
        }

        // Another game view may have booted into the scene before this one
        // (a test scene, or GameRoot.Boot after Play): the engine's own view
        // or the notice that it cannot run. Only one runs, so take them and
        // what they built away, the engine's view first so its game ends
        // before ours.
        static void TakeOverScene()
        {
            foreach (var name in new[] { "OpenKingdoms", "EngineNotice" })
            {
                var go = GameObject.Find(name);
                if (go != null) DestroyImmediate(go);
            }
            foreach (var name in new[] { "Sun", "Terrain", "Features" })
            {
                var go = GameObject.Find(name);
                if (go != null && go.transform.parent == null) DestroyImmediate(go);
            }
            var cam = Camera.main;
            if (cam != null)
                foreach (var b in cam.GetComponents<MonoBehaviour>())
                    if (b != null && !(b is GameCamera)) DestroyImmediate(b);
        }

        // The Map Browser's Play button leaves a map here, and the next
        // game starts straight on it, skipping the menus.
        public const string AutoStartKey = "oku.autostart.map";

        // For the studio's Play here: AutoStarting may change the setup of
        // an auto-started game, and WorldLoaded runs once a world is built.
        public static Action<GameRoot> AutoStarting, WorldLoaded;

        void Start()
        {
            if (QaRun.Begin(this, Environment.GetCommandLineArgs())) return;
            if (SmokeRun.Seconds(Environment.GetCommandLineArgs()) is float smoke)
            {
                SmokeRun.Begin(this, smoke);
                return;
            }
            string map = PlayerPrefs.GetString(AutoStartKey, "");
            if (map.Length == 0) return;
            PlayerPrefs.DeleteKey(AutoStartKey);
            PlayerPrefs.Save();
            foreach (var m in Backend.Maps)
            {
                if (m.Id != map) continue;
                Flow.Fire(FlowEvent.OpenSkirmish);
                Setup.MapId = map;
                AutoStarting?.Invoke(this);
                Screens.StartGame();
                return;
            }
            Debug.LogWarning("Map Browser asked for " + map + ", which this engine does not have.");
        }

        // Why the engine did not start, shown on the main menu, or null.
        public string BackendProblem { get; private set; }

        // The engine when it starts, the mock with the reason when it does
        // not, so a failed start never leaves the game without its menus.
        IGameBackend StartBackend()
        {
            if (BackendFactory == null) return new MockBackend();
            try
            {
                var b = BackendFactory();
                if (b != null) return b;
                BackendProblem = "The engine did not start.";
            }
            catch (Exception e)
            {
                BackendProblem = "The engine did not start: " + (e.InnerException ?? e).Message;
                Debug.LogWarning(BackendProblem);
            }
            return new MockBackend();
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
                    loadingFrom = Time.realtimeSinceStartup;
                    LoadWorstFrameMs = 0;
                    loadFrameAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    ApplyAudio();
                    if (pendingLoad != null)
                    {
                        // A refused save leaves the flow to Update, not this handler.
                        loadRefused = !Backend.LoadGame(pendingLoad);
                        pendingLoad = null;
                    }
                    else Backend.StartSkirmish(Setup);
                    break;
                case FlowState.Playing:
                    clock = 0;
                    break;
                case FlowState.Editing:
                    Editor = new MapEditTool(Backend, World);
                    break;
                case FlowState.MainMenu:
                case FlowState.Skirmish:
                    if (now == FlowState.MainMenu) LastError = null;
                    if (World != null || GameFlow.InGame(was)) EndGame();
                    if (now == FlowState.MainMenu) EndRooms();
                    break;
                case FlowState.Multiplayer:
                    if (was == FlowState.Room || was == FlowState.MapChoice || was == FlowState.Loading) Backend.Rooms?.LeaveRoom();
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

        // Leaving the lobby for the menu hangs up on the relay, and the
        // skirmish setup comes back from before a room's battle.
        void EndRooms()
        {
            netMatch = false;
            var rooms = Backend.Rooms;
            if (rooms != null && rooms.State != RoomSession.Off) rooms.Disconnect();
            if (skirmishSetup != null) { Setup = skirmishSetup; skirmishSetup = null; }
        }

        bool netMatch;
        SkirmishSetup skirmishSetup;

        // The relay said go: the room's battle loads.
        void StartRoomBattle(RoomState room)
        {
            MapInfo map = null;
            foreach (var m in Backend.Maps) if (m.Id == room.MapId) map = m;
            skirmishSetup = skirmishSetup ?? Setup;
            Setup = RoomSetup.ToSkirmish(room, map, (uint)(room.Id * 2654435761u));
            netMatch = true;
            if (Flow.State == FlowState.MapChoice || Flow.State == FlowState.Options) Flow.Fire(FlowEvent.Back);
            Flow.Fire(FlowEvent.Start);
        }

        void EndGame()
        {
            Editor = null;
            if (World != null) World.Entities.Brush = null;
            World?.Dispose();
            World = null;
            input = null;
            Backend?.EndGame();
            FramesPlayed = 0;
        }

        void Update()
        {
            // Scripts reloaded during Play leave this component without its
            // parts. Stop cleanly, once, rather than fail every frame.
            if (Screens == null || Backend == null || Flow == null)
            {
                Debug.LogWarning(Title + ": scripts were reloaded during Play, so the game stops. Press Play again.");
                enabled = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                return;
            }
            // Each frame from the start of a load to the first after it.
            if (loadFrameAt != 0)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                LoadWorstFrameMs = System.Math.Max(LoadWorstFrameMs, Ms(loadFrameAt));
                loadFrameAt = Flow.State == FlowState.Loading ? now : 0;
                if (loadFrameAt == 0) Debug.Log($"{CurrentMap()?.Id}: the slowest frame while it loaded took {LoadWorstFrameMs:0} ms");
            }
            switch (Flow.State)
            {
                case FlowState.Loading:
                    if (loadRefused)
                    {
                        loadRefused = false;
                        LastError = "That saved game could not be read.";
                        Flow.Fire(FlowEvent.LoadFailed);
                        break;
                    }
                    if (World == null)
                    {
                        var engine = Backend.PumpLoading();
                        Loading = new LoadProgress
                        {
                            Fraction = engine.Fraction * EngineShare, Stage = engine.Stage, Done = engine.Done, Failed = engine.Failed, Error = engine.Error
                        };
                        if (engine.Failed)
                        {
                            LastError = string.IsNullOrEmpty(engine.Error) ? "The map could not be loaded." : engine.Error;
                            Backend.EndGame();
                            Flow.Fire(FlowEvent.LoadFailed);
                            break;
                        }
                        if (!engine.Done) break;
                        builtFrom = Time.realtimeSinceStartup;
                        World = new WorldView(Backend);
                        World.Build(CurrentMap(), Options, false);
                        buildTotal = Mathf.Max(1, World.StepsLeft);
                    }
                    // The world builds a slice a frame, so the screen keeps drawing.
                    if (!World.BuildSome(BuildSliceMs))
                    {
                        Loading = new LoadProgress
                        {
                            Fraction = EngineShare + (1f - EngineShare) * (1f - World.StepsLeft / (float)buildTotal), Stage = World.BuildingLand ? LandStage : PreparingStage
                        };
                        break;
                    }
                    Loading = new LoadProgress { Fraction = 1f, Stage = "ready", Done = true };
                    Debug.Log($"{CurrentMap()?.Id}: the engine loaded it in {builtFrom - loadingFrom:0.0} s and the world was built in {Time.realtimeSinceStartup - builtFrom:0.0} s ({World.BuildTimes}; {World.ModelCount} models)");
                    input = new OrderInput(Backend, World, Options.ClassicControls);
                    Flow.Fire(FlowEvent.Loaded);
                    WorldLoaded?.Invoke(this);
                    break;
                case FlowState.Playing:
                    foreach (var k in BattleKeys)
                        if (Input.GetKeyDown(k)) BattleKey(k);
                    if (Flow.State != FlowState.Playing) break;
                    long simFrom = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (SimMarker.Auto()) RunSim(Time.deltaTime);
                    SimMs = Ms(simFrom);
                    using (InputMarker.Auto()) input?.Update();
                    FramesPlayed++;
                    if (Backend.Status == GameStatus.Victory) Flow.Fire(FlowEvent.Won);
                    else if (Backend.Status == GameStatus.Defeat) Flow.Fire(FlowEvent.Lost);
                    break;
                case FlowState.Paused:
                    if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Resume);
                    break;
                case FlowState.Editing:
                    // The world stands still while it is edited.
                    Editor?.Update();
                    break;
                case FlowState.Options when Flow.OptionsReturn == FlowState.Room:
                    // The room goes on while its options are open.
                    if (Backend.Rooms?.Pump() == RoomSession.Loading && Backend.Rooms.Room != null) StartRoomBattle(Backend.Rooms.Room);
                    else if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Back);
                    break;
                case FlowState.Options:
                case FlowState.Skirmish:
                case FlowState.LoadList:
                case FlowState.EditorSetup:
                    if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Back);
                    break;
                case FlowState.Multiplayer:
                case FlowState.Room:
                case FlowState.MapChoice:
                {
                    var rooms = Backend.Rooms;
                    var session = rooms?.Pump() ?? RoomSession.Off;
                    if (Flow.State != FlowState.Multiplayer && session == RoomSession.Loading && rooms.Room != null) StartRoomBattle(rooms.Room);
                    else if (Flow.State != FlowState.Multiplayer && rooms?.Room == null)
                    {
                        if (Flow.State == FlowState.MapChoice) Flow.Fire(FlowEvent.Back);
                        Flow.Fire(FlowEvent.Back);
                    }
                    else if (Input.GetKeyDown(KeyCode.Escape)) Flow.Fire(FlowEvent.Back);
                    break;
                }
            }
            // A room's battle keeps the relay's turns flowing.
            if (netMatch && GameFlow.InGame(Flow.State) || netMatch && Flow.State == FlowState.Loading) Backend.Rooms?.Pump();
            if (World != null)
            {
                // The sim's own clock, which stands still while paused.
                if (World.Entities != null) World.Entities.SimSeconds = Backend.Tick / (double)Mathf.Max(1, Backend.TicksPerSecond) + clock;
                // A batch run without graphics (the smoke test) has nothing to draw with.
                long renderFrom = System.Diagnostics.Stopwatch.GetTimestamp();
                // While the models build, the world waits behind the loading screen.
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && Flow.State != FlowState.Loading)
                    using (RenderMarker.Auto()) World.Render();
                RenderMs = Ms(renderFrom);
                // The frame that ends the loading draws the battle's first.
                if (FramesPlayed == 0 && Flow.State == FlowState.Playing) Debug.Log($"The battle's first frame was drawn in {RenderMs / 1000.0:0.0} s");
                if (!Application.isBatchMode) TellView();
            }
            using (ScreensMarker.Auto()) Screens.Tick();
            using (PointerMarker.Auto()) Pointer.Show(PointerCursor(), Time.unscaledTime);
        }

        // This frame's milliseconds in the simulation and in building the
        // world's draws, for the soak run in a player without the profiler.
        public double SimMs { get; private set; }
        public double RenderMs { get; private set; }
        static double Ms(long from) => (System.Diagnostics.Stopwatch.GetTimestamp() - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // Profiler markers for the frame's parts, read by the crowd test.
        static readonly Unity.Profiling.ProfilerMarker SimMarker = new Unity.Profiling.ProfilerMarker("Oku.Sim"),
            InputMarker = new Unity.Profiling.ProfilerMarker("Oku.Input"), RenderMarker = new Unity.Profiling.ProfilerMarker("Oku.Render"),
            ScreensMarker = new Unity.Profiling.ProfilerMarker("Oku.Screens"), PointerMarker = new Unity.Profiling.ProfilerMarker("Oku.Pointer");

        // The hourglass while loading, the game's pick in a battle, and the
        // plain pointer on every screen.
        public GameCursor PointerCursor()
        {
            if (Flow.State == FlowState.Loading) return GameCursor.Busy;
            if (Flow.State == FlowState.Playing && input != null) return input.PointerCursor();
            return GameCursor.Normal;
        }

        // ---- Saved games ----

        // The player's saves. Tests set a folder of their own, and null puts
        // the player's folder back.
        public static string SavesDir
        {
            get => savesDir ?? Path.Combine(Application.persistentDataPath, "Saves");
            set => savesDir = value;
        }
        static string savesDir;

        public struct SaveEntry
        {
            public string Path, Map;
            public uint Tick;
            public DateTime SavedAt;
        }

        // Writes the running game, named by the time and the map.
        public bool SaveNow(out string path)
        {
            path = null;
            if (!GameFlow.InGame(Flow.State) && !Flow.InGameNow) return false;
            Directory.CreateDirectory(SavesDir);
            string map = CurrentMap()?.Name ?? "game";
            foreach (var c in Path.GetInvalidFileNameChars()) map = map.Replace(c, '_');
            path = Path.Combine(SavesDir, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {map}.oksav");
            return Backend.SaveGame(path);
        }

        // Every save this backend can read, newest first.
        public List<SaveEntry> ListSaves()
        {
            var list = new List<SaveEntry>();
            if (!Directory.Exists(SavesDir)) return list;
            foreach (var f in Directory.GetFiles(SavesDir, "*.oksav"))
                if (Backend.SaveInfo(f, out var map, out var tick, out var at))
                    list.Add(new SaveEntry { Path = f, Map = map, Tick = tick, SavedAt = at });
            list.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
            return list;
        }

        public void LoadSave(SaveEntry save)
        {
            pendingLoad = save.Path;
            Setup.MapId = save.Map;
            Flow.Fire(FlowEvent.Start);
        }

        // Sound follows the options. Batch runs stay quiet.
        public void ApplyAudio()
        {
            if (Application.isBatchMode) return;
            Backend.SetAudio(Options.Volume, Options.Music);
        }

        void TellView()
        {
            var cam = World.Camera;
            var c = cam != null ? cam.GetComponent<Camera>() : null;
            if (c == null) return;
            float wide = 2f * cam.distance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Backend.SetView(cam.focus, wide * c.aspect, wide / Mathf.Max(0.2f, Mathf.Sin(cam.pitch * Mathf.Deg2Rad)));
        }

        static readonly KeyCode[] BattleKeys = { KeyCode.Escape, KeyCode.Pause, KeyCode.F1, KeyCode.F2 };

        // As in the original: Escape only cancels an armed command or the
        // selection, F1 is the game menu and F2 its options. The Pause key
        // still pauses.
        public void BattleKey(KeyCode k)
        {
            if (Flow.State != FlowState.Playing) return;
            switch (k)
            {
                case KeyCode.Escape: input?.Cancel(); break;
                case KeyCode.Pause:
                case KeyCode.F1: Flow.Fire(FlowEvent.Pause); break;
                case KeyCode.F2: Flow.Fire(FlowEvent.Pause); Flow.Fire(FlowEvent.OpenOptions); break;
            }
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
            if (UiKit.Sound == playSound) UiKit.Sound = null;
            Screens?.Dispose();
            Pointer?.Dispose();
            if (GlbLoader.Painter == Backend) GlbLoader.SetPainter(null);
            Backend?.Dispose();
        }
    }
}
