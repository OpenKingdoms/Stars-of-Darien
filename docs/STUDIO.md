# Studio

The studio is the set of tools inside the Unity editor for looking at the game's content and replacing it. Everything here works on `IGameBackend`, so it runs on the real engine when `okengine.dll` and your game files are present, and on the mock engine when they are not. A fresh clone gets `okengine.dll` from `engine/` when the editor starts (see `engine/README.md`), and OpenKingdoms, then Settings, says which engine runs and where it looks for the game.

For artists, Studio Mode puts all of this in one place. `docs/STUDIO_MODE.md` is its guide.

## Playing the remaster

Open the project and pick OpenKingdoms, then Play Remaster. It opens `Assets/Scenes/Remaster.unity`, which holds only a `GameRoot`, and enters Play. The game starts at the main menu. With the engine and your game files present, the maps and kingdoms are the original ones. Without them, the mock engine offers four made-up maps with boxy soldiers, so the menus, the world view and the tools can be worked on anywhere.

## Drop-in models

A model dropped into one of these folders replaces the original at load, with no code changes.

| Folder | What goes there | Committed |
| --- | --- | --- |
| `unity/Assets/Overrides/Units/` | Hand-made unit models, named after the unit | Yes |
| `unity/Assets/Overrides/Features/` | Feature models, named after the feature: hand-made ones, and carved ones from `batch.py` and `henge.py` that ship as geometry and are painted at load from the player's own game files | Yes |
| `unity/Assets/Overrides/Generated/` | Review copies of carved models that still hold your own sprites' pixels (`OK_KEEP_PIXELS=1`) | Never, it is in `.gitignore` |
| `unity/Assets/Overrides/Drop/` | Models Studio Mode is trying out, not read by the game | Never, it is in `.gitignore` |

A unit model with a `.json` of the same name beside it in `Units` (`ARALODE.glb` and `ARALODE.json`) is a card model. It replaces only the piece named by `replacesPiece` in the JSON, with `replacesTexture` its texture, and the unit keeps its other pieces. Studio Mode writes both files for a unit card.

The file name is what counts, in any case: `araking.glb` replaces the unit or model named AraKing, and `AraTree01.glb` replaces the feature AraTree01. A unit is looked up by its unit name first and then by its model name. A feature is looked up by its name, then its sprite sequence name, then its model name.

When there is more than one candidate, a model in `Features` beats one in `Generated`. A carved model goes into `Features` only for a feature with no hand-made model, so the hand-made one always wins. Within a folder a `.prefab` beats a `.glb`, which beats a `.gltf`, which beats an `.fbx`. A sprite feature with no model at all is drawn as the original sprite on an upright card.

### Units, scale and facing

Models are in map cells. One Blender or glTF unit is one cell, which is 16 pixels of the original game. The model is Y up, and its origin is the feature's anchor on the ground, which for a sprite is the sprite's hotspot. Blender's -Y, the south side that faces the classic camera, is exported to glTF as +Z, and the game turns it to face south on the map.

### Pieces

A unit model can move with the original's animation. Any node whose name matches a piece of the original model (for example `torso`, `head`, `larm`) follows that piece's pose from the unit script. Everything else rides the unit's root. The piece names are listed in the Unit Browser.

### Materials

Plain lit materials work best: a base colour texture and a plain emissive colour for anything that glows. The loader reads a glow as the brightest channel of the emissive colour times the base colour, so give glowing parts a bright base colour too. Transparent pixels in the base colour are cut out, so leaves and fences can be painted on cards. Every drop-in model casts and takes shadows like the originals.

### Formats and where they load

`.glb` files are read by the game's own loader (`GlbLoader`), in the editor and in a built game alike, with no extra package. `.fbx` and `.prefab` files load through Unity's asset database, so they work in the editor only for now. The engine has its own loader for shipped or modded builds, which reads `.glb` files from `unity/Assets/StreamingAssets/Overrides/` under the same names. Use the `Overrides` folders above while you iterate in the editor.

