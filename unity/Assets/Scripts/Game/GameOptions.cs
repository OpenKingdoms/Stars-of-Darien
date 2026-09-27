// GameOptions.cs - the player's settings, kept in PlayerPrefs.
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
