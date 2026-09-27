// OverrideEditorHook.cs - in the editor, drop-in .fbx and .prefab models
// load through the AssetDatabase, and any change under Assets/Overrides
// makes the next game scan the folders again.
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class OverrideEditorHook
    {
        static OverrideEditorHook()
        {
            OverrideLoader.EditorLoad = path => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }

    public sealed class OverridePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var list in new[] { imported, deleted, moved, movedFrom })
                foreach (var p in list)
                    if (p.StartsWith(OverrideIndex.Root))
                    {
                        OverrideLoader.Reset();
                        return;
                    }
        }
    }
}
