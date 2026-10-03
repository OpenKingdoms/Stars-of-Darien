# Contributing models

Stars of Darien draws the trees, stones, ruins, buildings and units of Total Annihilation: Kingdoms as 3D models, and anyone can make one. This page walks through sending a model in, from your own copy of the project to the moment it is in the game. You don't need to know any code.

1. Fork the project on GitHub and clone your fork.
2. Make the model in Blender or any tool that exports glTF.
3. Check it in Studio Mode and press Use in game, which puts it in the right folder under the right name.
4. Commit it, push it and open a pull request.
5. An automatic check looks at it, and anyone can review it. Once an art reviewer or the maintainer approves it, it can be merged, and merging puts your model in the game for everyone.

## What you need

- A GitHub account, which is free.
- GitHub Desktop from desktop.github.com, unless you already use git.
- Unity Hub with the free Unity Personal license, and Unity 6000.3.25f1, which Hub installs for you.
- Blender, or any tool that exports glTF.

Your own copy of Total Annihilation: Kingdoms helps but isn't required. With it, Studio Mode stands the original beside your model and lets you try the model in a real battle. Without it, you can still make, check and send models.

## Fork and clone

Press Fork on github.com/OpenKingdoms/Stars-of-Darien, clone your fork with GitHub Desktop, and add the clone's `unity` folder in Unity Hub. `docs/STUDIO_MODE.md`, under Setting up, once, covers this click by click.

Before you start a model, make a branch for it in GitHub Desktop (Branch, then New branch) and name it after what you are making, such as `aramon-well`. One branch and one pull request for each model, or for a small set that belongs together, keeps reviews quick.

If you'd like to say what you are working on, so that two people don't build the same thing, open an issue with the Model template first.

## Making the model

### What it replaces and what to call it

Every model replaces one thing from the original game, and the file is named exactly as the game names that thing: `AraTree01.glb` replaces the feature AraTree01, and `ARALODE.glb` replaces the Aramon lodestone's card. `docs/STUDIO.md`, under Drop-in models, says how the game finds a model by its name. Studio Mode lists every feature and unit under Replaces, so you can find the name there, and when you press Use in game it names the file for you.

There are three kinds.

| Kind | What it is | Where Use in game puts it |
| --- | --- | --- |
| Scenery | Trees, rocks, ruins, buildings and the other features on a map | `unity/Assets/Overrides/Features/<feature>.glb` |
| Unit | A whole unit, which can follow the original's animation | `unity/Assets/Overrides/Units/<MODEL>.glb` |
| Unit card | A model that takes the place of one painted card on a unit, such as a lodestone | `unity/Assets/Overrides/Units/<MODEL>.glb` and `<MODEL>.json` |

Models go straight into those two folders, not into folders inside them. A unit's parts follow the original's animation when they are named after its pieces, such as `torso`, `head` or `larm`, which `docs/STUDIO.md` explains under Pieces. Keep your Blender files and other working files outside the `unity` folder, because Unity tries to import anything inside it.

### Scale

One Blender unit is one cell of the map, and a cell is 16 pixels of the original game. Blender's default metric scene works as it is, one metre to a cell.

If you'd rather work in feet and inches, set Scene Properties, Units, Unit System to Imperial and leave Unit Scale at 1.0. Then one pixel of the original is 2.4606 inches and one cell is 39.37 inches.

A monarch stands 4 cells tall, which is 157.5 inches in Imperial. Studio Mode stands one beside your model when the game is installed, and checks your model's height against the original's.

### Origin, ground and facing

The origin is where the game stands the model. Put it in the middle of the model's base, on the ground, at 0, 0, 0. The ground is Blender's floor, where Z is 0.

A model may reach a little way below the ground as a foundation, so that it never looks like it floats where the ground slopes. Up to a quarter of the model's whole height is fine, so a stone 4 cells tall can reach 1 cell below the ground. A model that floats above the ground, or reaches deeper than that, is sent back.

The front faces south, toward the game's camera. In Blender that is the side you see in Front view (numpad 1), the -Y side.

### Triangles and picture sizes

A map can show hundreds of models at once, and lighter models draw faster. Studio Mode suggests these numbers, and the reviewers see them beside your model's own.

| | Scenery and unit cards | Units |
| --- | --- | --- |
| Triangles | about 3,000 | about 6,000 |
| Pictures | PNG or JPEG, up to 1024 pixels on a side | the same |

