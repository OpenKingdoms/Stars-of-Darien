// RemasterMenu.cs - OpenKingdoms menu items to make and play the remaster
// scene, which holds nothing but a GameRoot.
using OpenKingdomsUnity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class RemasterMenu
    {
        public const string ScenePath = "Assets/Scenes/Remaster.unity";

        [MenuItem("OpenKingdoms/Play Remaster", priority = 0)]
        public static void Play()
        {
            if (!System.IO.File.Exists(ScenePath)) CreateScene();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("OpenKingdoms/Create Remaster Scene", priority = 1)]
        public static void CreateScene()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            new GameObject("GameRoot").AddComponent<GameRoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
        }
    }
}
