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

That builds the core, runs its tests, copies `okcore.dll` into `unity/Assets/Plugins/x86_64`, and runs the Unity tests headless. The EditMode tests drive the plugin from C# and check it gives the same hashes as the C tests, so a binding mistake shows up there. The PlayMode test boots the demo the way Play does, runs it at twenty times speed until the first red wave sets off, and fails on any logged error. It needs the Unity license from the setup above. Without one Unity exits with code 198.

Without a license, `bash scripts/csharp-check.sh` from Git Bash gets most of the way. It compiles every script in `unity/Assets/Scripts` against the editor's own UnityEngine assemblies, then runs the EditMode test bodies in plain .NET against the built plugin. It uses the compiler and runtime inside the Unity install, so nothing else needs installing. It does not replace a real Unity run, because Unity's Mono and the editor itself are not involved.

Then open the project and press Play in any scene, even an empty one. The game builds its own camera, light, ground, walls and units. You play blue from the bottom left, and red raises six waves at the top right and sends each one at you. Each red wave also brings you three more units.

- Left click a unit, or drag a box, to select. Shift adds to the selection.
- Right click the ground to move there in a block, or right click a red unit to attack it.
- S stops the selected units. Idle units fight any enemy that comes close.
- Arrow keys, the screen edge or a middle drag pan the camera. The wheel zooms.
- When one side is gone, R starts a new game.

The top-left label shows the tick, the lockstep turn and the sim hash. Every order goes through a local relay and comes back as a turn, the same path a networked game will use.

Changes to C# scripts only (`unity/Assets/Scripts`) do not need a restart. Unity recompiles when you switch back to it.

## Playing the remaster

Pick OpenKingdoms, then Play Remaster. The game starts at the main menu, runs on the real engine when `okengine.dll` and your game files are there, and on the mock engine otherwise. Skirmish sets up the map, your kingdom, the computer seats and the options. F1, the Pause key or the Menu button opens the game menu, which saves the game, and F2 opens the options over the battle. Load game on the main menu brings a save back.

In a battle the controls are the original's by default. A left click selects a unit, orders the selection to the ground or at an enemy, or carries out an armed command. A right click or Escape cancels an armed command, or else deselects, and never pauses. Drag a box to select, and hold Shift to add. M, P and G arm move, patrol and guard, Ctrl A arms attack, and Ctrl S stops. Ctrl with a digit makes a group, and the digit alone brings it back. Options, then Controls, switches to a modern scheme where the right button gives orders. The camera pans with WASD, the arrow keys, the screen edge or a Shift middle drag, and zooms with the wheel. A middle drag up and down tilts it and raises or lowers it together, and left and right turns it. Q and E also turn it, Page Up and Page Down tilt it, and Home returns to the classic view.

With units selected, hold the order button on the ground and drag to set them out in a formation, the way Total War sets units in battle. The order button is the right one in the modern scheme. In the classic scheme it is Ctrl with the left button, or the right button held for a fifth of a second, so a quick right click still cancels however far the hand moves. The line you draw is the front rank and the direction of the drag sets the facing, left to right facing away from the camera. A drag shorter than two cells sets no direction. The formation then faces the way the camera looks and stands centred on the press, as a click would. Melee stands in front, archers behind, cavalry on the wings, and the monarch at the back. A line is never more than four ranks deep, and a unit wider than the rest of its group, such as a catapult or a god, stands in a rank of its own behind so the others keep their spacing. Tab changes the shape between Line, Block, Wedge and Loose, F turns it about, Alt keeps the group's current layout, and Shift at the release queues it. Each role block marches at the pace of its slowest unit, so the knights are not held back to the catapults, and G lets every unit keep its own pace for that order. Escape or the other button lets the drag go. A press on an enemy still attacks it, and a short press still does what a click did before.

In the Unity editor, keys reach the game only while the Game view has focus. With Play Focused chosen in the Game view's toolbar it has focus as soon as Play starts. After clicking in another editor window, click the Game view once to give the keys back. That click may be taken as focus rather than as a click in the game.

The battle screen keeps the original's layout. A sidebar on the right holds the minimap at the top, the Menu button and the clock, the orders, spells and stances in the original's places, a help box, and the crystal ball with the mana pool, income on its left and spending on its right. A strip along the bottom shows the selected unit, its health, its mana and what it is doing, and at its right end the unit under the pointer or the one the selection is after. Everything a selected builder or factory can make shows at once in a row above the strip. A builder's card shows a ghost of the building, green where it can stand. A factory's card queues a unit, Shift queues five, and a right click takes one off the queue. Options, then Interface size, scales the panels from 60 to 130 percent, 100 being the original at 640 by 480, and Key letters hides the key on each button. The world camera draws only the play area beside and above the panels.

## Testing the remaster

The EditMode tests cover the screen flow, the battle HUD's layout at every screen size and interface size, the mock engine, the terrain builder, the override rules, the glb reader, the studio windows, Studio Mode and the engine installer. The PlayMode tests go from the main menu through skirmish setup and the loading screen into a running game and back, save and load a game, play a fight to its end, and time five hundred units on screen. All of them run on the mock engine, so they need no game files.

The capture test draws every screen into PNG files without a window. It runs only when `OKU_CAPTURE_DIR` names a folder. Set `OKU_CAPTURE_BACKEND=engine` to use the real engine, `OKU_CAPTURE_MAP` to pick a map by name, and `OKU_CAPTURE_W` and `OKU_CAPTURE_H` for another size than 1920 by 1080. With `OKU_CAPTURE_TREES=1` it also frames the thickest stand of trees with the fog and weather off. With `OKU_CAPTURE_HUD=1` and the engine, `HudCaptures` draws the battle HUD for each kingdom's monarch and for the building it raises that builds the most, at 1280 by 720, 1920 by 1080 and 3840 by 2160, and `OKU_CAPTURE_SIDES` limits it to some kingdoms. The pictures in `docs/images/studio-mode` come from the EditMode test `CapturesForTheGuide` on the mock, which runs only when `OKU_STUDIO_SHOTS` names a folder. Pictures of the formation drag in each shape come from the PlayMode test `FormationCaptures`, which runs only when `OKU_FORMATION_SHOTS` names a folder.

## Where things go

- Rules, anything that decides what happens: `core/`, in C, with a test. Unit numbers are the `RULES` table in `core/src/ok_sim.c`. Pathing is `ok_path.c`, lockstep is `ok_turn.c`, and the wave opponent is `ok_ai.c`.
- Anything you see or hear: `unity/Assets/Scripts`, in C#. `SimDriver` runs the game and builds the scene, `UnitPresenter` draws units, `CommandInput` turns clicks into orders, `RtsCamera` moves the camera, and `ArtLibrary` finds models.
- A new command or a new field Unity needs to read: change the header in `core/include` and `unity/Assets/Scripts/OkSim.cs` together, and bump `OK_SIM_ABI_VERSION` and `OkNative.AbiVersion`.
- Art for Nicholas: `unity/Assets/Art/`, as FBX (or glTF with a package), with names from the new world, never from Kingdoms. `docs/ART.md` covers names, scale, pivot, facing and team colour.
