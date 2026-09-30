// EngineInstaller.cs - copies the engine libraries the bindings expect from
// engine/ into Assets/Plugins/x86_64 when scripts load, replaces a copy it
// put there when engine/ has a newer build, and when Unity has another
// version loaded turns the engine off and asks for a restart.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class EngineInstaller
    {
        // Update: a newer build of the loaded version waits for the next start.
        // Local: a build of the engine whose version cannot be read, left alone.
        public enum Outcome { Ready, Install, Restart, NotPublished, Mismatch, Update, Local }

        // What to do about one library. installed is the version of the file
        // in the plugin folder (null when missing, -1 when unreadable), loaded
        // the version Unity has loaded (null when not loaded). ours says the
        // plugin file is what the installer last put there, and newer that
        // engine/ now holds different bytes for the same version.
        public static Outcome Decide(int expected, int? installed, int? loaded, bool published, bool ours = false, bool newer = false)
        {
            bool refresh = published && ours && newer;
            if (loaded.HasValue)
            {
                if (loaded.Value != expected) return Outcome.Restart;
                return refresh ? Outcome.Update : Outcome.Ready;
            }
            if (installed == expected) return refresh ? Outcome.Install : Outcome.Ready;
            if (installed == -1 && !ours) return Outcome.Local;
            if (published) return Outcome.Install;
            if (installed == null) return Outcome.NotPublished;
            return Outcome.Mismatch;
        }

        public sealed class Library
        {
            public string File, Prefix, Export;
            public int Expected;
            public int? Installed, Loaded;
            public Outcome Outcome;
            public string Published;    // the engine/ file for Expected, or null
            public bool Ours, Newer;
        }

        public sealed class Report
        {
            public Library Engine, Sdl;
            public bool Copied;
            public readonly List<string> Log = new List<string>();
        }

        // One platform's file names: the engine in the plugin folder, the
        // extension of its published copies in engine/, and the SDL beside
        // it. Linux takes SDL from the system, so it has none.
        public sealed class Names
        {
            public string Engine, Extension, Sdl;
            public static readonly Names Windows = new Names { Engine = "okengine.dll", Extension = ".dll", Sdl = "SDL2.dll" };
            public static readonly Names Linux = new Names { Engine = "libokengine.so", Extension = ".so", Sdl = null };
            public static Names Current => Application.platform == RuntimePlatform.LinuxEditor ? Linux : Windows;
        }

        public static Library Engine { get; private set; }
        public static string Message { get; private set; }

        public static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        public static string ReleaseDir => Path.Combine(Path.GetDirectoryName(ProjectDir), "engine");
        public static string PluginDir => Path.Combine(Application.dataPath, "Plugins", "x86_64");
        public static string KeepDir => Path.Combine(ProjectDir, "Library", "OkEngine");
        public static string RecordPath => Path.Combine(KeepDir, "installed.json");
        const string AskedKey = "oku.engine.restartAsked";

        static EngineInstaller()
        {
            try { Apply(Run(PluginDir, ReleaseDir, KeepDir, LoadedVersion, names: Names.Current)); }
            catch (Exception e) { Debug.LogWarning("Engine installer: " + e.Message); }
        }

        // The binding's API version, read by name so the studio needs no
        // link to the engine assembly.
        public static int BindingApi()
        {
            var t = Type.GetType("OpenKingdomsUnity.Engine.OkEngine, OpenKingdomsUnity.Engine");
            var f = t?.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
            return f != null ? (int)f.GetRawConstantValue() : -1;
        }

        // Looks at every library and installs, replaces or stages it. loaded
        // gives the version of a library Unity has loaded (file, export), -1
        // for a loaded one with no version, or null when it is not loaded.
        // names defaults to Windows'.
        public static Report Run(string pluginDir, string releaseDir, string keepDir, Func<string, string, int?> loaded, int engineApi = int.MinValue, Names names = null)
        {
            names = names ?? Names.Windows;
            var r = new Report();
            var record = ReadRecord(Path.Combine(keepDir, "installed.json"));
            r.Engine = Check(pluginDir, releaseDir, record, loaded, names.Engine, "okengine-api", names.Extension, "okx_api_version", engineApi == int.MinValue ? BindingApi() : engineApi);
            r.Sdl = names.Sdl != null ? CheckPlain(pluginDir, releaseDir, record, loaded, names.Sdl) : null;
            foreach (var lib in new[] { r.Engine, r.Sdl })
            {
                if (lib == null) continue;
                if (lib.Outcome == Outcome.Install) r.Copied |= Install(lib, pluginDir, keepDir, record, r.Log);
                else if (lib.Outcome == Outcome.Restart || lib.Outcome == Outcome.Update) Stage(lib, pluginDir, keepDir, record, r.Log);
            }
            r.Copied |= Retire(pluginDir, record, r.Log);
            WriteRecord(Path.Combine(keepDir, "installed.json"), record);
            return r;
        }

        static void Apply(Report r)
        {
            Engine = r.Engine;
            foreach (var line in r.Log) Debug.Log("OpenKingdoms: " + line);
            Message = Describe(Engine);
            if (Engine.Outcome == Outcome.Restart || Engine.Outcome == Outcome.Mismatch) Block(Message);
            else if ((Engine.Outcome == Outcome.Ready || Engine.Outcome == Outcome.Update || Engine.Outcome == Outcome.Local) && !RuntimePresent())
            {
                Message = RuntimeMissing;
                Block(Message);
            }
            if (r.Copied) EditorApplication.delayCall += AssetDatabase.Refresh;
            if (Engine.Outcome == Outcome.Restart && !Application.isBatchMode && !SessionState.GetBool(AskedKey, false))
            {
                SessionState.SetBool(AskedKey, true);
                EditorApplication.delayCall += AskRestart;
            }
            else if (Engine.Outcome == Outcome.Restart) Debug.LogWarning($"{Message} (API {Engine.Loaded} loaded, {Engine.Expected} expected)");
        }

        static Library Check(string pluginDir, string releaseDir, Dictionary<string, string> record, Func<string, string, int?> loaded, string file, string prefix, string extension, string export, int expected)
        {
            var lib = new Library { File = file, Prefix = prefix, Export = export, Expected = expected };
            string installed = Path.Combine(pluginDir, file);
            string published = Path.Combine(releaseDir, prefix + expected + extension);
            lib.Published = expected > 0 && File.Exists(published) ? published : null;
            if (File.Exists(installed))
            {
                try { lib.Installed = ReadVersion(File.ReadAllBytes(installed), export) ?? -1; }
                catch (IOException) { lib.Installed = -1; }
            }
            Own(lib, installed, record);
            lib.Loaded = loaded?.Invoke(file, export);
            lib.Outcome = expected > 0 ? Decide(expected, lib.Installed, lib.Loaded, lib.Published != null, lib.Ours, lib.Newer) : Outcome.NotPublished;
            return lib;
        }

        // A library with no version export, such as SDL2: installed when
        // missing and kept up to date only while it is the installer's copy.
        static Library CheckPlain(string pluginDir, string releaseDir, Dictionary<string, string> record, Func<string, string, int?> loaded, string file)
        {
            var lib = new Library { File = file };
            string installed = Path.Combine(pluginDir, file), published = Path.Combine(releaseDir, file);
            lib.Published = File.Exists(published) ? published : null;
            if (lib.Published == null) return null;
            bool present = File.Exists(installed);
            if (present) lib.Installed = 0;
            Own(lib, installed, record);
            bool isLoaded = loaded?.Invoke(file, null) != null;
            if (!present) lib.Outcome = Outcome.Install;
            else if (lib.Ours && lib.Newer) lib.Outcome = isLoaded ? Outcome.Update : Outcome.Install;
            else lib.Outcome = Outcome.Ready;
            return lib;
        }

        // Whether the plugin file is the installer's own copy, and whether
        // engine/ now has different bytes. A plugin file with the published
        // bytes and no record is taken as the installer's.
        static void Own(Library lib, string installed, Dictionary<string, string> record)
        {
            if (!File.Exists(installed)) return;
            string have = Hash(installed), want = lib.Published != null ? Hash(lib.Published) : null;
            if (record.TryGetValue(lib.File, out var mine)) lib.Ours = mine == have;
            else if (want != null && want == have) { lib.Ours = true; record[lib.File] = have; }
            lib.Newer = want != null && want != have;
        }

        static bool Install(Library lib, string pluginDir, string keepDir, Dictionary<string, string> record, List<string> log)
        {
            Directory.CreateDirectory(pluginDir);
            string dest = Path.Combine(pluginDir, lib.File);
            // A local build is kept aside under a name of its own, never lost.
            if (File.Exists(dest) && !lib.Ours)
            {
                Directory.CreateDirectory(keepDir);
                string keep = Unique(keepDir, $"replaced-{DateTime.Now:yyyyMMdd-HHmmss}", lib.File);
                File.Copy(dest, keep, true);
                log.Add($"kept the previous {lib.File} as {keep}");
            }
            File.Copy(lib.Published, dest, true);
            record[lib.File] = Hash(dest);
            if (lib.Expected > 0) lib.Installed = lib.Expected;
            lib.Ours = true;
            lib.Newer = false;
            lib.Outcome = Outcome.Ready;
            log.Add($"installed {Path.GetFileName(lib.Published)} as Plugins/x86_64/{lib.File}");
            return true;
        }

        // The loaded file cannot be overwritten, but it can be moved, so the
        // right one is in place when Unity next starts.
        static void Stage(Library lib, string pluginDir, string keepDir, Dictionary<string, string> record, List<string> log)
        {
            if (lib.Published == null) return;
            string dest = Path.Combine(pluginDir, lib.File);
            if (File.Exists(dest) && Hash(dest) == Hash(lib.Published)) return;
            try
            {
                Directory.CreateDirectory(keepDir);
                if (File.Exists(dest)) File.Move(dest, Unique(keepDir, $"old-{DateTime.Now:yyyyMMdd-HHmmss}", lib.File));
                File.Copy(lib.Published, dest, true);
                record[lib.File] = Hash(dest);
                log.Add($"put {Path.GetFileName(lib.Published)} in place for the next start");
            }
            catch (Exception e) { Debug.LogWarning($"Engine installer: {lib.File} stays until Unity restarts: {e.Message}"); }
        }

        // Libraries the project no longer uses. The installer's own copy of
        // one goes, and one built on this machine stays.
        public static readonly string[] Retired = { "okcore.dll" };

        static bool Retire(string pluginDir, Dictionary<string, string> record, List<string> log)
        {
            bool removed = false;
            foreach (var file in Retired)
            {
                if (!record.TryGetValue(file, out var mine)) continue;
                string path = Path.Combine(pluginDir, file);
                if (File.Exists(path) && Hash(path) == mine)
                {
                    try
                    {
                        File.Delete(path);
                        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                        log.Add($"removed {file}, which the project no longer uses");
                        removed = true;
                    }
                    catch (Exception e) { log.Add($"{file} stays until Unity restarts: {e.Message}"); continue; }
                }
                record.Remove(file);
            }
            return removed;
        }

        static string Unique(string dir, string stem, string file)
        {
            for (int i = 0; ; i++)
            {
                string p = Path.Combine(dir, stem + (i > 0 ? "-" + i : "") + "-" + file);
                if (!File.Exists(p)) return p;
            }
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        static Dictionary<string, string> ReadRecord(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(path) && MiniJson.Parse(File.ReadAllText(path)) is Dictionary<string, object> o)
                    foreach (var kv in o) if (kv.Value is string s) d[kv.Key] = s;
            }
            catch (Exception) { }
            return d;
        }

        static void WriteRecord(string path, Dictionary<string, string> record)
        {
            if (record.Count == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonText.Write(record.ToDictionary(kv => kv.Key, kv => (object)kv.Value), true) + "\n");
        }

        static void Block(string why)
        {
            var t = Type.GetType("OpenKingdomsUnity.Engine.EngineSettings, OpenKingdomsUnity.Engine");
            t?.GetField("Blocked", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, why);
        }

        // In plain words for the artist; the version numbers go to the log.
        public static string Describe(Library lib)
        {
            if (lib == null) return "";
            switch (lib.Outcome)
            {
                case Outcome.Ready:
                    return lib.Installed.HasValue || lib.Loaded.HasValue
                        ? "The game's engine is installed and matches this project."
                        : "The game's engine is not installed.";
                case Outcome.Update:
                    return "A newer build of the game's engine is in place and takes over when Unity next starts.";
                case Outcome.Local:
                    return "A build of the game's engine made on this computer is installed. The game checks it when it starts.";
                case Outcome.Restart:
                    return "The game's engine was updated, but Unity still has the old one loaded. Restart Unity to use it. Until then the studio uses the stand-in world.";
                case Outcome.Mismatch:
                    return "The game's engine in Assets/Plugins is an older one, and the engine folder has no copy of the one this project needs. The studio uses the stand-in world.";
                case Outcome.NotPublished:
                    return lib.Expected > 0
                        ? "The game's engine is not installed, and the engine folder has no copy of it. The studio uses the stand-in world."
                        : "The game's engine is not in this project. The studio uses the stand-in world.";
                default:
                    return "";
            }
        }

        public const string RuntimeMissing = "Windows is missing the Microsoft Visual C++ runtime the game's engine needs, so the studio uses the stand-in world. " +
            "Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe and restart Unity.";

        // The engine and SDL link the Visual C++ runtime, which Unity itself
        // does not bring.
        public static bool RuntimePresent()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return true;
            return File.Exists(Path.Combine(Environment.SystemDirectory, "vcruntime140.dll")) || File.Exists(Path.Combine(PluginDir, "vcruntime140.dll"));
        }

        static void AskRestart()
        {
            int pick = EditorUtility.DisplayDialogComplex("The engine was updated",
                Message + "\n\nSave your work first. Restart now?", "Restart Unity", "Later", "");
            if (pick == 0) EditorApplication.OpenProject(ProjectDir);
        }

        // ---- Reading a library's version ----

        // The version a library's export returns, read from the file without
        // loading it: the function is "mov eax, N; ret" in a release build,
        // after an endbr64 where the compiler adds one, as GCC on Linux does.
        public static int? ReadVersion(byte[] dll, string export)
        {
            int at = ExportOffset(dll, export);
            if (at < 0) at = ElfExportOffset(dll, export);
            if (at >= 0 && at + 4 <= dll.Length && dll[at] == 0xF3 && dll[at + 1] == 0x0F && dll[at + 2] == 0x1E && dll[at + 3] == 0xFA) at += 4;
            if (at < 0 || at + 6 > dll.Length) return null;
            if (dll[at] == 0xB8 && dll[at + 5] == 0xC3) return BitConverter.ToInt32(dll, at + 1);
            return null;
        }

        // The file offset of an exported function in a 64-bit little-endian
        // ELF shared library, from its dynamic symbol table, or -1.
        public static int ElfExportOffset(byte[] d, string name)
        {
            try
            {
                if (d.Length < 0x40 || d[0] != 0x7F || d[1] != 'E' || d[2] != 'L' || d[3] != 'F' || d[4] != 2 || d[5] != 1) return -1;
                long shoff = BitConverter.ToInt64(d, 0x28);
                int shentsize = BitConverter.ToUInt16(d, 0x3A), shnum = BitConverter.ToUInt16(d, 0x3C);
                long Section(int i) => shoff + (long)shentsize * i;
                for (int i = 0; i < shnum; i++)
                {
                    long s = Section(i);
                    if (BitConverter.ToUInt32(d, (int)s + 4) != 11) continue;   // SHT_DYNSYM
                    long symOff = BitConverter.ToInt64(d, (int)s + 24), symSize = BitConverter.ToInt64(d, (int)s + 32);
                    long strOff = BitConverter.ToInt64(d, (int)Section(BitConverter.ToInt32(d, (int)s + 40)) + 24);
                    for (long e = symOff; e + 24 <= symOff + symSize; e += 24)
                    {
                        int n = (int)(strOff + BitConverter.ToUInt32(d, (int)e));
                        int end = Array.IndexOf(d, (byte)0, n);
                        if (end - n != name.Length || System.Text.Encoding.ASCII.GetString(d, n, end - n) != name) continue;
                        int shndx = BitConverter.ToUInt16(d, (int)e + 6);
                        if (shndx == 0 || shndx >= shnum) return -1;
                        long value = BitConverter.ToInt64(d, (int)e + 8), home = Section(shndx);
                        long addr = BitConverter.ToInt64(d, (int)home + 16), offset = BitConverter.ToInt64(d, (int)home + 24);
                        long at = value - addr + offset;
                        return at >= 0 && at < d.Length ? (int)at : -1;
                    }
                }
            }
            catch (ArgumentException) { }
            catch (IndexOutOfRangeException) { }
            return -1;
        }

        // The file offset of an exported function in a PE image, or -1.
        public static int ExportOffset(byte[] d, string name)
        {
            try
            {
                if (d.Length < 0x40 || d[0] != 'M' || d[1] != 'Z') return -1;
                int pe = BitConverter.ToInt32(d, 0x3C);
                if (BitConverter.ToUInt32(d, pe) != 0x00004550) return -1;
                int sections = BitConverter.ToUInt16(d, pe + 6), optSize = BitConverter.ToUInt16(d, pe + 20);
                int opt = pe + 24;
                bool plus = BitConverter.ToUInt16(d, opt) == 0x20B;
                int exportRva = BitConverter.ToInt32(d, opt + (plus ? 112 : 96));
                var table = new List<(int va, int size, int raw)>();
                for (int i = 0; i < sections; i++)
                {
                    int s = opt + optSize + 40 * i;
                    int vsize = BitConverter.ToInt32(d, s + 8), va = BitConverter.ToInt32(d, s + 12);
                    int rawSize = BitConverter.ToInt32(d, s + 16), raw = BitConverter.ToInt32(d, s + 20);
                    table.Add((va, Math.Max(vsize, rawSize), raw));
                }
                int Off(int rva)
                {
                    foreach (var t in table) if (rva >= t.va && rva < t.va + t.size) return rva - t.va + t.raw;
                    return -1;
                }
                int e = Off(exportRva);
                if (e < 0) return -1;
                int names = BitConverter.ToInt32(d, e + 24), funcs = Off(BitConverter.ToInt32(d, e + 28));
                int nameTable = Off(BitConverter.ToInt32(d, e + 32)), ordTable = Off(BitConverter.ToInt32(d, e + 36));
                for (int i = 0; i < names; i++)
                {
                    int n = Off(BitConverter.ToInt32(d, nameTable + 4 * i));
                    int end = Array.IndexOf(d, (byte)0, n);
                    if (end - n != name.Length || System.Text.Encoding.ASCII.GetString(d, n, end - n) != name) continue;
                    int ord = BitConverter.ToUInt16(d, ordTable + 2 * i);
                    return Off(BitConverter.ToInt32(d, funcs + 4 * ord));
                }
            }
            catch (ArgumentException) { }
            catch (IndexOutOfRangeException) { }
            return -1;
        }

        // The version of a library Unity has already loaded, asked of the
        // loaded copy itself, -1 when it has no such export, or null when it
        // is not loaded.
        static int? LoadedVersion(string file, string export)
        {
            if (Application.platform == RuntimePlatform.LinuxEditor) return LoadedVersionLinux(file, export);
            if (Application.platform != RuntimePlatform.WindowsEditor) return null;
            IntPtr module = GetModuleHandleW(file);
            if (module == IntPtr.Zero) return null;
            if (export == null) return -1;
            IntPtr fn = GetProcAddress(module, export);
            if (fn == IntPtr.Zero) return -1;
            return Marshal.GetDelegateForFunctionPointer<VersionFn>(fn)();
        }

        // RTLD_NOLOAD finds a library already loaded under that soname and
        // never loads one.
        static int? LoadedVersionLinux(string file, string export)
        {
            const int Lazy = 0x1, NoLoad = 0x4;
            try
            {
                IntPtr module = dlopen(file, Lazy | NoLoad);
                if (module == IntPtr.Zero) return null;
                try
                {
                    if (export == null) return -1;
                    IntPtr fn = dlsym(module, export);
                    if (fn == IntPtr.Zero) return -1;
                    return Marshal.GetDelegateForFunctionPointer<VersionFn>(fn)();
                }
                finally { dlclose(module); }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return null; }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int VersionFn();
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("libdl.so.2", CharSet = CharSet.Ansi)] static extern IntPtr dlopen(string file, int mode);
        [DllImport("libdl.so.2", CharSet = CharSet.Ansi)] static extern IntPtr dlsym(IntPtr module, string name);
        [DllImport("libdl.so.2")] static extern int dlclose(IntPtr module);
    }
}
