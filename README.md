# openkingdoms-unity

A working name. The game will get its own name once the trademark search is done.

This is a new real-time strategy game built in Unity. The simulation, pathfinding, networking and determinism come from a C core that started as the general parts of [OpenKingdoms](https://github.com/OpenKingdoms/OpenKingdoms). The world, art, units and rules are new. Nothing here uses Total Annihilation: Kingdoms data, names or story, and nothing may.

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

1. Run `scripts/build-core.sh`. It needs CMake and a C compiler (Visual Studio on Windows). Set `OKCORE_BUILD` to put the build tree elsewhere.
2. Open `unity/` in Unity Hub with Unity 6 LTS. Hub will offer to pick the editor version on first open.
3. Press Play in any scene. The game builds itself. `docs/ITERATE.md` lists the controls.

## Rules of the road

- The C side owns all game state and all randomness. Unity draws, takes input, runs the UI and plays sound, and never feeds rendering results back into the sim.
- `ok_sim_hash` must match on every platform. `test_the_script_hash_is_pinned` holds the value, and CI checks it on Windows and Linux. A change to the rules moves the pin on purpose, in the same commit.
- A change to a function or struct in `ok_sim.h`, `ok_turn.h` or `ok_ai.h` bumps `OK_SIM_ABI_VERSION` and `OkNative.AbiVersion` together.
- Nothing from the original game: no data files, names, factions, characters, logos, or art traced from its models or sprites.
- No code arrives from anyone but Zach until a contributor agreement is in place.

## License

Private and proprietary. See `LICENSE`.
