# Roadmap

The goal is a remaster of Total Annihilation: Kingdoms in Unity, running on the OpenKingdoms engine. The whole game is playable from the main menu into a skirmish, with modern lighting, weather and an ocean, plus a map editor and an animation editor. The original 3D models are reused. Players supply their own game files, and nothing is sold.

The engine runs inside Unity as `okengine`, a native library built from OpenKingdoms with an embedding API (`include/ok_embed.h` on the OpenKingdoms branch `unity-embed`). Unity draws what the engine simulates, and every order goes back through the engine's own command queue.

## Milestones

Status is one of: done, in progress, next, later.

| | Milestone | Status |
| --- | --- | --- |
| M1 | A real map in 3D with real units standing on it and animating, from the embedded engine | done |
| M2 | Skirmish playable: the engine's rules and AI, a modern HUD (selection, build and train menus, mana, minimap), controls like the game, victory and defeat | in progress |
| M3 | Game flow: main menu, skirmish setup (map, kingdoms, AI seats, options) and the loading screen, in a new UI that echoes the original's feel | later |
| M4 | Modern look: URP, real-time sun and shadows, an ocean with shorelines at the map's sea level, weather, post-processing, terrain with blended textures | later |
| M5 | Studio: Unit Browser and Map Browser, a map editor, and an animation editor that saves piece overrides | later |
| M6 | 3D replacements for features the original draws only as sprites, see `docs/SPRITE_FEATURES.md` | later |
| M7 | Multiplayer over the OpenKingdoms relay | later |

## M1 in detail

- `okengine.dll` (x64) boots the engine headless, loads a skirmish, ticks it, and hands out terrain heights and chunk pictures, models with their pieces and texture atlases, every unit's pose from its unit script, and every feature as a model or a sprite.
- Unity builds the terrain from the heights and chunk pictures, draws every model piece with the pose the engine computed, and draws sprite features as camera-facing quads, as OpenKingdoms' own 3D view does. `Assets/Engine/EngineDriver.cs` is this first view, and `EngineBackend.cs` offers the same engine to the presentation through `IGameBackend`.
- `scripts/build-engine.sh` builds okengine from the OpenKingdoms branch `unity-embed` and copies it with `SDL2.dll` into `Assets/Plugins/x86_64`.
- Tests: `test_embed` in OpenKingdoms, `OkEngineTests` (EditMode) and `EnginePlayTests` (PlayMode) here. The PlayMode test boots two castles, sends a unit east, checks it walked, and with `OK_CAPTURE_DIR` set saves three views as PNG.

## M2 so far

The engine side of a playable skirmish is in place and tested (okengine API 7). Through `IGameBackend` the presentation gets:

- the seats, each player's mana pool and income, every builder's menu with its costs, and victory or defeat
- orders through the engine's command queue: move, attack, build at a site, stop, patrol, guard, factory queues and the rest of the game's commands
- a build site snapped to the grid the way the game places it, with whether it can be built, factory queue counts for build card badges, and what each unit is doing
- the local player's fog, one byte per height sample
- projectiles, and explosions, sparks, blood and smoke as camera-facing frames
- the engine's own sound and music, placed and faded by where the camera looks
- a skirmish load in slices with the loading screen's own progress and status line
- for the Unit Browser, any of a unit's script functions (walk, attack and so on) played outside the battle

What remains for M2 is on the presentation side: the HUD, selection and the controls.

## Drop-in models

One naming rule on both sides: `<unitname>.glb` for a unit and `<feature>.glb` for a feature, matched without regard to case. While iterating in the editor, models go in `unity/Assets/Overrides/Units` and `unity/Assets/Overrides/Features` (glb, gltf, fbx or prefab), and `docs/STUDIO.md` covers scale, pivot and facing. For a shipped or modded build, `.glb` files in `unity/Assets/StreamingAssets/Overrides/` are read by the engine's own glTF loader. That loader goes by the model's object name, which for most units is the unit name, and a node named like a piece of the original model follows that piece's pose.
