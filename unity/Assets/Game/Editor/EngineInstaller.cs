// EngineInstaller.cs - copies the engine libraries the bindings expect from
// engine/ into Assets/Plugins/x86_64 when scripts load, and when Unity has
// another version loaded turns the engine off and asks for a restart.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class EngineInstaller
    {
        public enum Outcome { Ready, Install, Restart, NotPublished, Mismatch }

        // What to do about one library. installed is the version of the file
        // in the plugin folder (null when missing, -1 when unreadable), loaded
        // the version Unity has loaded (null when not loaded).
        public static Outcome Decide(int expected, int? installed, int? loaded, bool published)
        {
            if (loaded.HasValue) return loaded.Value == expected ? Outcome.Ready : Outcome.Restart;
            if (installed == expected) return Outcome.Ready;
            if (published) return Outcome.Install;
            if (installed == null || installed == -1) return Outcome.NotPublished;
            return Outcome.Mismatch;
        }

        public sealed class Library
        {
            public string File, Prefix, Export;
            public int Expected;
            public int? Installed, Loaded;
            public Outcome Outcome;
            public string Published;    // the engine/ file for Expected, or null
        }

        public static Library Engine { get; private set; }
        public static Library Core { get; private set; }
        public static string Message { get; private set; }

        public static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        public static string ReleaseDir => Path.Combine(Path.GetDirectoryName(ProjectDir), "engine");
        public static string PluginDir => Path.Combine(Application.dataPath, "Plugins", "x86_64");
        const string AskedKey = "oku.engine.restartAsked";

        static EngineInstaller()
        {
            try { Run(); }
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

        static void Run()
        {
            Engine = Check("okengine.dll", "okengine-api", "okx_api_version", BindingApi());
            Core = Check("okcore.dll", "okcore-abi", "ok_sim_abi_version", OkNative.AbiVersion);
            bool copied = false;
            foreach (var lib in new[] { Engine, Core })
            {
                if (lib.Outcome == Outcome.Install) copied |= Install(lib);
                if (lib.Outcome == Outcome.Restart) Stage(lib);
            }
            if (Engine.Outcome != Outcome.Restart && Engine.Expected > 0)
                copied |= CopyIfMissing("SDL2.dll");

            Message = Describe(Engine);
            if (Engine.Outcome == Outcome.Restart || Engine.Outcome == Outcome.Mismatch) Block(Message);
            else if (Engine.Outcome == Outcome.Ready && !RuntimePresent())
            {
                Message = RuntimeMissing;
                Block(Message);
            }
            if (copied) EditorApplication.delayCall += AssetDatabase.Refresh;
            if (Engine.Outcome == Outcome.Restart && !Application.isBatchMode && !SessionState.GetBool(AskedKey, false))
            {
                SessionState.SetBool(AskedKey, true);
                EditorApplication.delayCall += AskRestart;
            }
            else if (Engine.Outcome == Outcome.Restart) Debug.LogWarning(Message);
        }

        static Library Check(string file, string prefix, string export, int expected)
        {
            var lib = new Library { File = file, Prefix = prefix, Export = export, Expected = expected };
            string installed = Path.Combine(PluginDir, file);
            if (File.Exists(installed))
            {
                try { lib.Installed = ReadVersion(File.ReadAllBytes(installed), export) ?? -1; }
                catch (IOException) { lib.Installed = -1; }
            }
            lib.Loaded = LoadedVersion(file, export);
            string published = Path.Combine(ReleaseDir, prefix + expected + ".dll");
            lib.Published = expected > 0 && File.Exists(published) ? published : null;
            lib.Outcome = expected > 0 ? Decide(expected, lib.Installed, lib.Loaded, lib.Published != null) : Outcome.NotPublished;
            return lib;
        }

        static bool Install(Library lib)
        {
            Directory.CreateDirectory(PluginDir);
            string dest = Path.Combine(PluginDir, lib.File);
            // A local build of another version is kept aside, not lost.
            if (File.Exists(dest) && lib.Installed.HasValue)
            {
                string keep = Path.Combine(ProjectDir, "Library", "OkEngine");
                Directory.CreateDirectory(keep);
                File.Copy(dest, Path.Combine(keep, "replaced-" + lib.File), true);
            }
            File.Copy(lib.Published, dest, true);
            lib.Installed = lib.Expected;
            lib.Outcome = Outcome.Ready;
            Debug.Log($"OpenKingdoms: installed {Path.GetFileName(lib.Published)} as Plugins/x86_64/{lib.File}");
            return true;
        }

        static bool CopyIfMissing(string file)
        {
            string from = Path.Combine(ReleaseDir, file), dest = Path.Combine(PluginDir, file);
            if (File.Exists(dest) || !File.Exists(from)) return false;
            Directory.CreateDirectory(PluginDir);
            File.Copy(from, dest);
            return true;
        }

        // The loaded file cannot be overwritten, but it can be moved, so the
        // right one is in place when Unity next starts.
        static void Stage(Library lib)
        {
            if (lib.Published == null) return;
            string dest = Path.Combine(PluginDir, lib.File);
            if (lib.Installed == lib.Expected) return;
            try
            {
                string aside = Path.Combine(ProjectDir, "Library", "OkEngine");
                Directory.CreateDirectory(aside);
                if (File.Exists(dest)) File.Move(dest, Path.Combine(aside, $"old-{DateTime.Now.Ticks}-{lib.File}"));
                File.Copy(lib.Published, dest, true);
            }
            catch (Exception e) { Debug.LogWarning($"Engine installer: {lib.File} stays until Unity restarts: {e.Message}"); }
        }

        static void Block(string why)
        {
            var t = Type.GetType("OpenKingdomsUnity.Engine.EngineSettings, OpenKingdomsUnity.Engine");
            t?.GetField("Blocked", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, why);
        }

        public static string Describe(Library lib)
        {
            if (lib == null) return "";
            switch (lib.Outcome)
            {
                case Outcome.Ready:
                    return lib.Installed.HasValue || lib.Loaded.HasValue
                        ? $"The engine library (API {lib.Expected}) is installed and matches this project."
                        : "No engine library is installed.";
                case Outcome.Restart:
                    return $"The engine was updated to API {lib.Expected}, but Unity still has API {lib.Loaded} loaded from before. Restart Unity to use it. Until then the mock engine runs.";
                case Outcome.Mismatch:
                    return $"The engine library in Plugins is API {lib.Installed}, but this project needs API {lib.Expected} and engine/ has no copy of it. The mock engine runs.";
                case Outcome.NotPublished:
                    return lib.Expected > 0
                        ? $"No engine library for API {lib.Expected} is installed or published in engine/. The mock engine runs."
                        : "The engine binding is not in this project. The mock engine runs.";
                default:
                    return "";
            }
        }

        public const string RuntimeMissing = "Windows is missing the Microsoft Visual C++ runtime the engine needs, so the mock engine runs. " +
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
        // loading it: the function is "mov eax, N; ret" in a release build.
        public static int? ReadVersion(byte[] dll, string export)
        {
            int at = ExportOffset(dll, export);
            if (at < 0 || at + 6 > dll.Length) return null;
            if (dll[at] == 0xB8 && dll[at + 5] == 0xC3) return BitConverter.ToInt32(dll, at + 1);
            return null;
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
        // loaded copy itself, or null when it is not loaded.
        static int? LoadedVersion(string file, string export)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return null;
            IntPtr module = GetModuleHandleW(file);
            if (module == IntPtr.Zero) return null;
            IntPtr fn = GetProcAddress(module, export);
            if (fn == IntPtr.Zero) return -1;
            return Marshal.GetDelegateForFunctionPointer<VersionFn>(fn)();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int VersionFn();
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
