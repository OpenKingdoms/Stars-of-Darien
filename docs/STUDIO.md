# Studio

The studio is the set of tools inside the Unity editor for looking at the game's content and replacing it. Everything here works on `IGameBackend`, so it runs on the real engine when `okengine.dll` and your game files are present, and on the mock engine when they are not.

## Playing the remaster

Open the project and pick OpenKingdoms, then Play Remaster. It opens `Assets/Scenes/Remaster.unity`, which holds only a `GameRoot`, and enters Play. The game starts at the main menu. With the engine and your game files present, the maps and kingdoms are the original ones. Without them, the mock engine offers four made-up maps with boxy soldiers, so the menus, the world view and the tools can be worked on anywhere.

## Drop-in models

A model dropped into one of these folders replaces the original at load, with no code changes.

| Folder | What goes there | Committed |
| --- | --- | --- |
| `unity/Assets/Overrides/Units/` | Hand-made unit models, named after the unit | Yes |
| `unity/Assets/Overrides/Features/` | Hand-made feature models, named after the feature | Yes |
| `unity/Assets/Overrides/Generated/` | Feature models made on your own machine from your own sprites by `tools/sprite-replace/batch.py` | Never, it is in `.gitignore` |

The file name is what counts, in any case: `araking.glb` replaces the unit or model named AraKing, and `AraTree01.glb` replaces the feature AraTree01. A unit is looked up by its unit name first and then by its model name. A feature is looked up by its name, then its sprite sequence name, then its model name.

When there is more than one candidate, a hand-made model in `Features` beats a generated one, and within a folder a `.prefab` beats a `.glb`, which beats a `.gltf`, which beats an `.fbx`. A sprite feature with no model at all is drawn as the original sprite on an upright card.

### Units, scale and facing

Models are in map cells. One Blender or glTF unit is one cell, which is 16 pixels of the original game. The model is Y up, and its origin is the feature's anchor on the ground, which for a sprite is the sprite's hotspot. Blender's -Y, the south side that faces the classic camera, is exported to glTF as +Z, and the game turns it to face south on the map.

### Pieces

A unit model can move with the original's animation. Any node whose name matches a piece of the original model (for example `torso`, `head`, `larm`) follows that piece's pose from the unit script. Everything else rides the unit's root. The piece names are listed in the Unit Browser.

### Materials

Plain lit materials work best: a base colour texture, no emission. Transparent pixels in the base colour are cut out, so leaves and fences can be painted on cards. Every drop-in model casts and takes shadows like the originals.

### Formats and where they load

`.glb` files are read by the game's own loader (`GlbLoader`), in the editor and in a built game alike, with no extra package. `.fbx` and `.prefab` files load through Unity's asset database, so they work in the editor only for now. The engine has its own loader for shipped or modded builds, which reads `.glb` files from `unity/Assets/StreamingAssets/Overrides/` under the same names. Use the `Overrides` folders above while you iterate in the editor.

## Tools that are coming

The Unit Browser, the Map Browser and the Sprite Replacement window live under the OpenKingdoms menu. The map editor and the animation editor come later. The Unit Browser's preview and the `PoseModel` call on the backend are the seam the animation editor will build on, and the Map Browser's Play button is the seam for testing edited maps.
