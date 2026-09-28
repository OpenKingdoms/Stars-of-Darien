# Studio Mode

Studio Mode is a room inside the Unity editor for trying out models for the game. You drop a model in, and it stands on a turntable at the game's own scale, in the game's light, next to a monarch for size and next to the thing it is meant to replace. The studio checks it, fixes the common export slips with one click, puts it into the game with one button, and starts a battle with it so you can see it in play.

You don't need to write any code, and you don't need the original game. With the game installed, the studio shows the real maps, the real units and the original models and pictures to compare against. Without it, a stand-in world with made-up maps and boxy soldiers takes their place, and everything else works the same.

![The classic view, with the sample well on its footprint, the monarch on the left, the original on the right and its ghost over the well](images/studio-mode/classic.jpg)

## Setting up, once

1. Make a GitHub account if you don't have one, and tell Zach its name. He adds you to the project, and GitHub emails you an invitation. Accept it.
2. Install GitHub Desktop from desktop.github.com and sign in with that account. Choose File, then Clone repository, pick `zbennett10/openkingdoms-unity`, and clone it into a folder such as `C:\Projects`.
3. Install Unity Hub from unity.com/download and sign in. Unity asks for a license the first time, and the free Personal license is the one to pick.
4. In Hub, choose Projects, then Add, then Add project from disk, and pick the `unity` folder inside the clone (for example `C:\Projects\openkingdoms-unity\unity`). The project needs Unity 6000.3.25f1 exactly. Hub notices when it is missing and offers to install it. No extra modules are needed.
5. Open the project from Hub. The first open imports everything and takes several minutes. Later opens are quick.

Two things are optional. If you own Total Annihilation: Kingdoms (the GOG edition), pick OpenKingdoms, then Settings, and point Game folder at where it is installed. The default is `C:\GOG Games\Total Annihilation Kingdoms`, so a default GOG install needs nothing. For making models, Blender works well, and any tool that exports glTF or FBX will do.

The game's engine is a Windows library. On a Mac the studio runs on the stand-in world only.

## Opening and leaving

Pick OpenKingdoms, then Studio Mode. The editor opens the studio's own scene and lays out three windows. The Studio View opens as a tab beside the Scene view and shows the stage. The Studio panel opens beside the Inspector and holds everything you can change. Studio Drop opens beside the Project window at the bottom, and is where models come in. Each of them is also under OpenKingdoms, then Studio, if you close one by mistake.

To go back to normal, pick OpenKingdoms, then Leave Studio Mode, or press the button at the bottom of the Studio panel. Your scenes and your window layout come back as they were.

The first time, the studio may take a few seconds to load a map in the background, with a progress bar. Nothing you do in the studio changes the scenes you were working in.

## What is on the stage

Your model stands in the middle on a turntable. A monarch stands to its left for scale. With the game installed it is a real monarch from the game, and without it the stand-in world's boxy one. The original, the thing your model replaces, stands to the right once you have picked it.

On the ground, white lines mark the map's cells around the model and a yellow outline marks the footprint, the cells the original takes up on the map. An orange pin marks the anchor, the point the game stands the model on, and an orange arrow points south, the way the model's front must face.

The Studio View has two views. Classic view is the game's own camera, looking north and down at the angle the original draws everything, and in it a pale blue ghost of the original lies over your model so you can match its outline. Free view lets you look from anywhere. Drag to turn around the model, use the wheel to zoom, and Turn starts or stops the turntable. The toolbar also has a zoom slider, Reset, and Screenshot.

![The free view, from above and to one side](images/studio-mode/free.jpg)

## Bringing a model in

There are four ways, and they all do the same thing.

- Drag a `.glb`, `.gltf`, `.fbx` or `.obj` file onto Studio Drop or onto the Studio View, from Explorer or from Unity's Project window.
- Press Pick a file on Studio Drop.
- Save or export straight into `unity/Assets/Overrides/Drop` in the clone. Open the Drop folder opens it for you. A model saved there shows up by itself within a second, and the folder's models are listed on Studio Drop so you can switch between them.
- Press Load the sample to try the studio with a small stone well.

