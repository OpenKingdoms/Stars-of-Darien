## What this changes

<!-- A line or two. For a model, say what it replaces. -->

## For a model

<!-- docs/CONTRIBUTING-MODELS.md explains each of these. Delete this part for a code change. -->

- [ ] The file is named after what it replaces and sits straight in `unity/Assets/Overrides/Features` or `unity/Assets/Overrides/Units`
- [ ] Its `.meta` file is included, and for a unit card its `.json` and that file's `.meta` too
- [ ] I checked it in Studio Mode, pressed Use in game, and saw it in a battle or on the stage
- [ ] One Blender unit is one map cell, the origin is the middle of the base on the ground, and the front faces south
- [ ] glTF Binary (`.glb`) with Compression unticked
- [ ] At most 3,000 triangles for scenery or a unit card, or 6,000 for a unit
- [ ] Pictures are PNG or JPEG, at most 1024 pixels on a side
- [ ] Every picture is my own work or properly licensed, and nothing in the model comes from the original game

Where the pictures came from:

<!-- "Painted by me", or the source and licence of each texture. -->

A screenshot from Studio Mode or the game:

<!-- Drag it here. -->

## For code

<!-- Delete this part for a model. -->

- [ ] `scripts/csharp-check.sh` or the Unity tests pass
- [ ] A change in behaviour comes with a test that fails without it
- [ ] Nothing from the original game: no data files, nothing extracted from them, no code decompiled or disassembled from it
- [ ] Comments are brief

By opening this pull request I agree that my work is shared under the project's licence, the GNU GPL version 3 or later with the additional permission in `LICENSE.unity-exception`.
