// TempSaves.cs - a saves folder of a test's own, so no test writes to or
// lists the player's saved games.
using System;
using System.IO;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Tests
{
    static class TempSaves
    {
        // A fresh empty folder that GameRoot saves to and lists from.
        public static string Use()
        {
            string dir = Path.Combine(Path.GetTempPath(), "oku-saves-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            GameRoot.SavesDir = dir;
            return dir;
        }

        // Deletes the folder and points GameRoot back at the player's.
        public static void Drop(string dir)
        {
            GameRoot.SavesDir = null;
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