## Studio Mode

OpenKingdoms, then Studio Mode, opens a scene of its own (`Assets/Scenes/Studio.unity`, made on first use and never committed) with the remaster's sky, sun, weather and sea over a neutral island or a map's own ground, and docks the Studio View, the Studio panel and Studio Drop. A model dropped on it stands on a turntable beside a monarch and the original it replaces, with the footprint, the anchor, the classic and free views and a ghost of the original. The panel checks the model, fixes its size, anchor and turn, writes it into `Overrides` with Use in game and starts a battle with Play here. The code is in `unity/Assets/Game/Editor/StudioMode`, and `docs/STUDIO_MODE.md` is the guide.

## Flight

Winged flyers flap and glide by a small animator rather than by the script's coin toss. The engine still decides where a flyer is, how high it flies and whether it is in the air. On top of that height the animator draws the flyer a little higher or lower, inside a band a fraction of a cell deep. While the wings beat, the flyer rises through the band at its climb rate. At the top the wings settle into a glide pose and it sinks at its sink rate, which stands for wing loading. At the bottom it beats again. Takeoff, climbing over rising ground, hovering and flying slower than a stall speed keep the wings beating. Landing, an attack from the air and a death hand the pieces back to the script, so the landing, the attack and the fall play as the original has them. A heavy flyer such as the pegasus sinks fast, so it beats in a steady rhythm most of the time. A light one such as the spyhawk sinks slowly and glides most of the time. Nothing here reaches the game, so every player may see a flyer at a different point of its beat.

The whole flyer beats on the animator's clock, not only its wings. The table lists every piece the original's `fly` and `soar` move, the wings, body, neck, head, tail and legs, each with its own stroke and its own lag behind the wings, so a gliding dragon's neck and tail glide with it and a pegasus's legs pump in step with its wings. When the engine's studio can play a flyer's flight, which needs it to run the script's `BeginFlight` before `fly` and `soar`, the renderer bakes those two functions into clips instead, while a battle loads or the first time a flyer type is drawn. Each is posed a tick at a time, cut to one cycle and kept as keyframes of every piece it moves, and the animator plays `fly` at its own beat while the wings beat and `soar` while the flyer glides, crossing from one to the other. Today's okengine does not begin the flight in its studio, so every flyer takes the table's poses and the log says so.

The numbers live in `unity/Assets/Overrides/Units/flight.json`. An entry is found by unit name, then by model name, in any case. Values come from `defaults`, then the entry's `class` (heavy, medium or light), then the entry's own keys, so any of them can be set for one unit.

| Key | Meaning |
| --- | --- |
| `period` | Seconds for one wingbeat at cruise, which the `fly` function is played to |
| `glides` | `false` for a flyer that never glides, such as the fallen angel and the bird, whose scripts never soar |
| `downstroke` | For the table's poses, the share of the beat spent going down. A `fly` function has its own |
| `amplitude`, `forcedAmplitude` | For the table's poses, the stroke size, 1 being the full sweep from the down pose to the up pose, and the size while the beat is forced |
| `forcedPeriod` | The period's multiplier while the beat is forced, below 1 for a harder beat |
| `climb` | Cells a second the flyer rises while beating |
| `sink` | Cells a second it sinks while gliding at top speed |
| `sinkSlow` | How much faster it sinks at stall speed |
| `lower`, `upper` | The band, in cells below and above the engine's height |
| `stall` | Stall speed as a share of the unit's top speed |
| `climbForce` | Cells a second of climbing over rising ground that force a beat |
| `ease`, `blend` | Seconds to ease between the beat and the glide, and between the script and the animator |
| `jitter` | How much each unit's beat differs, so a flock drifts apart, from 0 to below 1 |
| `wobbleHz` | How often the small corrections of a glide come |

