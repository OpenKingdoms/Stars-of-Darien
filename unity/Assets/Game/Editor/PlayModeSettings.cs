// PlayModeSettings.cs - scripts changed during Play recompile only once
// Play ends, for every developer, since the game cannot carry its engine
// and screens across a reload in the middle of a battle.
using UnityEditor;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class PlayModeSettings
    {
        // 1 is Recompile After Finished Playing in Preferences, General.
        const string Key = "ScriptCompilationDuringPlay";

        static PlayModeSettings()
        {
            if (EditorPrefs.GetInt(Key, 0) != 1) EditorPrefs.SetInt(Key, 1);
        }
    }
}
