// AlphaBuild.cs - the Windows build for playtesters, from the command line:
//
//   Unity.exe -batchmode -quit -buildTarget Win64 -projectPath unity
//     -executeMethod OpenKingdomsUnity.Build.WindowsAlpha -okOut <folder>
//
// A 64-bit Mono player in Release, stamped "Alpha 1 (<commit>)" and
// skirmish only, with the published engine and SDL beside its plugins, the
// scenery and unit models under StreamingAssets, and a content report beside the
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
                File.WriteAllText(Path.Combine(ProjectDir, StampAsset), BuildStamp.Format(version, true));
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
}