Each entry lists the pieces its flight moves. The first with a `wobble` is the wing that sets the stroke: the top of the stroke is where it reaches its up pose and `downstroke` is the share of the beat it takes to reach its down pose. `down`, `up` and `glide` are a piece's turns at the bottom of its own stroke, the top and in a glide, in degrees, written exactly as a unit script writes `turn piece to x-axis`. A missing glide is halfway between down and up. `lag` is the share of a beat a piece reaches its top after the wing does, which gives an outer wing its fold and a tail its trailing swing. A stroke that folds or loops rather than swinging on one hinge gives `mid`, the pose halfway through the upstroke, or `midDown`, halfway through the downstroke, and the piece passes through it. `mirror` names the right-side piece, which takes the same poses reflected. `downMove`, `upMove`, `midMove`, `midDownMove` and `glideMove` move a piece in engine pixels, as a script's `move` reads in a disassembly. `keepY` keeps the script's own turn about y, for a head or neck the script's head turner swings, and `wobble` is how many degrees the piece rocks while gliding. Pieces not listed keep the script's pose, and the children of a listed piece follow it. A drop-in model whose nodes are named after the pieces follows them either way. `clips` names the functions that play `flap` and `glide`, `fly` and `soar` unless set, and later the clips of a skinned model, which will play by the same states once the model loader reads skins.

To tune, edit the file and pick OpenKingdoms, then Studio, then Reload Flight Table. It works during Play.

## Studio windows

The browsers live under OpenKingdoms, then Studio. They share one backend in edit mode, the engine when it and your game files are present and the mock otherwise. The Use mock button in their toolbar switches. The backend is let go before Play, since the engine runs one game at a time, and comes back afterwards.

The Unit Browser lists every unit the backend knows, filtered by kingdom or by a search. The selected unit turns on a turntable in the team colour you pick. Drag to turn it yourself and use the wheel to zoom. When the unit has script animations, pick one and it plays, posed by the engine's own unit script. The panel under the preview lists the model's pieces, which are the names a drop-in model's nodes can use to follow them.

The Map Browser lists every map with its overview picture, size, player count and climate. Play a skirmish on it opens the remaster scene and starts a game on that map straight away, with the default seats.

The Sprite Replacement window is the work list for the 3D replacements. It reads `catalog.json` and the `sprites` folder that `tools/sprite-replace/extract.py` writes from your own game files, by default in `D:/OKReplace`, and shows every sprite-only feature with how many maps use it and whether it has a model yet, hand-made or generated. The bar at the top counts progress. The selected feature shows its sprite beside its model on a turntable. Open Blender template starts Blender on `tools/sprite-replace/template.py` with the feature's name, its sprite, the path of the hand-made model to save (`Assets/Overrides/Features/<feature>.glb`) and its footprint and height. The Blender and template paths are fields in the window.

## Map editor

The map editor is part of the game rather than the Unity editor, so it works in a built game too. Pick Map editor on the main menu, choose a map, and Open brings it up through the loading screen with the world held still.

The panel on the left holds the tools. Raise, Lower, Flatten and Smooth work on the ground's heights under a round brush while the left button is held. Flatten levels toward the height where the stroke began. Paint gives the blocks under a square brush a picture from the game's own library, which the panel shows a page at a time, tiled so neighbouring blocks show neighbouring parts of it. Features places the feature picked from the list, which can be searched, and Erase takes away the feature nearest the pointer. Brush sets the size in cells and Strength how hard each stroke works. Only the patches of ground under a stroke are rebuilt, so edits show at once.

Save as new map in the bar on top saves the map under the name in the field, and it then shows in the skirmish list. It never replaces a map. A name that any map already has is refused, and the field offers the next free one. Saved maps go to the player's own maps folder, never into the game's files.

## Later tools

The animation editor comes later. The Unit Browser's preview and the backend's `PoseModel` call are the seam it builds on.