Once a model is loaded, the studio keeps an eye on its file. Keep Blender open, export over the same file, and the studio reloads it within a second, with your fixes and material changes kept.

glTF Binary (`.glb`) is the best format, because the pictures travel inside the file and it is what the game reads. In Blender, choose File, then Export, then glTF 2.0, and keep the format at glTF Binary and +Y Up ticked, which are the defaults. FBX and OBJ files go through Unity's own importer, and the studio turns them into a glb for the game. A `.gltf` with its `.bin` and pictures beside it is packed into one glb the same way.

## Scale, anchor and facing

One Blender unit (one metre) is one cell of the map, and a cell is 16 pixels of the original game. Use the monarch beside your model to judge size, and the checks below compare it with the original.

The model's origin is its anchor. Put the origin in the middle of the model's base, on the ground, at 0, 0, 0. The studio can do this for you, but getting it right in Blender saves a step every time.

The front of the model faces south, toward the classic camera. In Blender that is the side you see in Front view (numpad 1), which is the -Y side. If you see the back of your model in the classic view, give it a half turn.

## Picking what it replaces

The Replaces part of the Studio panel has three tabs and a search box.

Feature lists the trees, rocks, ruins and other things that stand on the maps. With the game installed and its sprite catalog made, each one shows its footprint and height. A feature's original is a flat picture in the game, and the studio stands it upright beside your model at its true size.

Unit lists every unit. Your model replaces the whole unit. The unit's original model stands beside yours, and any part of your model named like one of the original's pieces (for example `torso`, `head` or `larm`) moves with the original's animation. The checks list the piece names.

Unit card is for the units that stand on a painted card, such as the lodestones. Your model replaces only that one piece and the unit keeps the rest. Pick the piece under Stands in for. The original then shows around your model with that piece taken out, and the ghost shows just the piece.

If your file is named like a feature or unit, for example `AraTree01.glb`, and nothing is picked yet, the studio picks it for you as soon as the model loads.

## Checks and fixes

The Check part of the Studio panel lists what the studio found, in plain words. A green tick is fine, a blue note is for your information, a yellow warning is worth a look, and a red problem has to be sorted out before the model can go into the game. A red problem is usually that nothing has been picked under Replaces yet.

Some warnings come with a button that fixes them.

| Button | What it does |
| --- | --- |
| Centre it on the anchor | Moves the model so the middle of its base is on the anchor and its lowest point is on the ground |
| Match the original's height | Scales the model to the height of the original picture or model |
| Fit it to the footprint | Scales the model to the width of the footprint, for features with no known height |
| Make it 100 times smaller | For a model exported in centimetres |
| Turn it a quarter | For a model that lies across the other way from the original |

Below the list, Turn left, Turn right and Half turn turn the model by quarters, Scale sets its size by hand, Centre centres it on the anchor, and Undo fixes goes back to the model as exported. Fixes never change your own file. They are applied to the copy that goes into the game.

The studio also warns when a feature has more than 3,000 triangles or a unit more than 6,000, since a map can show hundreds of them at once. It warns about pictures larger than 1024 pixels on a side, pictures in a format the game can't read (use PNG or JPEG), pictures a `.gltf` names that are not there, and see-through pictures on a material set to opaque. For leaves, fences and anything else with see-through parts, plug the picture's Alpha into the shader's Alpha in Blender, and the game cuts those parts out.

## Putting it in the game

Press Use in game. If there are warnings, the studio lists them and asks first, and if a model for that thing is already in the game it asks before replacing it. The model goes to the place the game looks for it, with the fixes and, if Keep the material changes is ticked, the material changes baked in.

| Replaces | Written to |
| --- | --- |
| A feature | `unity/Assets/Overrides/Features/<feature>.glb` |
| A unit | `unity/Assets/Overrides/Units/<MODEL>.glb` |
| A unit card | `unity/Assets/Overrides/Units/<MODEL>.glb` and `<MODEL>.json` beside it, naming the piece and texture it replaces |

Then press Play here. For a feature, the studio starts a skirmish on a map where that feature stands, or on the studio's map when it doesn't know one, and places up to three of your model just south-east of your starting position, with the camera over them. For a unit, you play as the unit's kingdom on the studio's map. Monarchs and starting units are there from the start, and anything else your builders can build. Stop Play to come back to the studio just as you left it.

