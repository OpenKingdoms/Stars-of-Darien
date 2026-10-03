# Contributing

Thanks for helping with Stars of Darien. Changes come in as pull requests from a fork, and a maintainer merges them once they pass review.

Models for the scenery and units have their own guide, written for artists, in `docs/CONTRIBUTING-MODELS.md`. This page is for code.

## Before you start

For anything bigger than a small fix, open an issue first and say what you plan, so that no one builds the same thing twice and the approach is agreed before the work.

`README.md` says how to get the game running, and `docs/ITERATE.md` covers playing, testing and captures.

## Rules of the road

- The OpenKingdoms engine owns all game state and all randomness. Unity draws, takes input, runs the interface and plays sound, and every order goes back through the engine's command queue. Nothing Unity draws feeds back into the game.
- Nothing from the original game goes into the repository. That means no data files, no pictures, models or sounds extracted from them, nothing painted or traced from them, and no code decompiled or disassembled from the original.
- The engine libraries in `engine/` are built and published from the OpenKingdoms repository by the maintainers. Changes to the engine go to github.com/OpenKingdoms/OpenKingdoms.
- Comments are brief, a line or two saying what a reader can't see from the code. The story of a change goes in the commit message and the pull request.

## Testing

On Windows with Unity 6000.3.25f1 installed, `bash scripts/csharp-check.sh` from Git Bash compiles the game's scripts and runs the quick tests without opening Unity. Set `UNITY_DATA` to the editor's `Data` folder when the editor isn't in `D:/Unity/6000.3.25f1`. `powershell -File scripts/unity-test.ps1 -Unity <path to Unity.exe>` runs the EditMode and PlayMode suites with the editor closed. A change that alters behaviour should come with a test that fails without it.

`python3 scripts/check-models.py --changed origin/main` runs the model check that every pull request gets.

## Licence

Stars of Darien is under the GNU General Public License, version 3 or later, with an additional permission for the Unity engine in `LICENSE.unity-exception`. By opening a pull request you agree that your contribution is shared under those terms, including that permission.

Everyone taking part follows the `CODE_OF_CONDUCT.md`.
