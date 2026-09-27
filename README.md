# Darien Reforged

A remaster of Total Annihilation: Kingdoms in Unity, running on the [OpenKingdoms](https://github.com/OpenKingdoms/OpenKingdoms) engine. The engine plays the game by the original's rules, and Unity draws it with modern light, shadows, water and weather, a new interface in the spirit of the original, and 3D models in place of the sprite-only scenery. It is free and open, and nothing is sold.

You bring your own game. Darien Reforged contains none of the original's data, and it never will. It reads the maps, units, models, sounds and music from your own installed copy of Total Annihilation: Kingdoms. Without one it runs on a mock engine with made-up maps, which is enough to work on the menus, the world view and the tools.

`docs/ROADMAP.md` has the plan, `docs/ITERATE.md` how to build, test and play, and `docs/STUDIO.md` the studio and the drop-in models.

The small capsule demo on the C core in `core/`, described below, stays in the project as a fallback scene.

## What the MVP shows

A small real-time battle on a 64 by 64 map, with every rule decided by the C core.

- Two teams of soldiers (melee) and archers (ranged) with health, and move, attack and stop orders.
- Units find their way around walls and rocks with A* on a grid, chase moving targets, and fight on their own when an enemy comes close.
- Every order goes through a lockstep relay and comes back as a numbered turn, the path a networked game uses. A test runs two peers on different simulated networks, with late, repeated and reordered turns, and checks that their sims hash the same on every turn. Another plays a whole AI against AI game through the relay.
- A computer opponent raises six waves and sends each one at the player.
- In Unity: drag select, right click to move or attack, team colours, health bars, arrows in flight, a camera that pans and zooms, and a drop-in folder for Nicholas's models (see `docs/ART.md`).
- Pinned hashes for a scripted march and a scripted battle, checked on Windows and Linux in CI and from C# in the Unity tests.

## Layout

- `core/` is the C11 simulation library. Unity sees only `core/include/ok_sim.h` (the sim), `ok_turn.h` (lockstep turns) and `ok_ai.h` (the wave opponent).
- `unity/` is the Unity project. `Assets/Scripts/OkSim.cs` mirrors the headers, and `SimDriver.cs` runs the demo.
- `docs/ITERATE.md` is how to build, test and play. `docs/ART.md` is how to bring in models.
- `docs/CARVE.md` lists what moved over from OpenKingdoms, what was written fresh instead, and what never comes.
- `scripts/build-core.sh` builds the core, runs its tests, and copies the plugin into the Unity project. `scripts/unity-test.ps1` adds the Unity tests, and `scripts/csharp-check.sh` checks the C# side without a Unity license.

## Getting started

Artists who only want to try models in the game need none of the steps below. `docs/STUDIO_MODE.md` covers cloning, Unity and Studio Mode.

1. Run `scripts/build-core.sh`. It needs CMake and a C compiler (Visual Studio on Windows). Set `OKCORE_BUILD` to put the build tree elsewhere.
2. Open `unity/` in Unity Hub with Unity 6 LTS. Hub will offer to pick the editor version on first open.
3. Press Play in any scene. The game builds itself. `docs/ITERATE.md` lists the controls.

## Rules of the road

- The C side owns all game state and all randomness. Unity draws, takes input, runs the UI and plays sound, and never feeds rendering results back into the sim.
- `ok_sim_hash` must match on every platform. `test_the_script_hash_is_pinned` holds the value, and CI checks it on Windows and Linux. A change to the rules moves the pin on purpose, in the same commit.
- A change to a function or struct in `ok_sim.h`, `ok_turn.h` or `ok_ai.h` bumps `OK_SIM_ABI_VERSION` and `OkNative.AbiVersion` together.
- Nothing from the original game is ever committed: no data files, sprites, models, sounds or anything extracted from them. The game reads them from the player's own copy at run time.
- No code arrives from anyone but Zach until a contributor agreement is in place.

## License

Darien Reforged is free software under the GNU General Public License, version 3 or any later version, in `LICENSE`, with an additional permission to combine it with the Unity engine, in `LICENSE.unity-exception`. The repository is private for now.
