# Putting your own models in

The demo draws every unit as a coloured capsule until a model for its kind exists. Drop a model in the right folder with the right name and the next Play uses it. No code changes.

## Where and what name

Put the file in `unity/Assets/Art/Resources/Units/` and name it after the unit kind, in lower case:

| Kind | File name | What it does in the game |
| --- | --- | --- |
| soldier | `soldier.fbx` (or `soldier.prefab`) | Walks up and fights hand to hand |
| archer | `archer.fbx` (or `archer.prefab`) | Shoots from about six cells away |

The game asks Unity for `Units/soldier` and `Units/archer`, so only the name counts, not the extension. Only one file per name, or Unity picks one of them for you.

Textures and materials can live anywhere under `Assets/Art/`. Keep them out of `Resources/` unless they belong to a model there, since everything in a `Resources` folder ships with the game.

## FBX and glTF

FBX works out of the box. Unity does not read glTF by itself. To use `.glb` or `.gltf`, open Window, then Package Manager, choose Add package by name, and enter `com.unity.cloud.gltfast`. After that a `soldier.glb` works like a `soldier.fbx`. This project has not tried glTF yet, so say so if it misbehaves.

## Scale

One Unity unit is one map cell. A soldier should stand about 1.2 units tall. Units push each other apart when their centres come within 0.7 of a cell, so keep the body inside a circle about 0.7 across or neighbours will overlap. Weapons and capes can stick out a little.

Export in metres. If a model comes in far too big or too small, fix it in the modelling tool, or set Scale Factor in the model's Import Settings in Unity.

## Pivot

The model's origin is where the unit stands: on the ground, centred between the feet. The game puts that point on the unit's position and turns the model around it.

## Facing

The model looks along +Z, which is Unity's forward. To check, select the model in the Scene view, set the tool handle to Local, and look at the blue arrow. It must point where the character looks.

If it points the wrong way, the easiest fix is a prefab. Drag the model into a scene, put it under an empty GameObject, rotate the child until the blue arrow of the parent points where the character looks, and save the parent as `soldier.prefab` in the folder above. Delete the loose FBX from that folder, or move it out of `Resources`, so the prefab is the only `soldier`.

## Team colour

Any material whose name contains `team` (for example `TeamColor`, `soldier_team_cloth`) is tinted blue or red for its side. Every other material keeps its own colours. Paint team parts such as tabards, shields and banners in a light neutral colour with a material named that way. Every unit also stands on a disc in its team colour, so plain models still read at a glance.

The project uses Unity's built-in render pipeline, so the Standard shader is the safe choice for materials.

## Animation

Not yet. The game places and turns the model but does not play clips. The sim already reports whether each unit is idle, moving, attacking or dead, so walk, idle, attack and death clips are the useful set to make. Hooking them up is the next step on the Unity side. A dead unit topples over and sinks for now.

## Names

Everything here belongs to the new world. No names, shapes or textures from Total Annihilation: Kingdoms, and nothing traced from its art.
