// StudioBackend.cs - the one backend the studio windows share in edit
// mode: the real engine when okengine and the game files are present, the
// mock otherwise, or the mock on request. It is let go before Play, since
// the engine runs one instance at a time.
using System;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class StudioBackend
    {
        static IGameBackend backend;
        static ModelCache models;
        public static string Problem { get; private set; }
        public static event Action Changed;

        const string MockKey = "oku.studio.mock";
        public static bool PreferMock
        {
            get => EditorPrefs.GetBool(MockKey, false);
            set { if (value == PreferMock) return; EditorPrefs.SetBool(MockKey, value); Release(); Changed?.Invoke(); }
        }

        static StudioBackend()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) { Release(); Changed?.Invoke(); }
            };
            AssemblyReloadEvents.beforeAssemblyReload += Release;
        }

        public static bool Available => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static IGameBackend Get()
        {
            if (!Available) return null;
            if (backend != null) return backend;
            Problem = null;
            if (!PreferMock) backend = TryEngine();
            if (backend == null) backend = new MockBackend { StageSeconds = 0 };
            return backend;
        }

        public static ModelCache Models
        {
            get
            {
                var b = Get();
                if (b == null) return null;
                return models ??= new ModelCache(b);
            }
        }

        // The engine's backend, found by name so the studio needs no link
        // to the engine assembly.
        static IGameBackend TryEngine()
        {
            var settings = Type.GetType("OpenKingdomsUnity.Engine.EngineSettings, OpenKingdomsUnity.Engine");
            var type = Type.GetType("OpenKingdomsUnity.Engine.EngineBackend, OpenKingdomsUnity.Engine");
            if (settings == null || type == null) { Problem = "The engine binding is not in this project."; return null; }
            var available = settings.GetProperty("EngineAvailable")?.GetValue(null) as bool?;
            if (available != true) { Problem = "okengine.dll or the game files are missing, so the mock engine runs."; return null; }
            try { return (IGameBackend)Activator.CreateInstance(type); }
            catch (Exception e)
            {
                Problem = "The engine did not start: " + (e.InnerException ?? e).Message;
                return null;
            }
        }

        public static void Release()
        {
            models?.Dispose();
            models = null;
            try { backend?.Dispose(); } catch (Exception e) { Debug.LogWarning("Studio backend: " + e.Message); }
            backend = null;
        }

        public static void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var b = Available ? Get() : null;
                GUILayout.Label(b != null ? "Engine: " + b.Name : "Stopped while playing", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                bool mock = GUILayout.Toggle(PreferMock, "Use mock", EditorStyles.toolbarButton);
                if (mock != PreferMock) PreferMock = mock;
            }
            if (!string.IsNullOrEmpty(Problem)) EditorGUILayout.HelpBox(Problem, MessageType.Info);
        }
    }
}
