# Testing and iterating

Two loops, a fast one for the C core and a slower one for Unity. Most game rules live in the core, so most of the time goes to the fast loop.

## One-time setup

1. Open Unity Hub from the Start menu and sign in with your Unity account. Under Preferences, then Licenses, add a Unity Personal license.
2. In Hub, go to Installs, then Locate, and pick `D:\Unity\6000.3.25f1\Editor\Unity.exe`.
3. In Hub, go to Projects, then Add, then Add project from disk, and pick `C:\Projects\openkingdoms-unity\unity`. The first open imports for a few minutes.

## The core loop, seconds per round

Edit `core/src/*.c` or add a test in `core/tests/`, then:

```
cmake --build D:\OKBuild\okcore --config Release
ctest --test-dir D:\OKBuild\okcore -C Release --output-on-failure
```

Every rule change starts as a failing test here. Two tests guard determinism, `the_script_hash_is_pinned` and `the_battle_hash_is_pinned`. When you change a rule on purpose, the hashes move, and you update both pins in the same commit, here and in `unity/Assets/Tests/Editor/OkSimTests.cs`. CI checks the same hashes on Windows and Linux on every push.

The suites are `test_ok_sim` (units, orders, combat, the pinned games), `test_path` (the grid and A*), `test_turn` (lockstep through the relay, with late, repeated and reordered frames) and `test_ai` (the wave opponent, including a whole AI against AI game through the relay).

## The Unity loop

After a core change, close the Unity editor, because it keeps `okcore.dll` open while it runs. Then run:

```
powershell -File scripts\unity-test.ps1
```

That builds the core, runs its tests, copies `okcore.dll` into `unity/Assets/Plugins/x86_64`, and runs the Unity EditMode tests headless. Those tests drive the plugin from C# and check it gives the same hashes as the C tests, so a binding mistake shows up there. It needs the Unity license from the setup above. Without one Unity exits with code 198.

Without a license, `bash scripts/csharp-check.sh` from Git Bash gets most of the way. It compiles every script in `unity/Assets/Scripts` against the editor's own UnityEngine assemblies, then runs the EditMode test bodies in plain .NET against the built plugin. It uses the compiler and runtime inside the Unity install, so nothing else needs installing. It does not replace a real Unity run, because Unity's Mono and the editor itself are not involved.

Then open the project and press Play in any scene, even an empty one. The game builds its own camera, light, ground, walls and units. You play blue from the bottom left, and red raises six waves at the top right and sends each one at you. Each red wave also brings you three more units.

- Left click a unit, or drag a box, to select. Shift adds to the selection.
- Right click the ground to move there in a block, or right click a red unit to attack it.
- S stops the selected units. Idle units fight any enemy that comes close.
- Arrow keys, the screen edge or a middle drag pan the camera. The wheel zooms.
- When one side is gone, R starts a new game.

The top-left label shows the tick, the lockstep turn and the sim hash. Every order goes through a local relay and comes back as a turn, the same path a networked game will use.

Changes to C# scripts only (`unity/Assets/Scripts`) do not need a restart. Unity recompiles when you switch back to it.

## Where things go

- Rules, anything that decides what happens: `core/`, in C, with a test. Unit numbers are the `RULES` table in `core/src/ok_sim.c`. Pathing is `ok_path.c`, lockstep is `ok_turn.c`, and the wave opponent is `ok_ai.c`.
- Anything you see or hear: `unity/Assets/Scripts`, in C#. `SimDriver` runs the game and builds the scene, `UnitPresenter` draws units, `CommandInput` turns clicks into orders, `RtsCamera` moves the camera, and `ArtLibrary` finds models.
- A new command or a new field Unity needs to read: change the header in `core/include` and `unity/Assets/Scripts/OkSim.cs` together, and bump `OK_SIM_ABI_VERSION` and `OkNative.AbiVersion`.
- Art for Nicholas: `unity/Assets/Art/`, as FBX (or glTF with a package), with names from the new world, never from Kingdoms. `docs/ART.md` covers names, scale, pivot, facing and team colour.
