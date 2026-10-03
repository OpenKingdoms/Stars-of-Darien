# Stars of Darien: notes for AI coding assistants

This file is read automatically by Claude Code and similar tools. It says how this project likes to be worked on. Humans will find the same ground in `CONTRIBUTING.md`.

## What this project is

A Unity remaster of Total Annihilation: Kingdoms (1999). The game itself runs in the OpenKingdoms engine (github.com/OpenKingdoms/OpenKingdoms, branch `unity-embed`), which Unity loads as a native library called okengine. The engine owns all game state and all randomness. Unity draws, takes input, runs the interface and plays sound, and every order goes back through the engine's command queue. Nothing Unity draws feeds back into the game. Changes to the engine go to the OpenKingdoms repository.

## Ground rules

- Nothing from the original game goes into this repository. That means no data files, no pictures, models or sounds extracted from them and nothing painted or traced from them, not even as a test fixture. The game reads the player's own copy at run time.
- Models are glTF binaries (`.glb`). Their pictures are the artist's own or licensed to share. A model made from the original ships as geometry and is painted at load from the player's files. `docs/CONTRIBUTING-MODELS.md` has the rules and the budgets.
- Tests first. A change that alters behaviour comes with a test that fails without it, and a bug fix with one that reproduces the bug.
- `OkEngine.ApiVersion` matches the engine's `OKX_API_VERSION`. A change to the embedding API bumps both, and the new libraries go into `engine/` in the same commit, as `engine/README.md` says.
- Comments are brief, one to three lines saying what a reader can't see from the code. The story of a change goes in the commit message and the pull request.
- Prose in docs and comments is plain sentences, with no em or en dashes, no semicolons, no bold lead-ins on bullets, no filler and straight quotes only.
- Commit messages have a present tense subject saying what the game now does and a body saying why. No tool attribution trailers.

## Building and testing on Windows

From Git Bash, with Unity 6000.3.25f1 installed.

- `bash scripts/csharp-check.sh` compiles the game's scripts against the editor's assemblies and runs the quick tests, with no Unity license. Set `UNITY_DATA` to the editor's `Data` folder when it isn't `D:/Unity/6000.3.25f1/Editor/Data`.
- `powershell -File scripts/unity-test.ps1 -Unity <path to Unity.exe>` runs the EditMode and PlayMode suites headless. Close the editor on the project first.
- `bash scripts/build-engine.sh` builds `okengine.dll` from an OpenKingdoms checkout of `unity-embed` with Visual Studio and vcpkg, and `bash scripts/publish-engine.sh <folder>` puts a build into `engine/`.
- `python3 scripts/check-models.py --changed origin/main` runs the model check every pull request gets.

## Building and testing on macOS

On Apple silicon, with Unity 6000.3.25f1 from Unity Hub and Xcode's command line tools.

- The engine comes with the clone as `engine/okengine-api<N>.dylib`, and the editor puts it in `unity/Assets/Plugins/macOS` when scripts load. Nothing needs building to open the project.
- `bash scripts/build-engine-mac.sh` builds `libokengine.dylib` from an OpenKingdoms checkout of `unity-embed` (`~/dev/OpenKingdoms`, or set `OK_ENGINE_SRC`), with SDL2 built from source and linked in. It runs the engine's own `test_embed` and puts the library in the plugin folder, where Unity loads it after a restart. It needs cmake, and a Homebrew under `/usr/local` is kept out of the build.
- `bash scripts/unity-test-mac.sh EditMode` runs one suite headless, and with no argument it runs both. Close the editor on the project first. Results and logs go to `~/unity-test`.
- The game files are looked for in `~/Games/Total Annihilation Kingdoms`. OpenKingdoms, then Settings, in the editor picks another folder, and `OK_GAME_DIR` wins over both.

## Where things are

- `unity/Assets/Engine/` has the engine binding (`OkEngine.cs`), the backend the game reads, and the search for the game files.
- `unity/Assets/Game/Editor/` has the studio windows, the engine installer and the alpha build.
- `unity/Assets/Tests/` has the EditMode suites in `Editor` and `Studio`, and the PlayMode suite.
- `engine/` has the published engine libraries and a record of where each was built from.
- `docs/STUDIO.md` and `docs/STUDIO_MODE.md` cover the studio and the drop-in model workflow.
- `docs/CONTRIBUTING-MODELS.md` is the guide for artists, and `docs/ITERATE.md` covers playing, testing and captures.
