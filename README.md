# openkingdoms-unity

A working name. The game will get its own name once the trademark search is done.

This is a new real-time strategy game built in Unity. The simulation, pathfinding, networking and determinism come from a C core that started as the general parts of [OpenKingdoms](https://github.com/OpenKingdoms/OpenKingdoms). The world, art, units and rules are new. Nothing here uses Total Annihilation: Kingdoms data, names or story, and nothing may.

## Layout

- `core/` is the C11 simulation library. `core/include/ok_sim.h` is the only interface Unity sees.
- `unity/` is the Unity project. `Assets/Scripts/OkSim.cs` mirrors `ok_sim.h`, and `SimDriver.cs` is the smallest loop over it.
- `docs/CARVE.md` lists what moves over from OpenKingdoms next, and what never does.
- `scripts/build-core.sh` builds the core, runs its tests, and copies the plugin into the Unity project.

## Getting started

1. Run `scripts/build-core.sh`. It needs CMake and a C compiler (Visual Studio on Windows). Set `OKCORE_BUILD` to put the build tree elsewhere.
2. Open `unity/` in Unity Hub with Unity 6 LTS. Hub will offer to pick the editor version on first open.
3. Make a scene, add an empty GameObject with `SimDriver`, and point the camera down at the ground. Right-click to send the blue units.

## Rules of the road

- The C side owns all game state and all randomness. Unity draws, takes input, runs the UI and plays sound, and never feeds rendering results back into the sim.
- `ok_sim_hash` must match on every platform. `test_the_script_hash_is_pinned` holds the value, and CI checks it on Windows and Linux. A change to the rules moves the pin on purpose, in the same commit.
- A change to a function or struct in `ok_sim.h` bumps `OK_SIM_ABI_VERSION` and `OkNative.AbiVersion` together.
- Nothing from the original game: no data files, names, factions, characters, logos, or art traced from its models or sprites.
- No code arrives from anyone but Zach until a contributor agreement is in place.

## License

Private and proprietary. See `LICENSE`.
