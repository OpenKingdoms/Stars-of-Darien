// EngineInstallerTests.cs - the installer's choices, what it does to a
// plugin folder, reading a library's version from the file, and the
// published libraries in engine/ matching what the bindings expect.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Studio;
using static OpenKingdomsUnity.Studio.EngineInstaller;

namespace OpenKingdomsUnity.Tests
{
    public class EngineInstallerTests
    {
        [Test]
        public void ItInstallsOnlyWhatIsMissingOrAnotherVersion()
        {
            Assert.AreEqual(Outcome.Install, Decide(17, null, null, true), "a fresh clone gets the published engine");
            Assert.AreEqual(Outcome.Ready, Decide(17, 17, null, true), "the right one stays, even a local build");
            Assert.AreEqual(Outcome.Install, Decide(17, 16, null, true), "an old one is replaced before anything loads it");
            Assert.AreEqual(Outcome.Local, Decide(17, -1, null, true), "a local build whose version cannot be read is left to the binding's own check");
            Assert.AreEqual(Outcome.Mismatch, Decide(17, 16, null, false), "an old one with nothing to replace it blocks the engine");
            Assert.AreEqual(Outcome.NotPublished, Decide(17, null, null, false));
            Assert.AreEqual(Outcome.Local, Decide(17, -1, null, false));
        }

        [Test]
        public void ANewBuildOfTheSameVersionReplacesOnlyTheInstallersCopy()
        {
            Assert.AreEqual(Outcome.Install, Decide(17, 17, null, true, ours: true, newer: true), "its own copy is brought up to date");
            Assert.AreEqual(Outcome.Ready, Decide(17, 17, null, true, ours: false, newer: true), "a local build is not");
            Assert.AreEqual(Outcome.Ready, Decide(17, 17, null, true, ours: true, newer: false));
            Assert.AreEqual(Outcome.Update, Decide(17, 17, 17, true, ours: true, newer: true), "loaded, it waits for the next start");
        }

        static byte[] Lib(string export, int version, byte tag)
        {
            var d = FakeLibrary(export, new byte[] { 0xB8, (byte)version, 0, 0, 0, 0xC3 });
            d[d.Length - 1] = tag;
            return d;
        }

        [Test]
        public void ThePluginFolderFollowsEngineFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "oku-installer-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string plugins = Path.Combine(root, "Plugins"), release = Path.Combine(root, "engine"), keep = Path.Combine(root, "keep");
            string engine = Path.Combine(plugins, "okengine.dll"), sdl = Path.Combine(plugins, "SDL2.dll"), core = Path.Combine(plugins, "okcore.dll");
            Directory.CreateDirectory(release);
            Func<string, string, int?> none = (f, e) => null;
            void Publish(byte tag) => File.WriteAllBytes(Path.Combine(release, "okengine-api17.dll"), Lib("okx_api_version", 17, tag));
            byte Tag(string path) { var b = File.ReadAllBytes(path); return b[b.Length - 1]; }
            try
            {
                Publish(1);
                File.WriteAllBytes(Path.Combine(release, "okcore-abi2.dll"), Lib("ok_sim_abi_version", 2, 1));
                File.WriteAllBytes(Path.Combine(release, "SDL2.dll"), new byte[] { 1, 2, 3 });
                var r = Run(plugins, release, keep, none, 17, 2);
                Assert.AreEqual(1, Tag(engine), "a fresh clone gets the published engine");
                Assert.AreEqual(1, Tag(core));
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(sdl));
                Assert.AreEqual(Outcome.Ready, r.Engine.Outcome);

                Publish(2);
                File.WriteAllBytes(Path.Combine(release, "SDL2.dll"), new byte[] { 4, 5, 6 });
                Run(plugins, release, keep, none, 17, 2);
                Assert.AreEqual(2, Tag(engine), "a new build of the same API reaches the clone");
                CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, File.ReadAllBytes(sdl), "and so does a new SDL2");

                File.WriteAllBytes(engine, Lib("okx_api_version", 17, 9));
                Publish(3);
                Assert.AreEqual(Outcome.Ready, Run(plugins, release, keep, none, 17, 2).Engine.Outcome);
                Assert.AreEqual(9, Tag(engine), "a local build of the right API stays");

                var debug = FakeLibrary("okx_api_version", new byte[] { 0x55, 0x48, 0x89, 0xE5, 0xB8, 1 });
                File.WriteAllBytes(engine, debug);
                Assert.AreEqual(Outcome.Local, Run(plugins, release, keep, none, 17, 2).Engine.Outcome);
                CollectionAssert.AreEqual(debug, File.ReadAllBytes(engine), "so does one whose version cannot be read");

                File.WriteAllBytes(engine, Lib("okx_api_version", 16, 5));
                Run(plugins, release, keep, none, 17, 2);
                Assert.AreEqual(3, Tag(engine), "an older API is replaced");
                File.WriteAllBytes(engine, Lib("okx_api_version", 15, 6));
                Run(plugins, release, keep, none, 17, 2);
                var kept = Directory.GetFiles(keep, "replaced-*okengine.dll").Select(Tag).OrderBy(t => t).ToArray();
                CollectionAssert.AreEqual(new byte[] { 5, 6 }, kept, "every local build it replaced is kept aside");