Pressing Play in the editor while Studio Mode is on starts the game from its main menu, the same as OpenKingdoms, then Play Remaster.

## Experimenting

The Look part of the Studio panel changes the stage, not your model. Ground picks a neutral island or a real map's terrain. Climate changes the ground's colour and the sky to grass, snow, desert, swamp or volcanic. Sea turns the water around the neutral island on or off. Weather adds rain, snow or fog, Time of day moves the sun from morning to night, and Shadows turns shadows off and on. Team colour tints any material with "team" in its name, to preview a player's colour on it. The game itself does not tint drop-in models yet.

The Material part changes your model. Tint and Brightness change its colour, Roughness makes it shinier or duller (untick Keep own roughness first), and Self light makes it glow, for braziers, crystals and magic. Reset the material puts it back.

![Falling snow in the game's own light](images/studio-mode/snow.jpg)

![The same in the evening](images/studio-mode/evening.jpg)

![Night, with a warm tint and some self light on the well](images/studio-mode/night-glow.jpg)

The Show part turns the monarch, the grid, the anchor, the original and the ghost on and off. Screenshot, classic and Screenshot, free save a 1920 by 1080 picture into the `Captures` folder at the top of the clone, next to the `unity` folder, and Open the Captures folder opens it. Frame it in the Scene view points Unity's own Scene view at your model, if you want Unity's tools on it.

![A mock map's own ground with the sea around it](images/studio-mode/map-ground.jpg)

## Settings

OpenKingdoms, then Settings, holds the few things the studio needs to know about your computer. Game folder is where the original game is installed, and Browse picks it. The status line under Engine says in plain words whether the real engine and your game files will be used, or the stand-in world and why. Use these settings now applies a change without restarting Unity. Sprite catalog is the folder `tools/sprite-replace/extract.py` wrote from your game files, which fills the Feature list with every sprite feature and its picture. The studio works without it. Blender is where `blender.exe` is, for the other studio windows that open Blender.

## Sharing your work

Models you put into the game with Use in game land in `unity/Assets/Overrides/Features` and `unity/Assets/Overrides/Units`, and those folders are shared through GitHub. To send them to Zach, open GitHub Desktop, make a new branch named after what you made (Branch, then New branch), tick only your model files and their `.meta` files in the list of changes, write a line about them, press Commit, then Push. Tell Zach the branch name.

Unity changes a few of its own settings files when it opens a project, and they show up in GitHub Desktop too. Leave those unticked, and use Discard changes on them if they get in the way.

The Drop folder, the Captures folder and `Overrides/Generated` never leave your computer, so you can try anything there. Nothing from the original game goes into the shared folders, not its pictures, not its models, and nothing painted or traced from them. The original is there to size and place your model against, never to copy from.

## Keeping up to date

In GitHub Desktop, press Fetch origin and then Pull when it offers. Unity picks up the changes when you switch back to it. When a pull brings a new version of the engine, Unity shows a message saying the engine was updated and asks you to restart. Save your work and let it restart. Until then the studio uses the stand-in world.

## When something looks wrong

- The model is huge or tiny. It was probably exported in centimetres, or at the wrong scale. Use the fix button, or set the scale in Blender to 1 unit per metre.
- The model lies on its back or on its face. Export as `.glb` with +Y Up ticked. FBX files from some tools carry a turn the studio can't always undo.
- You see the back of the model in the classic view. Press Half turn.
- The model floats or sinks. Press Centre it on the anchor, or move the origin to the bottom of the model in Blender.
- Parts are missing or look inside out. Check the face normals in Blender (Mesh, then Normals, then Recalculate Outside).
- A part looks plain grey or white. Its picture was not in the file. Export as `.glb` so the pictures travel inside.
- The studio says it runs on the stand-in world when you have the game. Open OpenKingdoms, then Settings, and read the status line. It says what is missing.
- The status line says the Microsoft Visual C++ runtime is missing. The engine needs it and most PCs with games on them already have it. Install it from the link in the message and restart Unity.
