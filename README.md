# Stars of Darien

A remaster of Total Annihilation: Kingdoms in Unity, running on the [OpenKingdoms](https://github.com/OpenKingdoms/OpenKingdoms) engine. The engine plays the game by the original's rules, and Unity draws it with modern light, shadows, water and effects, an interface built on the original's own screens, and 3D models in place of the scenery the original only painted. It is free and open, and nothing is sold.

You bring your own game. Stars of Darien contains none of the original's data, and it never will. It reads the maps, units, models, sounds and music from your own installed copy of Total Annihilation: Kingdoms. Without one it runs on a mock engine with made-up maps, which is enough to work on the menus, the world view and the tools.

## What is in it

- A skirmish from the main menu. The lobby sits on the original's own screens, with a map browser, start positions, kingdoms, computer seats and the game's options, and leads through the loading screen into the battle.
- The original's controls, plus Shift to queue orders and see them, build counts on Shift and Ctrl, and a formation drag in the style of Total War.
- The battle HUD in the original's layout, in a Carolingian skin that scales to any screen.
- A new sea with surf and wakes, lodestones that breathe light, rock on the cliffs, fog of war drawn the way the original draws it, and the original's effects remastered.
- Hundreds of hand-built models for the trees, stones, ruins and lodestones the original drew as flat pictures. Models that need the original's pictures take them from your game files when the game loads, so none are stored here.
- F9 for a picture of what you see, and Shift+F9 for a short clip.
- A studio for making the game: a unit browser, a map browser, an animation editor, a map editor, and Studio Mode for dropping in a model and comparing it with the original.

`docs/ROADMAP.md` has the plan.

## Getting the game files

Stars of Darien plays the game from your own copy of Total Annihilation: Kingdoms. The GOG edition works, and so does the game folder copied off the original CD. Nothing from it is ever copied into this repository, and none of it is shared.

Tell the game where your copy is under OpenKingdoms, then Settings, in the Unity editor. With nothing set it looks in the GOG edition's default folder, `C:/GOG Games/Total Annihilation Kingdoms`, and on a Mac in `~/Games/Total Annihilation Kingdoms`. The `OK_GAME_DIR` environment variable overrides both.

Without a copy, the game runs on a stand-in world with made-up maps and boxy soldiers, which is enough to work on the menus, the world view, the tools and new models.

## Running it

1. Install Unity Hub and sign in with a Unity account. A free Unity Personal license is enough.
2. Clone this repository and add its `unity/` folder in Hub. Hub offers to install the editor version the project needs, 6000.3.25f1.
3. Open the project and pick OpenKingdoms, then Play Remaster.

The engine libraries come with the clone in `engine/`, and the editor puts them in place when it starts, so there is nothing to build. The engine runs on Windows and on a Mac with Apple silicon. If it can't run, the game says why and what to do instead of starting. `docs/ITERATE.md` covers the controls, the tests and the captures.

## Contributing

Changes come in as pull requests from a fork, and each one is reviewed before it is merged, by the maintainer or, for models, by an art reviewer from the community. Models are the easiest way in. `docs/CONTRIBUTING-MODELS.md` takes an artist from a fork to a model in the game with no code, and `docs/STUDIO_MODE.md` is the guide to the studio where models are tried out. Code follows `CONTRIBUTING.md`, and everyone follows `CODE_OF_CONDUCT.md`.

## How it is put together

- `unity/` is the Unity project. `Assets/Engine/OkEngine.cs` binds the engine's embedding API, and `EngineBackend.cs` offers the engine to the game through `IGameBackend`, which the mock engine also implements.
- `engine/` holds the released engine libraries, built from the OpenKingdoms branch `unity-embed`. `engine/README.md` says how they are installed and published.
- `scripts/` builds and publishes the engine and runs the Unity tests.
- `tools/` holds the kit for hand-built models and the studio's helpers.
- `docs/` has the roadmap, the guide to playing and testing (`ITERATE.md`), and the studio guides (`STUDIO.md` and `STUDIO_MODE.md`).

## Rules of the road

- The engine owns all game state and all randomness. Unity draws, takes input, runs the interface and plays sound, and every order goes back through the engine's own command queue. Nothing Unity draws feeds back into the game.
- `OkEngine.ApiVersion` matches the engine's `OKX_API_VERSION`. A change to the embedding API bumps both, and the new library is published to `engine/` in the same commit.
- Nothing from the original game is ever committed: no data files, sprites, models, sounds or anything extracted from them. The game reads them from the player's own copy at run time.
- Contributions arrive as pull requests, and opening one shares the work under the licence below, including the Unity permission.

## License

Stars of Darien is free software under the GNU General Public License, version 3 or any later version, in `LICENSE`, with an additional permission to combine it with the Unity engine, in `LICENSE.unity-exception`.

Total Annihilation: Kingdoms and everything in it belong to their owners. This project is not affiliated with them or endorsed by them.