                Publish(4);
                r = Run(plugins, release, keep, (f, e) => f == "okengine.dll" ? 17 : (int?)null, 17, 2);
                Assert.AreEqual(Outcome.Update, r.Engine.Outcome, "loaded, the new build waits for the next start");
                Assert.AreEqual(4, Tag(engine), "in place for it");
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        }

        [Test]
        public void ALoadedLibraryOfAnotherVersionNeedsARestart()
        {
            Assert.AreEqual(Outcome.Restart, Decide(17, 16, 16, true));
            Assert.AreEqual(Outcome.Restart, Decide(17, 17, 16, true), "even once the new file is staged");
            Assert.AreEqual(Outcome.Ready, Decide(17, 16, 17, true), "what is loaded is what counts");
        }

        [Test]
        public void TheVersionIsReadFromTheFileWithoutLoadingIt()
        {
            var dll = FakeLibrary("okx_api_version", new byte[] { 0xB8, 42, 0, 0, 0, 0xC3 });
            Assert.AreEqual(42, ReadVersion(dll, "okx_api_version"));
            Assert.IsNull(ReadVersion(dll, "ok_sim_abi_version"), "no such export");
            var odd = FakeLibrary("okx_api_version", new byte[] { 0x55, 0x48, 0x89, 0xE5, 0xB8, 1 });
            Assert.IsNull(ReadVersion(odd, "okx_api_version"), "code that is not mov eax, N; ret is not guessed at");
            Assert.IsNull(ReadVersion(new byte[64], "okx_api_version"));
            Assert.IsNull(ReadVersion(Encoding.ASCII.GetBytes("MZ not really a library at all, just text"), "okx_api_version"));
        }

        [Test]
        public void ThePublishedLibrariesMatchTheBindings()
        {
            int api = BindingApi();
            Assert.Greater(api, 0, "the engine binding is in the project");
            string engine = Path.Combine(ReleaseDir, $"okengine-api{api}.dll");
            Assert.IsTrue(File.Exists(engine), "engine/ has the library for OkEngine.ApiVersion; run scripts/publish-engine.sh");
            Assert.AreEqual(api, ReadVersion(File.ReadAllBytes(engine), "okx_api_version"), "the published library reports the binding's API");
            string core = Path.Combine(ReleaseDir, $"okcore-abi{OkNative.AbiVersion}.dll");
            Assert.IsTrue(File.Exists(core));
            Assert.AreEqual(OkNative.AbiVersion, ReadVersion(File.ReadAllBytes(core), "ok_sim_abi_version"));
            Assert.IsTrue(File.Exists(Path.Combine(ReleaseDir, "SDL2.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(ReleaseDir, "VERSION")));
        }

        [Test]
        public void TheInstallerHasRunForThisProject()
        {
            Assert.IsNotNull(Engine, "the installer ran when scripts loaded");
            Assert.AreEqual(BindingApi(), Engine.Expected);
            Assert.That(Engine.Outcome, Is.EqualTo(Outcome.Ready).Or.EqualTo(Outcome.Restart).Or.EqualTo(Outcome.Update).Or.EqualTo(Outcome.Local));
            Assert.IsTrue(File.Exists(Path.Combine(PluginDir, "okengine.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(PluginDir, "SDL2.dll")));
        }

        // A PE32+ image with one section holding an export table with one
        // named function made of code.
        static byte[] FakeLibrary(string export, byte[] code)
        {
            var d = new byte[0x400];
            void U16(int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); }
            void U32(int at, int v) { U16(at, v & 0xFFFF); U16(at + 2, (v >> 16) & 0xFFFF); }
            d[0] = (byte)'M'; d[1] = (byte)'Z';
            const int pe = 0x40, opt = pe + 24, optSize = 240, sections = opt + optSize;
            U32(0x3C, pe);
            U32(pe, 0x00004550);
            U16(pe + 4, 0x8664);
            U16(pe + 6, 1);
            U16(pe + 20, optSize);
            U16(opt, 0x20B);
            const int va = 0x1000, raw = 0x200;
            U32(opt + 112, va);
            U32(opt + 116, 0x100);
            U32(sections + 8, 0x200);
            U32(sections + 12, va);
            U32(sections + 16, 0x200);
            U32(sections + 20, raw);
            // Export directory at va, then its tables, the name and the code.
            int funcs = va + 40, names = va + 44, ords = va + 48, name = va + 52, fn = va + 96;
            U32(raw + 20, 1);
            U32(raw + 24, 1);
            U32(raw + 28, funcs);
            U32(raw + 32, names);
            U32(raw + 36, ords);
            U32(raw + 40, fn);
            U32(raw + 44, name);
            U16(raw + 48, 0);
            var n = Encoding.ASCII.GetBytes(export);
            Array.Copy(n, 0, d, raw + 52, n.Length);
            Array.Copy(code, 0, d, raw + 96, code.Length);
            return d;
        }
    }
}
