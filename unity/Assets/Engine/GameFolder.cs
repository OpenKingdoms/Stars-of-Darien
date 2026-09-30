// GameFolder.cs - finding the player's Total Annihilation: Kingdoms and
// telling whether a folder really holds it: the archives the engine reads,
// data.hpi and terrain.hpi, in the folder itself.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenKingdomsUnity.Engine
{
    public static class GameFolder
    {
        public static readonly string[] Required = { "data.hpi", "terrain.hpi" };

        // Folders inside an install, which a player may pick by mistake.
        static readonly string[] Inside = { "maps", "music", "movies", "docs", "boneyards", "gc", "mplayer", "atlas", "save", "anims" };

        public const string Hint = "Pick the folder where Total Annihilation: Kingdoms is installed, the one with data.hpi and terrain.hpi in it.";

        // Why dir does not hold the game, in words for the player, or null
        // when it does.
        public static string Problem(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return Hint;
            dir = Clean(dir);
            string shown = dir.Replace('/', '\\');
            try
            {
                if (File.Exists(dir)) return $"{shown} is a file. {Hint}";
                if (!Directory.Exists(dir)) return $"There is no folder at {shown}. {Hint}";
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(dir, "*.hpi")) have.Add(Path.GetFileName(f));
                if (have.Count == 0)
                {
                    string name = Path.GetFileName(dir.TrimEnd('/', '\\'));
                    if (Array.IndexOf(Inside, name.ToLowerInvariant()) >= 0)
                        return $"That is the game's {name} folder. Pick the folder above it, the one with data.hpi and terrain.hpi.";
                    return $"{shown} has no game archives (.hpi files) in it. {Hint}";
                }
                var missing = new List<string>();
                foreach (var r in Required) if (!have.Contains(r)) missing.Add(r);
                if (missing.Count > 0)
                    return $"{shown} has game archives but not {string.Join(" or ", missing)}, which the game needs. " +
                        "The install may be incomplete or a demo. Reinstall the game, or pick another copy.";
                return null;
            }
            catch (UnauthorizedAccessException) { return $"Windows would not let the game look inside {shown}. Pick another folder."; }
            catch (IOException e) { return $"{shown} could not be read ({e.Message}). Pick another folder."; }
            catch (ArgumentException) { return $"\"{shown}\" is not a folder path. {Hint}"; }
        }

        public static bool Holds(string dir) => Problem(dir) == null;

        // dir itself when it holds the game, or the folder one or two levels
        // under it that does, since a player may pick "C:\GOG Games". Null
        // when neither does. Looks at no more than a few hundred folders.
        public static string Resolve(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            dir = Clean(dir);
            if (Holds(dir)) return dir;
            int budget = 400;
            foreach (var sub in Children(dir, ref budget))
                if (Holds(sub)) return Clean(sub);
            foreach (var sub in Children(dir, ref budget))
                foreach (var subsub in Children(sub, ref budget))
                    if (Holds(subsub)) return Clean(subsub);
            return null;
        }

        static List<string> Children(string dir, ref int budget)
        {
            var list = new List<string>();
            if (budget <= 0) return list;
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    if (budget-- <= 0) break;
                    list.Add(d);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // Forward slashes, no trailing slash but on a drive's root, no quotes.
        public static string Clean(string dir)
        {
            if (dir == null) return null;
            dir = dir.Trim().Trim('"').Replace('\\', '/');
            while (dir.Length > 3 && dir.EndsWith("/")) dir = dir.Substring(0, dir.Length - 1);
            if (dir.Length == 2 && dir[1] == ':') dir += "/";
            return dir;
        }

        // Where the game was found, for the log.
        public enum Source { None, Saved, Environment, Registry, UsualPlace }

        // The first folder that holds the game, in this order: the player's
        // saved choice, OK_GAME_DIR, the installs Windows knows of, then the
        // usual install places. Null when none does.
        public static string Find(string saved, string env, IEnumerable<string> registry, IEnumerable<string> places, out Source source, Func<string, bool> holds = null)
        {
            holds = holds ?? Holds;
            source = Source.None;
            if (!string.IsNullOrWhiteSpace(saved) && holds(Clean(saved))) { source = Source.Saved; return Clean(saved); }
            if (!string.IsNullOrWhiteSpace(env) && holds(Clean(env))) { source = Source.Environment; return Clean(env); }
            foreach (var d in registry ?? new string[0])
                if (!string.IsNullOrWhiteSpace(d) && holds(Clean(d))) { source = Source.Registry; return Clean(d); }
            foreach (var d in places ?? new string[0])
                if (!string.IsNullOrWhiteSpace(d) && holds(Clean(d))) { source = Source.UsualPlace; return Clean(d); }
            return null;
        }

        // Where the GOG, Steam and CD editions install by default.
        public static List<string> UsualPlaces()
        {
            var roots = new List<string>();
            foreach (var v in new[] { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432" })
            {
                string p = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(p) && !roots.Contains(p)) roots.Add(p);
            }
            if (roots.Count == 0) roots.AddRange(new[] { "C:/Program Files (x86)", "C:/Program Files" });
            var list = new List<string>
            {
                "C:/GOG Games/Total Annihilation Kingdoms",
                "D:/GOG Games/Total Annihilation Kingdoms",
                "C:/GOG Games/Total Annihilation - Kingdoms",
            };
            foreach (var r in roots)
            {
                list.Add(Path.Combine(r, "GOG Galaxy/Games/Total Annihilation Kingdoms"));
                list.Add(Path.Combine(r, "GOG.com/Total Annihilation Kingdoms"));
                list.Add(Path.Combine(r, "Steam/steamapps/common/Total Annihilation Kingdoms"));
                list.Add(Path.Combine(r, "Steam/steamapps/common/Total Annihilation - Kingdoms"));
                list.Add(Path.Combine(r, "Cavedog/Kingdoms"));
                list.Add(Path.Combine(r, "Cavedog/Total Annihilation Kingdoms"));
            }
            list.Add("C:/Cavedog/Kingdoms");
            list.Add("C:/Games/Total Annihilation Kingdoms");
            list.Add("C:/Games/Kingdoms");
            for (int i = 0; i < list.Count; i++) list[i] = Clean(list[i]);
            return list;
        }

        // ---- Windows' record of the install ----

        // The folders the GOG installer and the original's uninstall entries
        // name, for this machine. Empty off Windows or when none is there.
        public static List<string> FromRegistry()
        {
            var list = new List<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const string un = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\", un64 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
            foreach (var root in new[] { un, un64 })
            {
                Add(list, ReadString(root + "GOGPACKTAKINGDOMS_is1", "InstallLocation"));
                Add(list, ReadString(root + "GOGPACKTAKINGDOMS_is1", "Inno Setup: App Path"));
                foreach (var key in new[] { "Total Annihilation: Kingdoms", "TAK: The Iron Plague" })
                    Add(list, FolderOfCommand(ReadString(root + key, "UninstallString")));
            }
#endif
            return list;
        }

        static void Add(List<string> list, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            dir = Clean(dir);
            if (!list.Contains(dir)) list.Add(dir);
        }

        // The folder of the program a command line starts: "C:\x\unins000.exe" /SILENT gives C:/x.
        public static string FolderOfCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            string exe;
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                exe = end > 0 ? command.Substring(1, end - 1) : command.Trim('"');
            }
            else
            {
                int dot = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                exe = dot > 0 ? command.Substring(0, dot + 4) : command;
            }
            try { return Clean(Path.GetDirectoryName(exe)); }
            catch (ArgumentException) { return null; }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static readonly UIntPtr LocalMachine = new UIntPtr(0x80000002u);
        const uint StringsOnly = 0x00000002 | 0x00000004;   // RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
        static extern int RegGetValue(UIntPtr key, string subKey, string value, uint flags, out uint type, StringBuilder data, ref uint bytes);

        static string ReadString(string subKey, string value)
        {
            try
            {
                uint bytes = 2048;
                var sb = new StringBuilder(1024);
                return RegGetValue(LocalMachine, subKey, value, StringsOnly, out _, sb, ref bytes) == 0 ? sb.ToString() : null;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return null; }
        }
#endif
    }
}