Going over is fine when the model needs it. The check fails a model only when it has more than 100,000 triangles or a picture larger than 4096 pixels on a side, since that is nearly always a mistake, such as a Subdivision Surface modifier applied at a high level or a photo straight from a camera.

A few shared pictures draw faster than many. `docs/STUDIO.md`, under Materials, says which materials work best and how to make parts glow.

### Pictures

You can paint your own textures, and they ship inside your model. Every picture on your model must be your own work, or something you have the right to share, such as a CC0 texture. Say where each one came from in your pull request.

By sending in a model or a texture, you confirm that it is your own work or that you have the right to share it, and you give OpenKingdoms the right to use it in Stars of Darien. The pull request form has a box to tick for this.

Nothing may come from the original game. Don't use its pictures, don't paint over them, and don't trace them. Look at the original as much as you like to judge size and shape, and then make your own.

### Exporting

In Blender, choose File, then Export, then glTF 2.0. Keep Format at glTF Binary (.glb) and +Y Up ticked, which are the defaults, and leave Compression unticked, because the game can't read compressed files. `docs/STUDIO_MODE.md` covers other tools and formats.

## Checking it in Studio Mode

Open the project in Unity and pick OpenKingdoms, then Studio Mode. Drop your `.glb` onto the Studio View, and pick what it replaces if Studio Mode hasn't picked it from the name.

Work through the Check list until nothing in it is yellow or red. Then press Use in game, which writes the model into the right folder under the right name, and Play here to see it in a battle. `docs/STUDIO_MODE.md` explains every part of the studio.

Take a screenshot while you are there, with the Screenshot button or F9. It goes into your pull request.

## Sending it in

1. In GitHub Desktop, look at the list of changes. Tick your model and its `.meta` file. For a unit card, also tick its `.json` and that file's `.meta`. Leave everything else unticked, including any Unity settings files that changed by themselves.
2. Write a line saying what the model is, press Commit, then Push origin (or Publish branch the first time).
3. Press Create Pull Request. GitHub opens a form in your browser.
4. Fill in the checklist in the form, say where your pictures came from, and drag your screenshot in. Leave Allow edits by maintainers ticked, so a maintainer can fix a small thing for you.
5. Press Create pull request.

By opening a pull request you agree that your work is shared under the project's licence, the GNU General Public License version 3 or later with the additional permission in `LICENSE.unity-exception`. For a model or a texture, you also confirm that it is your own work or that you have the right to share it, and you give OpenKingdoms the right to use it in Stars of Darien.

To change your model after that, export again, press Use in game again, then commit and push to the same branch. The pull request picks up the new version by itself.

## Review

An automatic check called model-check runs on every pull request within a few minutes. It reads each model you changed the way the game does, with no Unity and no game files, and fails when a model:

- isn't a readable glTF binary, or uses compression
- isn't straight in `Overrides/Features` or `Overrides/Units`, or has no `.meta` file beside it
- has more than 100,000 triangles, or a picture that isn't PNG or JPEG or is larger than 4096 pixels on a side
- is far too big or small, which usually means it was exported at the wrong scale
- floats above the ground, or reaches below it by more than a quarter of its height
- holds anything made from the original game's files by the project's own tools

Its report is on the pull request under Checks, with a table of each model's triangles, pictures and size beside Studio Mode's advice. Every new or changed picture is drawn in a texture sheet, `new-textures.png`, which the report links to, so reviewers can see your textures without opening Unity. If the check fails, fix what it says, export, Use in game, commit and push again. You can run the same check yourself with `python scripts/check-models.py` and the path of your model.

Anyone can review a model's pull request and comment on it. The art reviewers, trusted members of the community in the OpenKingdoms art-reviewers team, and the maintainer approve them. They check that:

- it replaces the right thing and is named for it
- it is the right size against the original and the monarch, and sits on its footprint
- it reads well from the game's own camera, at the game's zoom and in its light
- its pictures are your own work or properly licensed, and nothing in it comes from the original game
- a unit's parts follow the original's animation, and a unit card leaves the rest of the unit in place
- the pull request holds only your model and the files that go with it

They may ask for changes in a comment, and you can answer there. An approval from an art reviewer or the maintainer lets the pull request be merged, and merging puts the model and its textures in the game. Everyone who updates the project gets your model the next time they open it.

## Getting help

Ask in your pull request or open an issue. Everyone was new to this once.
