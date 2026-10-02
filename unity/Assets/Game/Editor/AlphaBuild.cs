// AlphaBuild.cs - the Windows build for playtesters, from the command line:
//
//   Unity.exe -batchmode -quit -buildTarget Win64 -projectPath unity
//     -executeMethod OpenKingdomsUnity.Build.WindowsAlpha -okOut <folder>
//
// A 64-bit Mono player in Release, stamped "Alpha 1 (<commit>)" and
// skirmish only without the map editor, with the published engine and SDL
// beside its plugins, the scenery and unit models under StreamingAssets, and
// a content report beside the
// folder. It fails when anything in it could hold the original's pixels.
// scripts/build-alpha.ps1 runs it and packs the zip.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Studio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace OpenKingdomsUnity
{
    public static class Build
    {
        public const string Alpha = "Alpha 1";
        public const string ExeName = "Darien Reforged.exe";
        public const string DataName = "Darien Reforged_Data";
        public const string StampAsset = "Assets/Game/Resources/" + BuildStamp.ResourceName + ".txt";
        public const string DefaultOut = "D:/OKBuild/alpha/DarienReforged";

        // Every line this logs starts with this, for the script to find.
        const string Tag = "OKBUILD ";

        public static void WindowsAlpha()
        {
            bool ok = false;
            try { ok = Run(Arg("-okOut") ?? DefaultOut, Arg("-okAlpha") ?? Alpha, Arg("-okReport")); }
            catch (Exception e) { Debug.LogError(Tag + "FAILED: " + e); }
            Debug.Log(Tag + (ok ? "RESULT PASS" : "RESULT FAIL"));
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Say(string line) => Debug.Log(Tag + line);

        static bool Fail(string why)
        {
            Debug.LogError(Tag + "FAILED: " + why);
            return false;
        }

        public static string ProjectDir => Path.GetDirectoryName(UnityEngine.Application.dataPath);
        public static string RepoDir => Path.GetDirectoryName(ProjectDir);

        public static bool Run(string outDir, string alpha, string reportPath)
        {
            outDir = Path.GetFullPath(outDir);
            string version = $"{alpha} ({Commit()})";
            Say($"building {GameRoot.Title} {version} into {outDir}");

            // The engine the binding expects, from engine/.
            int api = EngineInstaller.BindingApi();
            string engine = Path.Combine(RepoDir, "engine", $"okengine-api{api}.dll"), sdl = Path.Combine(RepoDir, "engine", "SDL2.dll");
            if (!File.Exists(engine) || !File.Exists(sdl)) return Fail($"engine/ has no okengine-api{api}.dll and SDL2.dll for the binding's API {api}");
            if (EngineInstaller.ReadVersion(File.ReadAllBytes(engine), "okx_api_version") != api) return Fail($"{engine} is not API {api}");

            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            if (backend != ScriptingImplementation.Mono2x) return Fail($"the standalone scripting backend is {backend}, not Mono");

            if (Directory.Exists(outDir))
            {
                bool ours = File.Exists(Path.Combine(outDir, ExeName)) || !Directory.EnumerateFileSystemEntries(outDir).Any();
                if (!ours) return Fail($"{outDir} is there and is not an earlier build, so it is left alone");
                Directory.Delete(outDir, true);
            }

            BuildReport report;
            try
            {
                File.WriteAllText(Path.Combine(ProjectDir, StampAsset), BuildStamp.Format(version, true, false));
                AssetDatabase.ImportAsset(StampAsset, ImportAssetOptions.ForceSynchronousImport);
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Say("scenes: " + string.Join(", ", scenes));
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, ExeName),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                AssetDatabase.DeleteAsset(StampAsset);
            }
            var s = report.summary;
            Say($"player build {s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / 1048576.0:0.0} MB, {s.totalTime.TotalSeconds:0} s");
            if (s.result != BuildResult.Succeeded) return Fail("the player build did not succeed");

            // Unity's player exe carries Unity's name and version, so the
            // game's go over them.
            string exe = Path.Combine(outDir, ExeName);
            string stamped = ExeVersion.Stamp(exe, PlayerSettings.productName, PlayerSettings.companyName, alpha);
            if (stamped != null) return Fail(stamped);
            Say($"exe properties: {PlayerSettings.productName}, {PlayerSettings.companyName}, {alpha}");

            string data = Path.Combine(outDir, DataName);
            if (!Directory.Exists(data)) return Fail($"the build has no {DataName} folder");

            // The published engine beside the player's plugins, whatever the
            // project's plugin folder held.
            string plugins = Path.Combine(data, "Plugins", "x86_64");
            Directory.CreateDirectory(plugins);
            File.Copy(engine, Path.Combine(plugins, "okengine.dll"), true);
            File.Copy(sdl, Path.Combine(plugins, "SDL2.dll"), true);
            if (EngineInstaller.ReadVersion(File.ReadAllBytes(Path.Combine(plugins, "okengine.dll")), "okx_api_version") != api) return Fail("the copied okengine.dll is not the published one");
            string vc = VcRuntime();
            if (vc != null)
            {
                File.Copy(vc, Path.Combine(plugins, "vcruntime140.dll"), true);
                Say("Visual C++ runtime for Windows without it: " + vc);
            }
            else Debug.LogWarning(Tag + "no redistributable vcruntime140.dll found, so the player needs the Visual C++ runtime installed");
            Say($"engine: okengine-api{api}.dll as okengine.dll, SDL2.dll, in {plugins}");

            // The models, hand-built and carved, where OverrideLoader looks in a player.
            int models = 0;
            foreach (var folder in new[] { OverrideIndex.Folder(OverrideKind.Feature), OverrideIndex.Folder(OverrideKind.Unit) })
            {
                string from = Path.Combine(ProjectDir, folder), to = Path.Combine(data, "StreamingAssets", folder);
                if (!Directory.Exists(from)) continue;
                Directory.CreateDirectory(to);
                foreach (var f in Directory.GetFiles(from))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".meta" || ext == ".gitkeep" || Path.GetFileName(f).StartsWith(".")) continue;
                    File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                    models++;
                }
            }
            Say($"copied {models} model files to StreamingAssets");

            var packed = report.packedAssets.SelectMany(p => p.contents).Select(c => new ContentCheck.Packed
            {
                Type = c.type != null ? c.type.Name : "?",
                Source = c.sourceAssetPath,
                Bytes = (long)c.packedSize,
            }).ToList();
            var check = ContentCheck.Scan(outDir, packed);
            reportPath = reportPath ?? Path.Combine(Path.GetDirectoryName(outDir), "content-report.txt");
            File.WriteAllText(reportPath, ContentCheck.Describe(check, $"Content report for {GameRoot.Title} {version}, {DateTime.Now:yyyy-MM-dd HH:mm}"));
            Say($"content report: {reportPath}");
            foreach (var g in check.Streaming.GroupBy(e => e.Kind)) Say($"  {g.Key}: {g.Count()} files");
            if (!check.Passed)
            {
                foreach (var p in check.Problems) Debug.LogError(Tag + "CONTENT " + p);
                return Fail($"the content check found {check.Problems.Count} problems");
            }
            Say($"done: {GameRoot.Title} {version}");
            return true;
        }

        // The short commit, with "+changes" when tracked files differ from it.
        public static string Commit()
        {
            string head = Git("rev-parse --short HEAD");
            if (string.IsNullOrEmpty(head)) return "unknown";
            // Unity writes its .asset settings back on its own, so they do not count.
            string dirty = Git("status --porcelain --untracked-files=no -- . \":(exclude)*.asset\"");
            return string.IsNullOrEmpty(dirty) ? head : head + "+changes";
        }

        static string Git(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = RepoDir,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(20000);
                return p.ExitCode == 0 ? output : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + "git " + args + ": " + e.Message);
                return null;
            }
        }

        // Microsoft's redistributable copy of vcruntime140.dll, from the
        // newest Visual Studio here, or null.
        static string VcRuntime()
        {
            var found = new List<string>();
            foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                string vs = pf != null ? Path.Combine(pf, "Microsoft Visual Studio") : null;
                if (vs == null || !Directory.Exists(vs)) continue;
                try
                {
                    // <vs>/<year>/<edition>/VC/Redist/MSVC/<version>/x64/Microsoft.VC<n>.CRT
                    foreach (var year in Directory.GetDirectories(vs))
                        foreach (var edition in Directory.GetDirectories(year))
                        {
                            string msvc = Path.Combine(edition, "VC", "Redist", "MSVC");
                            if (!Directory.Exists(msvc)) continue;
                            foreach (var ver in Directory.GetDirectories(msvc))
                            {
                                string x64 = Path.Combine(ver, "x64");
                                if (!Directory.Exists(x64)) continue;
                                foreach (var crt in Directory.GetDirectories(x64, "Microsoft.VC*.CRT"))
                                    if (File.Exists(Path.Combine(crt, "vcruntime140.dll"))) found.Add(Path.Combine(crt, "vcruntime140.dll"));
                            }
                        }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            return found.OrderByDescending(f => FileVersionInfo.GetVersionInfo(f).FileVersion, StringComparer.Ordinal).FirstOrDefault();
        }
    }

    // Writes the product, company and version into a Windows exe's file
    // properties, over the version resource it had.
    public static class ExeVersion
    {
        const ushort English = 0x0409, Unicode = 1200;
        static readonly IntPtr RtVersion = (IntPtr)16, VersionId = (IntPtr)1;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        static extern bool UpdateResourceW(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool EndUpdateResource(IntPtr update, bool discard);

        // Null once written and read back, or why not. "Alpha 1" is file
        // version 0.1.0.0, since Windows wants numbers there.
        public static string Stamp(string exe, string product, string company, string version)
        {
            string copyright = FileVersionInfo.GetVersionInfo(exe).LegalCopyright ?? "";
            var digits = new string(version.Where(char.IsDigit).ToArray());
            ushort minor = ushort.TryParse(digits, out var n) ? n : (ushort)0;
            var strings = new[]
            {
                ("CompanyName", company), ("FileDescription", product), ("FileVersion", version), ("InternalName", product),
                ("LegalCopyright", copyright), ("OriginalFilename", Path.GetFileName(exe)), ("ProductName", product), ("ProductVersion", version),
            };
            byte[] resource = Resource(strings, minor);
            IntPtr update = BeginUpdateResource(exe, false);
            if (update == IntPtr.Zero) return $"could not open {exe} to stamp it (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})";
            // In US English, as Unity's is, so this one takes its place.
            if (!UpdateResourceW(update, RtVersion, VersionId, English, resource, (uint)resource.Length))
            {
                EndUpdateResource(update, true);
                return $"could not write the version into {exe} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})";
            }
            if (!EndUpdateResource(update, false)) return $"could not save {exe} after stamping it (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})";
            var read = FileVersionInfo.GetVersionInfo(exe);
            if (read.ProductName != product || read.CompanyName != company || read.ProductVersion != version)
                return $"{exe} reads back as {read.ProductName}, {read.CompanyName}, {read.ProductVersion}";
            return null;
        }

        // VS_VERSIONINFO: the fixed numbers, one table of strings and the
        // language it is in.
        static byte[] Resource((string key, string value)[] strings, ushort minor)
        {
            var fixedInfo = new MemoryStream();
            using (var w = new BinaryWriter(fixedInfo))
            {
                uint ms = minor, ls = 0;
                foreach (uint v in new uint[] { 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x00040004, 1, 0, 0, 0 }) w.Write(v);
            }
            var table = strings.Select(kv => Block(kv.key, System.Text.Encoding.Unicode.GetBytes(kv.value + "\0"), kv.value.Length + 1, true)).ToArray();
            var stringInfo = Block("StringFileInfo", null, 0, true, Block($"{English:x4}{Unicode:x4}", null, 0, true, table));
            var varInfo = Block("VarFileInfo", null, 0, true, Block("Translation", BitConverter.GetBytes(English | (uint)Unicode << 16), 4, false));
            return Block("VS_VERSION_INFO", fixedInfo.ToArray(), 52, false, stringInfo, varInfo);
        }

        // One block: its length, value length and type, its key, its value
        // and its children, each on a four-byte boundary.
        static byte[] Block(string key, byte[] value, int valueLength, bool text, params byte[][] children)
        {
            var block = new MemoryStream();
            var w = new BinaryWriter(block);
            w.Write((ushort)0);
            w.Write((ushort)valueLength);
            w.Write((ushort)(text ? 1 : 0));
            w.Write(System.Text.Encoding.Unicode.GetBytes(key + "\0"));
            void Pad() { while (block.Length % 4 != 0) w.Write((byte)0); }
            Pad();
            if (value != null) w.Write(value);
            foreach (var child in children) { Pad(); w.Write(child); }
            w.Flush();
            var bytes = block.ToArray();
            BitConverter.GetBytes((ushort)bytes.Length).CopyTo(bytes, 0);
            return bytes;
        }
    }
}
