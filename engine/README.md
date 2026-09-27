# Released engine libraries

This folder holds the engine libraries a fresh clone needs, so nobody has to build the engine to open the project. They live outside `unity/Assets` on purpose. The editor copies the right ones into `unity/Assets/Plugins/x86_64` when it starts, before anything calls them, and that folder stays out of git.

| File | What it is |
| --- | --- |
| `okengine-api<N>.dll` | The OpenKingdoms engine as a library, for binding API version N (`OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs`) |
| `okcore-abi<N>.dll` | The small core behind the capsule demo, for `OkNative.AbiVersion` in `unity/Assets/Scripts/OkSim.cs` |
| `SDL2.dll` | SDL 2, which okengine loads for sound. It is under the zlib license |
| `VERSION` | Where each file was built from |

## What the editor does with them

`EngineInstaller` (in `unity/Assets/Game/Editor`) runs every time scripts load. It reads the API version of the `okengine.dll` in the plugin folder straight from the file, without loading it, and remembers in `unity/Library/OkEngine/installed.json` the exact bytes of every file it put there.

- A missing library is copied in from here.
- A library of another version than the binding expects is replaced by the one here. A build made on that machine is kept under `unity/Library/OkEngine` first.
- A library the installer put there is replaced whenever this folder has a different build of it, so a new build of the same API version, or a new `SDL2.dll`, reaches every clone on its next pull.
- A library built on that machine is left alone when its version matches, or when its version can't be read, as with a Debug build. The binding checks the version itself when the engine starts.

Unity cannot unload a native library once it has used it. If a pull brings a new binding while the editor still has the old engine loaded, the installer puts the new file in place for the next start, turns the engine off for this session so nothing calls the old one with the new binding, and asks for a restart. The studio and the game run on the stand-in world until then. A new build of the same version is also put in place for the next start, and the loaded one keeps working until then.

## Publishing a new engine

1. Build okengine as Release with `scripts/build-engine.sh`, or take a published build such as `D:\OKBuild\okengine-published`. A Debug build's version can't be read, and the script refuses it.
2. When the API changed, bump `OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs` to the engine's `OKX_API_VERSION`.
3. Run `bash scripts/publish-engine.sh <folder>`. It needs Python on PATH. For `okengine.dll` (with `SDL2.dll` beside it) it checks the library's own version against the binding, writes `okengine-api<N>.dll` and `SDL2.dll` here and removes the older ones. For `okcore.dll` it does the same against `OkNative.AbiVersion`. The folder may hold either or both, and `VERSION` is rewritten.
4. Commit this folder in the same commit as the binding change. The EditMode test `EngineInstallerTests` fails if the library here does not match the binding.

A new build of the same API version needs no bump. Publish it the same way and every clone picks it up.
