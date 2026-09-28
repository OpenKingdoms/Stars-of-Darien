# Released engine libraries

This folder holds the engine libraries a fresh clone needs, so nobody has to build the engine to open the project. They live outside `unity/Assets` on purpose. The editor copies the right ones into `unity/Assets/Plugins/x86_64` when it starts, before anything calls them, and that folder stays out of git.

| File | What it is |
| --- | --- |
| `okengine-api<N>.dll` | The OpenKingdoms engine as a library, for binding API version N (`OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs`) |
| `okcore-abi<N>.dll` | The small core behind the capsule demo, for `OkNative.AbiVersion` in `unity/Assets/Scripts/OkSim.cs` |
| `SDL2.dll` | SDL 2, which okengine loads for sound. It is under the zlib license |
| `VERSION` | Where each file was built from |

## What the editor does with them

`EngineInstaller` (in `unity/Assets/Game/Editor`) runs every time scripts load. It reads the API version of the `okengine.dll` in the plugin folder straight from the file, without loading it. When that file is missing or has another version than the binding expects, it copies `okengine-api<N>.dll` over it, along with `SDL2.dll` and the core when they are missing. A locally built engine of the right version is left alone.

Unity cannot unload a native library once it has used it. If a pull brings a new binding while the editor still has the old engine loaded, the installer puts the new file in place for the next start, turns the engine off for this session so nothing calls the old one with the new binding, and asks for a restart. The studio and the game run on the mock engine until then.

## Publishing a new engine

1. Build okengine with `scripts/build-engine.sh`, or take a published build such as `D:\OKBuild\okengine-published`.
2. Bump `OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs` to the engine's `OKX_API_VERSION` if the API changed.
3. Run `bash scripts/publish-engine.sh <folder with okengine.dll and SDL2.dll>`. It checks the library's own version against the binding, writes `okengine-api<N>.dll` here, removes the older ones and updates `VERSION`. An `okcore.dll` in the same folder is checked against `OkNative.AbiVersion` and published as `okcore-abi<N>.dll` the same way.
4. Commit this folder in the same commit as the binding change. The EditMode test `EngineInstallerTests` fails if the library here does not match the binding.
