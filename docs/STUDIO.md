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

## Studio windows

All three live under OpenKingdoms, then Studio. They share one backend in edit mode, the engine when it and your game files are present and the mock otherwise. The Use mock button in their toolbar switches. The backend is let go before Play, since the engine runs one game at a time, and comes back afterwards.

The Unit Browser lists every unit the backend knows, filtered by kingdom or by a search. The selected unit turns on a turntable in the team colour you pick. Drag to turn it yourself and use the wheel to zoom. When the unit has script animations, pick one and it plays, posed by the engine's own unit script. The panel under the preview lists the model's pieces, which are the names a drop-in model's nodes can use to follow them.

The Map Browser lists every map with its overview picture, size, player count and climate. Play a skirmish on it opens the remaster scene and starts a game on that map straight away, with the default seats.

The Sprite Replacement window is the work list for the 3D replacements. It reads `catalog.json` and the `sprites` folder that `tools/sprite-replace/extract.py` writes from your own game files, by default in `D:/OKReplace`, and shows every sprite-only feature with how many maps use it and whether it has a model yet, hand-made or generated. The bar at the top counts progress. The selected feature shows its sprite beside its model on a turntable. Open Blender template starts Blender on `tools/sprite-replace/template.py` with the feature's name, its sprite, the path of the hand-made model to save (`Assets/Overrides/Features/<feature>.glb`) and its footprint and height. The Blender and template paths are fields in the window.

## Later tools

The map editor and the animation editor come later. The Unit Browser's preview and the backend's `PoseModel` call are the seam the animation editor builds on, and the Map Browser's Play button is the seam for testing an edited map.
