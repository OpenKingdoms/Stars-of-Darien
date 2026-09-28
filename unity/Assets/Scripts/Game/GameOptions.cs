// GameOptions.cs - the player's settings, kept in PlayerPrefs.
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public enum WeatherChoice { ByMap, Off, Rain, Snow, Fog }

    public sealed class GameOptions
    {
        public WeatherChoice Weather = WeatherChoice.ByMap;
        public bool Shadows = true;
        public bool PostEffects = true;
        public bool Fullscreen = true;
        public float ScrollSpeed = 1f;
        public int GameSpeed = 1;   // 1 normal, 2 fast
        public float Volume = 0.8f;
        public bool Music = true;
        // Classic: left click orders, as the original. Modern: right click orders.
        public bool ClassicControls = true;
        // The pointer's scale, 0 to fit the screen, else 1 to 4 times.
        public int CursorScale = 0;
        // Formation drags: the group keeps one pace, the classic scheme's
        // right drag makes one, which way it faces, and the last shape used.
        public bool FormationPace = true;
        public bool ClassicRightDrag = true;
        public FormationFacing FacingRule = FormationFacing.ByDrag;
        public FormationShape Formation = FormationShape.Line;

        const string Prefix = "oku.";

        public static GameOptions Load()
        {
            var o = new GameOptions();
            o.Weather = (WeatherChoice)PlayerPrefs.GetInt(Prefix + "weather", (int)o.Weather);
            o.Shadows = PlayerPrefs.GetInt(Prefix + "shadows", 1) != 0;
            o.PostEffects = PlayerPrefs.GetInt(Prefix + "post", 1) != 0;
            o.Fullscreen = PlayerPrefs.GetInt(Prefix + "fullscreen", Screen.fullScreen ? 1 : 0) != 0;
            o.ScrollSpeed = PlayerPrefs.GetFloat(Prefix + "scroll", 1f);
            o.GameSpeed = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "speed", 1), 1, 2);
            o.Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "volume", 0.8f));
            o.Music = PlayerPrefs.GetInt(Prefix + "music", 1) != 0;
            o.ClassicControls = PlayerPrefs.GetInt(Prefix + "classic", 1) != 0;
            o.CursorScale = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "cursor", 0), 0, 4);
            o.FormationPace = PlayerPrefs.GetInt(Prefix + "formationpace", 1) != 0;
            o.ClassicRightDrag = PlayerPrefs.GetInt(Prefix + "rightdrag", 1) != 0;
            o.FacingRule = (FormationFacing)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "facing", 0), 0, 1);
            o.Formation = (FormationShape)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "formation", 0), 0, 3);
            return o;
        }

        public void Save()
        {
            PlayerPrefs.SetInt(Prefix + "weather", (int)Weather);
            PlayerPrefs.SetInt(Prefix + "shadows", Shadows ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "post", PostEffects ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "scroll", ScrollSpeed);
            PlayerPrefs.SetInt(Prefix + "speed", GameSpeed);
            PlayerPrefs.SetFloat(Prefix + "volume", Volume);
            PlayerPrefs.SetInt(Prefix + "music", Music ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "classic", ClassicControls ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "cursor", CursorScale);
            PlayerPrefs.SetInt(Prefix + "formationpace", FormationPace ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "rightdrag", ClassicRightDrag ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "facing", (int)FacingRule);
            PlayerPrefs.SetInt(Prefix + "formation", (int)Formation);
            PlayerPrefs.Save();
        }

        // The shape Tab picked in a drag, kept for the next game.
        public static void SaveFormation(FormationShape shape)
        {
            PlayerPrefs.SetInt(Prefix + "formation", (int)shape);
            PlayerPrefs.Save();
        }

        // The weather a map gets: the player's choice, or what its climate suggests.
        public static WeatherChoice Resolve(WeatherChoice choice, string climate)
        {
            if (choice != WeatherChoice.ByMap) return choice;
            switch (climate)
            {
                case "snow": return WeatherChoice.Snow;
                case "swamp": return WeatherChoice.Fog;
                default: return WeatherChoice.Off;
            }
        }
    }
}
