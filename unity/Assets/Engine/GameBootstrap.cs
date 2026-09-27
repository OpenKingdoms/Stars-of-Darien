// GameBootstrap.cs - Play in any scene, even an empty one, starts the
// game. When the presentation's GameRoot is in the scene it runs the game
// and only needs the engine backend handed to it. Otherwise the real
// engine's view runs when okengine and the game files are here, and the
// capsule demo on the small core when they are not.
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Engine
{
    public static class GameBootstrap
    {
        // Set before Play (a test, a menu item) to take the demo anyway.
        public static bool ForceDemo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OfferBackend()
        {
            // GameRoot.BackendFactory, when the presentation has one.
            var root = Type.GetType("OpenKingdomsUnity.Game.GameRoot, OpenKingdomsUnity");
            var field = root?.GetField("BackendFactory", BindingFlags.Public | BindingFlags.Static);
            if (field == null || !EngineSettings.EngineAvailable) return;
            if (field.FieldType == typeof(Func<OpenKingdomsUnity.Game.IGameBackend>))
                field.SetValue(null, (Func<OpenKingdomsUnity.Game.IGameBackend>)(() => new EngineBackend()));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var root = Type.GetType("OpenKingdomsUnity.Game.GameRoot, OpenKingdomsUnity");
            if (root != null && Object.FindAnyObjectByType(root) != null) return;
            if (Object.FindAnyObjectByType<EngineDriver>() != null) return;
            if (Object.FindAnyObjectByType<SimDriver>() != null) return;
            if (!ForceDemo && EngineSettings.EngineAvailable)
                new GameObject("OpenKingdoms").AddComponent<EngineDriver>();
            else
                new GameObject("SimDriver").AddComponent<SimDriver>();
        }
    }
}
