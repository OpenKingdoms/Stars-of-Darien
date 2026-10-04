# Released engine libraries

This folder holds the engine libraries a fresh clone needs, so nobody has to build the engine to open the project. They live outside `unity/Assets` on purpose. The editor copies the right ones into `unity/Assets/Plugins/x86_64` when it starts, before anything calls them, and that folder stays out of git. On a Mac they go into `unity/Assets/Plugins/macOS`, also out of git.

| File | What it is |
| --- | --- |
| `okengine-api<N>.dll` | The OpenKingdoms engine as a library, for binding API version N (`OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs`) |
| `SDL2.dll` | SDL 2, which okengine loads for sound. It is under the zlib license |
| `okengine-api<N>.dylib` | The same engine for a Mac with Apple silicon, with SDL 2 linked in |
| `VERSION` | Where each file was built from |

## What the editor does with them

`EngineInstaller` (in `unity/Assets/Game/Editor`) runs every time scripts load. It reads the API version of the `okengine.dll` in the plugin folder straight from the file, without loading it, and remembers in `unity/Library/OkEngine/installed.json` the exact bytes of every file it put there.

- A missing library is copied in from here.
- A library of another version than the binding expects is replaced by the one here. A build made on that machine is kept under `unity/Library/OkEngine` first.
- A library the installer put there is replaced whenever this folder has a different build of it, so a new build of the same API version, or a new `SDL2.dll`, reaches every clone on its next pull.
- A library built on that machine is left alone when its version matches, or when its version can't be read, as with a Debug build. The binding checks the version itself when the engine starts.
- A library the project no longer uses, such as the old `okcore.dll`, is removed when the installer put it there.

On a Mac the same rules apply to `libokengine.dylib`, published here as `okengine-api<N>.dylib`. SDL is inside it, so nothing goes beside it. The installer also writes the library's `.meta`, which tells Unity to load it in the editor and in a Mac player, and clears the quarantine flag a downloaded zip leaves on it.

On Linux the same rules apply to `libokengine.so`, published here as `okengine-api<N>.so`, and SDL comes from the system rather than from this folder. No Linux build is published yet, so `scripts/cloud-unity-test.sh` builds one and puts it in the plugin folder, where the installer reads its version from the file and leaves it alone.

Unity cannot unload a native library once it has used it. If a pull brings a new binding while the editor still has the old engine loaded, the installer puts the new file in place for the next start, turns the engine off for this session so nothing calls the old one with the new binding, and asks for a restart. The studio and the game run on the stand-in world until then, and Play in a scene without the game says what is wrong. A new build of the same version is also put in place for the next start, and the loaded one keeps working until then.

## Publishing a new engine

1. Build okengine as Release with `scripts/build-engine.sh`, or take a published build such as `D:\OKBuild\okengine-published`. A Debug build's version can't be read, and the script refuses it.
2. Leave the Mac library to GitHub, or build it from the same unity-embed commit on a Mac with `scripts/build-engine-mac.sh`, which leaves `libokengine.dylib` and `VERSION-macos.txt` in `~/okbuild/embed-mac/publish` to copy beside `okengine.dll`. The next section says what GitHub does.
3. When the API changed, bump `OkEngine.ApiVersion` in `unity/Assets/Engine/OkEngine.cs` to the engine's `OKX_API_VERSION`.
4. Run `bash scripts/publish-engine.sh <folder>`. It needs Python on PATH. It checks the version of every library in the folder against the binding, writes them here as `okengine-api<N>.dll` with the `SDL2.dll` beside it and `okengine-api<N>.dylib`, removes the older ones and rewrites `VERSION`. A folder with only one of the two publishes that one and keeps the other.
5. Commit this folder in the same commit as the binding change. The EditMode test `EngineInstallerTests` fails if a library here does not match the binding.

A new build of the same API version needs no bump. Publish it the same way and every clone picks it up.

When the API changes and no Mac build comes with it, the script removes the old Mac library, since it can't run with the new binding. A Mac then shows the stand-in world and says so in the editor until a Mac build of the new API is published, or until someone on that Mac builds one with `scripts/build-engine-mac.sh`.

## The Mac library from GitHub

A push to main that changes `VERSION` starts the Mac engine workflow, `.github/workflows/mac-engine.yml`. When the Mac library is not from the unity-embed commit `VERSION` names for the Windows one, the workflow checks that commit out on a GitHub Mac with Apple silicon and builds it with `scripts/build-engine-mac.sh`. It checks that `file` and `lipo` say arm64 and that the library exports every `okx_` function `OkEngine.cs` imports. Then it runs `scripts/publish-engine.sh` with the Mac library alone and pushes the result to the `mac-engine` branch, one commit on main that touches only `okengine-api<N>.dylib` and `VERSION`. The library is also kept as the run's artifact `okengine-macos-arm64-<commit>` for 90 days.

Main is protected, so the commit lands through a pull request:

```
gh pr create -R OpenKingdoms/Stars-of-Darien --base main --head mac-engine --fill
```

It merges once model-check passes and a code owner approves, or with `gh pr merge <N> --squash --admin` from a maintainer. The merge changes `VERSION` again, and the workflow sees the Mac library already matches and builds nothing.

Every six hours a scheduled run looks again, so a build that failed, or one whose unity-embed commit reached GitHub after the engine reached main, still gets made. It builds nothing when main or the `mac-engine` branch already has that library.

To build on demand, for example after a failed run, run `gh workflow run mac-engine.yml -R OpenKingdoms/Stars-of-Darien`. Add `-f embed=<commit>` to build another unity-embed commit, which goes to the artifact only, or `-f force=true` to rebuild the one main already has.

## A Mac catching up

`bash scripts/mac-catch-up.sh` brings a Mac's checkout up to date. It fast-forwards the checkout from GitHub when no tracked file has changes, and leaves a checkout with changes, a merge or a rebase completely alone. Then it makes sure `unity/Assets/Plugins/macOS/libokengine.dylib` is the library from the unity-embed commit `VERSION` names. It takes the committed library when that one matches. While GitHub's build still waits on the `mac-engine` branch, it takes the library from that branch with plain git, and failing that with `gh` from the artifact of a Mac engine workflow run on main. An artifact from any other run, such as one a pull request from a fork made, is never taken. It installs the library the way `EngineInstaller` does, as a new file with its import settings and without the quarantine flag. A library the installer did not put there is kept under `unity/Library/OkEngine` first. A library taken from the branch or the artifact stays out of the installer's record, so the editor doesn't swap it back for the older committed one. It prints what it did and appends the same to `~/Library/Logs/stars-of-darien-catch-up.log`.

`bash scripts/mac-catch-up.sh install-auto` installs a launchd agent, `~/Library/LaunchAgents/net.openkingdoms.stars-of-darien.catch-up.plist`, that runs the same catch-up at login, on the hour, on waking and whenever a network comes up. When it pulls or installs something it posts a notification. When it can't pull, because tracked files have changes or the branch has diverged, it says so in one notification, and says it again only after a run that went through. `bash scripts/mac-catch-up.sh remove-auto` removes the agent. `scripts/test-mac-catch-up.sh` tests the script against throwaway repositories, and the Mac engine workflow runs it on a GitHub Mac.
