// GameBootstrap.cs - Play in any scene, even an empty one, starts the
// game. When the presentation's GameRoot is in the scene it runs the game
// and only needs the engine backend handed to it. Otherwise the real
// engine's view runs when okengine and the game files are here, and a
// notice says what is missing when they are not.
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Engine
{
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OfferBackend()
        {
            if (!Application.isEditor) PlayerHooks();
            // GameRoot.BackendFactory, when the presentation has one.
            var root = Type.GetType("OpenKingdomsUnity.Game.GameRoot, OpenKingdomsUnity");
            var field = root?.GetField("BackendFactory", BindingFlags.Public | BindingFlags.Static);
            if (field == null || !EngineSettings.EngineAvailable) return;
            if (field.FieldType == typeof(Func<OpenKingdomsUnity.Game.IGameBackend>))
                field.SetValue(null, (Func<OpenKingdomsUnity.Game.IGameBackend>)(() => new EngineBackend()));
        }

        // A built player looks for the game itself and asks for the folder
        // when it found none. Options can pick another.
        static bool askForFolder;

        static void PlayerHooks()
        {
            string dir = EngineSettings.FindForPlayer();
            Debug.Log(dir != null ? $"Game folder: {dir} ({Describe(EngineSettings.FoundBy)})" : "Game folder: not found, the player will be asked");
            OpenKingdomsUnity.Game.GameRoot.GameFolder = () => GameFolderScreen.Shown(EngineSettings.GameDir);
            OpenKingdomsUnity.Game.GameRoot.ChangeGameFolder = () => GameFolderScreen.Show(EngineSettings.GameDir, Restart, () => { });
            bool library = System.IO.File.Exists(System.IO.Path.Combine(EngineSettings.PluginDir, EngineSettings.LibraryFile));
            askForFolder = dir == null && library && EngineSettings.Blocked == null;
            if (!library) Debug.LogWarning(OpenKingdomsUnity.Game.GameRoot.Title + ": " + EngineSettings.Problem);
        }

        static string Describe(GameFolder.Source s)
        {
            switch (s)
            {
                case GameFolder.Source.Saved: return "the player's choice";
                case GameFolder.Source.Environment: return "OK_GAME_DIR";
                case GameFolder.Source.Registry: return "the installer's record in Windows";
                case GameFolder.Source.UsualPlace: return "a usual install place";
                default: return "unknown";
            }
        }

        // The game starts again on the chosen folder: the scene loads anew,
        // and its GameRoot starts the engine there.
        static void Restart(string dir)
        {
            askForFolder = false;
            OpenKingdomsUnity.Game.GameRoot.BackendFactory = () =>
            {
                OpenKingdomsUnity.Game.GameRoot.BackendFactory = () => new EngineBackend();
                // An engine already up keeps its first folder until it stops.
                OkEngine.okx_shutdown();
                return new EngineBackend();
            };
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (askForFolder && !Application.isBatchMode)
            {
                GameFolderScreen.Show("", Restart, null);
                return;
            }
            BootScene();
        }

        // The engine's view, or the notice when the engine cannot run,
        // unless the scene already has a game.
        public static void BootScene()
        {
            var root = Type.GetType("OpenKingdomsUnity.Game.GameRoot, OpenKingdomsUnity");
            if (root != null && Object.FindAnyObjectByType(root) != null) return;
            if (Object.FindAnyObjectByType<EngineDriver>() != null) return;
            if (Object.FindAnyObjectByType<EngineNotice>() != null) return;
            string problem = EngineSettings.Problem;
            if (problem == null) new GameObject("OpenKingdoms").AddComponent<EngineDriver>();
            else EngineNotice.Show(problem);
        }
    }
}
