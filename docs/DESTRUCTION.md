# Destruction

How Stars of Darien makes battles wreck the land: explosions that look real in 3D, scenery that breaks, burns and falls, and a field that keeps its scars. Gunpowder, siege engines, fire and each kind of magic should leave their own marks. Defilade (RBGames, announced 2026-09-23) is the reference for ambition, a battlefield where nearly everything can be destroyed and the wreckage matters.

The work has three layers. The first is the original's own rules for destroying scenery, which the engine does not have yet. The second is the look, drawn by Stars of Darien alone and safe in a room shared with openkingdoms.net. The third is optional remastered rules, chosen per room and run by both clients.

## What the original does

The player's files hold 1,379 feature definitions in 412 files, base game and Iron Plague together, as the engine mounts them. Leaving out bodies (150 wrecks, 116 stone and 73 frozen) and the stages other features turn into, there are 641 kinds of placed scenery. 301 of them can be destroyed and 340 cannot.

Every placed tree (46 kinds), wall (66), building (63), plant (25), fence (12), hut (11), tower (4) and well (4) can be destroyed, and so can Aramon's crops, carts, fences, fountains and ivy (37), the Taros devices (9), 17 Creon carts, sheds, fences and inventions, 4 ruins and 3 grasses. Every rock (41 kinds) is indestructible, as are the 91 lodestone kinds, waves, sound emitters, spires, mounds and most ruins and grasses. Bodies can all be destroyed.

Hit points are the def's `damage`. Trees have 200 to 1,000, plants 200 to 400, huts 650 to 900, buildings 1,400 to 10,000, walls 2,000 to 24,000 and towers 8,000 to 10,000. A Cannoneer's ball deals 2,000.

Each stage names what it leaves when destroyed (`featuredead`) and, for flammable kinds, what fire leaves (`featureburnt`). A tree becomes a dead tree, which still blocks, and then a smudge on the ground (CreTree01, CreTree01a, CreTreesmudge01). A wall loses half its height and then lies as rubble that no longer blocks (AraWall01 at 12,000, AraWall01a at 9,000, AraWall01b). A building becomes a ruin and then a shell that never stops blocking (Arabuild01, 01a, 01b). Fences and crops fall once, to wrecks that stop blocking, and huts to low wrecks that still block. Of the 269 last stages, 97 still block, every building's shell among them. 261 destructible kinds have a death animation (`seqnamedie`) that plays before the next stage replaces them, a tree falling or a house caving in.

Blasts are what destroy scenery. A weapon's blast runs one pass over the cells within its radius, which is half its `areaofeffect` (legacy:245089). Units in it take damage with falloff. Each feature within the radius, or in the blast's own cell, takes the weapon's full damage once (legacy:245236-245302). A `unitsonly` weapon skips scenery (legacy:245240). A shot with an `areaofeffect` under 17 that hits a unit is a direct hit and does not touch scenery (legacy:245029-245033), but the same shot landing on the ground, or on a feature in its path, does. A unit's own death blast is a blast like any other (legacy:245709-245739).

Damage adds up across blasts. When it reaches the def's `damage` the feature dies (legacy:128781-128789). Indestructible kinds ignore hits (legacy:128756). A destroyed feature plays its death animation, then the cell takes its `featuredead` (legacy:127838-127955).

Fire is the original's too. A `firestarter` weapon whose blast reaches a `flamable` feature sets it burning instead of damaging it (legacy:128781, 128795). 162 of the destructible kinds burn, among them 37 of the 46 tree kinds, and 32 weapons start fires. A burning feature plays its burn art with flames in front and behind (`seqnameburn`, `seqnamefrontflame`, `seqnamebackflame`). Once, about 2.5 to 5 seconds after it catches (`sparktime`, legacy:127801), it throws sparks. Each flammable feature within 3 cells catches with `spreadchance` percent, 40 for most kinds, and so does one cell at each of five steps downwind (legacy:128021-128088). When the burn art ends the feature becomes its `featureburnt` (legacy:128103-128116, 128583). The wind is simulated: a speed between the map's `minwindspeed` and `maxwindspeed` (legacy:168998-169000), redrawn from time to time with the direction turning up to 45 degrees (legacy:241670-241700). Burning scenery hurts nobody. The data names `burnweapon = TreeBurn` as a key, but the loader only reads a `[BurnWeapon]` section (legacy:127389-127401) and no feature has one.

Units never crush scenery, as the original has no crush key at all. Sweeping a feature sinks it with no remains (legacy:127958-128019). In multiplayer the host decides every hit on scenery and broadcasts each death and fire (legacy:128759-128776, 128815-128822).

Unit deaths drop pieces. 128 of the 204 unit scripts call EXPLODE, over 1,100 times, nearly all with FALL, so pieces drop off a unit as it dies. Units then leave their 3D wrecks as features.

## What OpenKingdoms and Stars of Darien do today

No scenery is ever destroyed. The engine reads `damage`, `indestructible` and `featuredead` into each FeatureDef, but nothing applies a hit to a feature. A placed feature has no hit points, `featuredead` is never resolved, and there is no fire, `flamable`, `firestarter` or wind. Features leave only when swept, rotted or raised, or when another is placed over them. A shot stops at a tall feature in its path, and the feature takes nothing. So the forests, walls and towns of the original's maps cannot be burned or knocked down in either client. Restoring that is a parity fix, not a new rule.

Two smaller gaps sit beside it. The engine takes `areaofeffect` as the splash radius with a straight falloff, while the original uses half of it with a curved falloff (legacy:245089, 245213-245217). That is worth its own issue. And the engine marks a piece EXPLODE names but drops its flags (`src/render/cob_vm.c`), so nothing falls off a dying unit.

The engine tells Stars of Darien little about a hit. `okx_effects` gives each blast's picture strip, place, frame, pace and glow size, and `okx_projectiles` gives shots in flight with their velocity. Nothing names the weapon, its radius, damage, damage kind or flags, and nothing says a feature died. `EngineFx` infers what it can from the art, such as breath from the flames at the muzzle. `okx_features` is the whole list each frame, by index, and a removal closes the gap, so `EntityRenderer` rebuilds every entry after it. A destroyed feature would pop, and on a map with thousands of features each death would rebuild most of them.

Explosions are drawn as the original draws them and then remastered: its frames eased and enlarged, added art glowing into the bloom, soft edges on the ground, a pool of 4 point lights, trails, shot shadows, and a scorch under fire blasts that fades after 14 seconds, at most 48 at once. There are no craters, dust rings, debris, 3D smoke or lasting marks.

The pipeline is Unity 6000.3.25f1 with URP 17.3 on the Forward path: 4 lights per object, no shadows from added lights, MSAA 4x, HDR with bloom, SSAO and a 4096 shadow map. The Particle System module is in, the VFX Graph package is not, and the renderer has no decal feature. Burst, Collections and Mathematics come with URP. Models are drawn instanced through `InstancedDraws`. The ground is our own mesh, regions of 16 by 16 blocks with a detailed and a coarse level, painted from one picture per region with detail and rock tiling in `OkuTerrain`. `Atmosphere` blows rain and snow along one fixed wind. The last perf probe (84c3f19) brought the worst entity frame of a five-kingdom battle to 26 ms, and there is no measurement of a battle heavy with effects yet.

The player's files supply 31 explosion classes (`explosions.tdf`): cannon blasts in four sizes, `explodeb` for fireballs, dust puffs and dirt clods, lightning, splashes, blood, blue and green shock rings, ice, tsunami, a volcano blast, a flame strike and the Soul Stealer. They also hold flame art for burning scenery, death animations for 261 kinds and burn animations for 166, smudges and every unit's wreck. Stars of Darien draws scenery as 3D models, and 51 dead and burnt stages, mostly Creon buildings, carts and trees, have no hand-built model yet.

## Weapons by kind

194 weapons, by what they are. Radii here are `areaofeffect` as the data gives it.

| Kind | Who | What reaches scenery | Blast art |
|---|---|---|---|
| Gunpowder | Aramon Cannoneer, Grenadier, War Galley, Stronghold. Veruna Musketeer, Mortar, Man of War, Bastion, Trebuchet Ship. Taros Sea Shadow, Ghost Ship, Iron Beak. Creon Iron Clad, Sage's mortar, Submersible, Tortoise, Bomb Sprinkler, Stern Wheeler, Aerial Juggernaut, Beast Rider | `damagetype = explosion`, area 0 to 150, damage 311 to 2,070 | teeny to large explosion, a blue shock ring for the Ghost Cannon |
| Siege | Aramon Catapult, Trebuchet, Rolling Tower. Veruna Catapult, Ballista, harpoons, the Dirigible's dropped shot. Zhon Stone Giant | area 20 to 100, damage 1,070 to 2,001, the giant's and Elsin's Meteor are thrown rock models | small to large dust puff and dirt clods |
| Arrows and spears | archers, towers, ships' bows, Crossbowman, Centaur, Gatling Crossbow, Hunter, Gryphon, Amazon Knight, Mage Archer | mostly area 0, so only when they land on scenery or miss. The Skeleton Archer's Flame Arrow starts fires | none, or a small dust puff |
| Fire | Taros Fire Mage, Fire Demon, Black Dragon, Lokken, Heligrin, Rictus, Caged Demon, Fire Spout, Sky Knight, Fallen Angel, Darkest Priest's Fire Bomb. Every dragon's breath. Aramon Gold Dragon and the Grenadier's Incendiary. Creon Fire Wagon, Neo Dragon and Sage's Blue Flame | 29 of the 32 fire starters (two ball lightnings and the Blade Demon's sword are the rest). Breath runs along a line, fireballs reach 10 to 150, Fire Storm and Wrath rain meteors over 200 and 600, Ring of Fire 420 | `explodeb`, flamestrike, Volcblast, flames |
| Lightning | Zhon Thirsha, Shaman, Wisp, Death Totem, Ancient Dragon. Taros Darkest Priest, Mage Tower, Weather Witch. Aramon Elsin. Creon tasers and the Prismatic Mirror | instant bolts with area 0, ball lightning 50 to 60 (the Taros ball starts fires), tasers 40, Shockring 230 | lightning, blue shock ring |
| Water | Veruna Priest of Lihr, Kirenna, Sea Dragon, Angel of Lihr. The Sea Serpent and Kraken | water balls, Tsunami 180, Trident 110, Water Vortex 180 | water splash, tsunami |
| Frost | Aramon Acolyte's Hail Shower, Taros Weather Witch's Ice Storm, the Ghost of Garacaius's Ice Storms, Creon freeze beams and the Neo Dragon | hail rains ice balls over 200 to 300. Freezing turns units into frozen bodies | `iceballexp` |
| Earth | Earthquake of the Gold Dragon, Avatar of Anu, Acolyte and Ghost of Garacaius. Elsin's Earthen Wave | spells over 200 to 500 with the only screen shake in the data. Earthen Wave is units only | rings |
| Wind | Taros Weather Witch's Tornado, Zhon Wrath of Tammuz's Hurricane, Thirsha's Wind Wave | wandering vortices of 30 to 240. Wind Wave is units only | none |
| Dark | Taros Lich's Death Aura, Spawn of Belial's claws and Fire Vortex, the Fallen Angel's Death Sword. Mind control and Turn to Stone | Death Aura and mind control are units only | Soul Stealer, shock rings |
| Melee | 48 swords, axes, claws and bites | the engine resolves them on their target alone | none |

There is no poison in the data, and holy magic has no damage kind of its own. The divine casters are Aramon's Acolyte and Avatar of Anu and Veruna's Priest and Angel of Lihr. Twelve spells are `unitsonly` and never touch scenery in the original: Earthen Wave, both Fire Waves, Water Blast, both Wind Waves, Energy Blast, Death Aura, Freezing Storm and three mind controls.

By side, Aramon is gunpowder, siege, earth and holy light. Taros is fire, dark and lightning. Veruna is water, gunpowder and holy light. Zhon is lightning, wind, beasts and thrown stone. Creon is steam cannon, electricity, frost and blue flame.

Whether every breath, rain and wandering spell reaches scenery through the same blast pass is still to be confirmed while porting, and so is whether the original's melee swings ever do. The engine resolves a swing on its target alone.

## Layer 0: the original's rules, in the engine

The engine gets the original's scenery destruction: hit points on each placed feature, the blast pass over features with its radius and full damage, `unitsonly` and `firestarter`, the death animation's length before the dead stage, fire with its sparks, spread chance and wind, burnt stages, shots stopped by scenery damaging it, and death blasts reaching scenery. The dead stage replaces the feature in place, as the original keeps its cell, so the list does not shift. Hit points, fire and wind go into the state hash and saves.

It ships in one engine release to both clients, the way every simulation change does, so mixed rooms stay in step through the matching build id they already need. The classic view, and so openkingdoms.net, draws the death and burn animations and the flames from the player's files. Everything after this builds on it, because with no scenery dying there is nothing for the look to show.

## Layer 1: the look, in Stars of Darien

Nothing here feeds back into the engine, so it is safe in a mixed room. It reads new exports that only report (see below) and the simulation stays as it is.

### Explosions

Every blast keeps the original's picture at its core, so it stays recognisable, and gains 3D around it. A flash with a real light, a fireball or body, a shock ring and a dust ring along the ground, sparks, embers and debris thrown with simple physics, smoke that rises and drifts with the wind, and a scar left on the ground. Each kind tunes these.

Gunpowder gives a white flash that lights the ground to twice the radius, a fast dust ring, a fountain of dirt clods, splinters where wood is near, a dark smoke column drifting for 15 to 20 seconds and a crater ringed with thrown soil. Siege stone has no flash or fire. The boulder lands as a 3D rock that bounces and stays, with a brown dust burst, stone chips and a pit, and stone it hits breaks into rubble. Arrows raise a puff of dust and stay stuck in the ground or a trunk for a while. Fire rolls out as a fireball with an orange light and embers drifting downwind, and leaves a scorch that glows and then darkens. A breath scorches a swath along its line. Lightning lights the field blue-white for two frames, branches, throws sparks and leaves a charred star, and a tree it kills splits down the trunk. Water throws a column of spray and leaves a dark wet patch that dries over a minute, and a tsunami leaves a long wet swath. Frost throws ice shards and cold mist and leaves frost on the ground that melts over 30 to 60 seconds, with cracks in stone. Earth sends a ripple through the ground mesh, opens fissures, tosses rocks, sways the scenery and shakes the camera by the weapon's `shakemagnitude`. Wind spins dust and leaves, picks up loose debris and bends trees. Dark magic leaves purple-black smoke and a blight that withers grass grey and darkens trees without killing them. Holy light rises as a golden pillar and leaves a fading ring of light and no scorch.

### Scenery that breaks

Each model kind on the map, and the kinds its chain leads to, is split once into chunks by plane cuts seeded from the kind's name, so it splits the same way every time. A model built of many small parts, such as a palisade of logs, breaks along its parts into groups instead. Chunks are not capped. They draw both faces, with the inside shaded as the material's interior, charred wood or raw stone, which reads well while they fly and hides under dust once they land. A tree gets 6 to 10 chunks, a wall or hut 12 to 24 and a building 16 to 40. Splitting runs while the battle loads, inside the scenery's slice of about 25 ms a frame, and the result is kept per kind. A kind first needed mid-battle splits at most 2 ms a frame and falls whole until it is ready.

The simulation's swap is the clock. The look starts when the feature is dying and the dead stage fades in under the dust at the tick the engine swaps it. A tree hit by a blast loses its crown, which bursts into leaves and a few branches thrown away from the blow, and its trunk stands as the dead tree. When the dead tree dies it snaps at the base, topples away from the blow in about a second, breaks on landing and leaves its stump. A wall crumbles from the top, chunks falling mostly down with dust at its foot, then collapses to rubble. A building's roof caves in, its walls fall outward and a dust cloud rolls out, with a smoke column if it burned. Fences, crops and huts break apart and scatter. Stone bodies shatter into stone, frozen ones into ice and wrecks into scrap. Rocks stay whole in the original, so a blast only chips them.

Chunks are a pool, simulated with gravity, spin, bounce and friction against the drawn ground and drawn instanced. No Unity rigid bodies, and a Burst job once there are more than a few hundred. Settled chunks stay as rubble up to a cap, and the oldest sink into the ground.

#### As built

`Destruction/FractureCache.cs` splits each breakable kind on the field, and every stage it can become, while the battle loads in the scenery's slices, and a kind first seen in battle for at most 2 ms a frame. Every step of a split is bounded, at most a batch of 6,000 triangles, and a kind's chunks share a mesh for each 40,000 vertices. A model built of many small parts breaks along them, grouped by where they lie. A face seen from inside a chunk is lit as one flat face turned to the eye, in the interior's colour mixed with the face's own, so the cut reads as solid.

`Destruction/FeatureFalls.cs` starts each break from the feature's dying event. The blow comes from the blast toward the feature, or along the shot when it burst on the feature. A tree whose dead stage still stands loses its crown and keeps its trunk, and a dead tree snaps at its foot and swings down as a rod, breaking into its chunks where it meets the ground. A wall's chunks above its next stage fall from the top down through the first half of its death, and into rubble it all comes down. A hut or building drops its roof in and tips its walls out over what stands of its wreck. A body shatters, other kinds scatter, a hit that kills nothing chips, and every break throws grit. A wall, hut or building that comes down leaves a scuff of churned soil on the scar map. Chunks the next stage keeps wait in place, and at the swap they dither away as the stage dithers in, each filling the other's holes. A break off screen shows nothing, and one more than 140 units from the camera breaks into its parts whole.

`Destruction/Debris.cs` keeps the chunks in a pool sized by the Battle effects setting. They fly with gravity, spin and drag, bounce and slide on the dented ground `ScarMap` draws, tip onto a side and settle, lie for 3, 6, 12 or 24 seconds by the setting, grit for a third of that, and sink. A dying unit's EXPLODE pieces use the same pool through `Destruction/PieceFalls.cs`, and the unit stops drawing them. `Destruction/DustPuffs.cs` makes its dust, smoke and spray in the explosions' shared particles, which light, sort, cap and blow it with the rest.

Measured in the editor, splitting all 611 breakable shapes among the 793 feature models, 5.1 million triangles as drawn, took 13.4 s, about 22 ms a kind, in 20 ms slices with no step over 16 ms. A thousand chunks step and draw in 0.63 ms a frame with nothing allocated. In a stress battle on the mock at High, 170 pieces of scenery under cannon fire all the while and two armies routed, breaking took 1.65 ms of main thread a frame on average and 2.6 ms at the 95th percentile, with up to 1,481 chunks flying and 3,982 in play, and drawing them added 3.6 ms to a frame at 2560 by 1440. Taking a feature away no longer rebuilds the ones after it. On Ulasem Arena's 1,452 features in the engine that frame costs what a still one does, where building them all again took 5.6 ms against 3.5.

### Stages with models of their own

Every stage scenery breaks or burns into now has a model of its own. The last 51, all Creon, were built by hand in Blender with the kits in `tools/sprite-replace/hand/creon_stages`, a file for each group: the palace, great hall and senate (`g1.py`), the institute, coliseum and observatory (`g2.py`), the embassy and the two slate-roofed buildings (`g3.py`), the houses (`g4.py`), the dead trees and the smudges they leave (`g5.py`), and the inventions, fences, plants, cart, shed and well (`g6.py`). A ruin is its intact building broken in the intact's frame, so the walls that still stand sit where the intact's do, and a shell is the ruin taken further. What a stage's picture shows gone is cut away along the classic camera's line of sight, and rubble is heaped where the picture draws it and no steeper than it could lie. Every texture is made from noise and numbers, so the models ship as geometry. `coverage.py` names any stage still without a model, and the game draws such a stage from the nearest earlier stage's chunks.

### Deaths

A FALL piece drops off a dying unit from its pose, bounces and lies on the ground for a while. A building collapses with the same chunks before its wreck appears.

### Scars and craters

The ground keeps a scar map, textures laid over the whole map at one texel for every 4 pixels: scorch, churned earth, frost, blight and wet, plus a height change. `OkuTerrain` draws the colours, lights the change through its normals and moves the ground mesh by it, so a crater is a real dip. Craters are shallow, up to 6 pixels for a cannon and 10 for the largest spells, and overlapping ones take the deeper rather than adding up. Anything standing on the ground is drawn lowered by the same change, so units sit in craters instead of floating. The engine's heights do not change, so movement and sight are as before. Scars last the whole battle. Scorch fades to dark soil, and frost and wet dry away. Stamping is capped per frame, and the scar map costs a fixed amount of memory however many blasts land.

#### As built

`Scars/ScarMap.cs` keeps three textures over the map, a byte a channel at the setting's texel size. Marks hold char, thrown soil, blight and stone. Shape holds the dip, the rim and cracks. Fade holds the seconds left of frost, wet, holy light and heat, and loses a step every half second of the battle, so a paused game holds them. Each blast the backend reports becomes a stamp, and `ScarMap.Mark` lets a look leave one of its own, such as char under a burning tree. Stamps wait in a queue of 16 frames' worth and are drawn on the GPU, 24 a frame on Low, 48 on Medium, 96 on High and 160 on Ultra. Past the cap a stamp folds into a waiting one of its kind that it overlaps, or the oldest gives way. The dips are also kept on the CPU, texel for texel as the GPU filters them, so `ScarMap.GroundOffset(x, z)` and `ScarMap.Sink(position)` answer what the ground shows, and units, features and corpses are drawn on the dented ground. On High and Ultra a region the first crater reaches is rebuilt on a worker with a vertex every half unit, a quarter on Ultra, and `OkuTerrain` moves those vertices by the dip in its lit, shadow and depth passes alike. Medium lights the scars through their normals without moving the ground, and Low draws their colour only and lets them fade over about two minutes.

| Kind | Weapons | What it leaves |
|---|---|---|
| Gunpowder | cannon, mortar, musket, bomb and every death blast | a crater with 0.8 of the blast's radius, at most 4 units, 2 to 6 px deep with a flat floor, a lumpy rim 0.4 of its depth, scorch at its heart, walls of dug earth and earth thrown in rays to 2.4 crater radii. A shot of no area scorches a spot |
| Siege | thrown stone, catapult and trebuchet shot | a pit 0.45 of the radius, at most 5 px deep with a low rim, churned soil and stones scattered to 2.8 pit radii, and no scorch |
| Impact | volcanic blasts, meteors and fire spells of 3.5 units or more | a crater up to 5 units in radius and 10 px deep, charred, glowing for 25 s |
| Fire | fireballs, flame strikes and burning arrows | char over the blast's reach with a ragged edge, black and glowing while fresh, settling to dark soil after 20 s |
| Breath | dragon breath | a scorched swath 3.5 units back along the breath |
| Lightning | bolts, ball lightning and shock rings | a charred star with a scorched fork at least 6 units across, five branches each with a side branch |
| Frost | hail, ice storms and freezing | rime that lasts 60 s at its heart and melts from its edges, and cracks that stay |
| Dark | death auras, mind control and turning to stone | blight that withers what grows grey and violet, most on green ground |
| Water | water balls, splashes and tsunamis on land | wet dark ground that dries from its edges over a minute, a long swath for a tsunami |
| Holy | a divine caster's magic that is no element | a pale ring of light that fades over 25 s, its middle first |
| Earth | earthquakes | cracks and churned soil along four fissures |
| Dust | dust puffs and whirlwinds | a scuff of churned soil |

The kinds are the ones `FxKinds` sorts every weapon into for the explosions, so a weapon's scar and its blast agree. Blasts on water leave nothing here, as the sea's churn belongs to the explosions, and so does a shot that struck a unit directly. A crater by the shore fills to just above the water. The soil follows the map's climate, with brown earth on grass, sand in the desert, slush on snow, black mud holding water in swamps and ash on volcanic ground. Dug snow shows wherever the ground itself is snowy. The fog of war darkens scars like the rest of the ground.

Measured on the mock in the editor at High, the scar map's own update took 0.2 ms a frame on average in an eight seat battle on a 512 unit map with two stamps a frame landing all over it, and 3 to 7 ms in the worst of 900 frames over three runs. Drawing a scarred field at 2560 by 1440 took 0.13 to 0.18 ms more than the same field with the scars switched off. Five thousand blasts landing at once filled the queue to its cap of 1,536, folded 2,950 into waiting stamps, let 514 go and drained in 53 frames of at most 1.8 ms. The textures hold 8 MB on a 192 unit map and 56 MB on a 512 unit one.

### Fire

A burning feature gets flames, embers, a smoke column drifting downwind and a flickering light from the shared pool. Char spreads under it while it burns, and its burnt stage is drawn darker. The wind comes from the engine once Layer 0 simulates it, and the rain, snow and smoke all follow it.

#### As built

`Fx/FxFire.cs` follows the engine's feature events. When a feature catches, flames rise from its model, sized to what is drawn. They catch low on the side the fire came from and climb over it in the first third of the burn, more in a tree's crown than on its trunk, and each flame is a hot puff in the shared particles that cools into smoke at its tip, so the flames lean with the wind. A soft glow sits round the crown, embers drift off downwind, and the fire throws a shower of sparks once, 2.5 to 5 seconds after it caught, as the original's does. A feature that catches from a neighbour, with no blast behind it, gets sparks flying across from the burning fire most likely to have thrown them, the one nearest and upwind. The model itself chars in patches as it burns, with embers glowing in the cracks of the char, and a tree's leaves burn away. At the burnt stage the charred model dithers away over 1.2 seconds while its burnt stage dithers in, and what is left keeps its char, its embers dying over 20 seconds while it smoulders. The ground under it takes three char marks through `ScarMap.Mark`, as it takes hold, halfway through and as it burns out.

A forest burns by patch. Fires within the same 6 unit square share one smoke column, which bends downwind as the wind carries it, and one flickering light from the effects' pool, so a burning forest asks for a light a patch rather than one a tree. Fires keep to 45 percent of the setting's particles: when they ask for more, each makes fewer flames and each flame grows a little to keep the crown covered. A fire off screen or out of sight makes nothing but its char, and a far one makes a quarter as much.

Weather is drawn only. In rain the flames live 55 percent of their dry life and are smaller, steam hisses off them, and the smoke turns white with steam and thickens. Snow shortens them a little and lightens the smoke. `Scars/ScarSnow.cs` keeps the battle second each patch of ground was last scarred, and while it snows the terrain whitens over a scar two minutes after it was made or the snow began, so a fresh crater shows dark through the snow and an old one is buried. The rain, snow and drifting fog in `Atmosphere.cs` blow with the battle's wind, the same wind that carries the smoke, flames, embers and dust.

### Magic on the land

Each kind of magic leaves its own mark on the scenery it reaches, past its blast and its scar on the ground. The marks live in `Fx/SceneryLook.cs`, one per feature, kept through its stages, and the model shader reads them per instance, so marked scenery stays in its instanced batch. `Fx/FxMagic.cs` reads the blasts and finds what each reaches through a grid of the features.

| Kind | What it leaves on scenery |
|---|---|
| Frost | rime on every surface facing up, white and glinting, on trees and rocks alike, for 8 seconds and then melting until it is gone at a minute, with cold mist round each foot. A burning tree only steams |
| Dark | what grows withers over 2.5 seconds, its leaves greying, darkening and turning faintly violet, a tree losing a third of its leaves to falling grey leaves. Stone and walls darken a little. It lasts the battle |
| Lightning | the tree it strikes splits down its trunk, the halves leaning apart with a slit between them, charred and glowing, with sparks, charred splinters and smoke curling from the split for 10 seconds. If the bolt kills it, it breaks as any tree does and its dead trunk stands split. Anything else it strikes is scorched |
| Holy | a golden sheen, brightest at the edges, gone in about 6 seconds, golden motes rising, and what dark magic withered heals by more than half. A divine caster's other spells leave half a sheen |
| Earth | trees sway hard and settle in about 4 seconds, dropping leaves, buildings and walls shudder, rubble and low wrecks throw stones and dust, and the chunks lying from earlier breaks are thrown up again |
| Water | what it reaches is wet, darker and glossy, drying over a minute, with drops falling from it. Fires in its reach go out in a burst of steam and steam as they smoulder |
| Wind | trees bend round the vortex, swaying back over a few seconds, and lose up to 60 percent of their leaves, torn off green. Fires in its reach flare and throw embers and sparks downwind |
| Fire | what does not burn is scorched and glows a moment, and frost and wet steam off it. What burns is the fire's own |

Camera shake from `shakemagnitude` came with the explosions in `Fx/FxBlast.cs`, as the earthquakes are the only weapons the data gives a shake.

Measured on the mock in the editor at High: a forest of 336 trees where up to 94 burned at once in 37 patches, and 234 caught over the run, while frost, dark, lightning, holy, fire and water spells, the Earthen Wave and a tornado landed eight at a time every half second and marked 1,597 pieces of scenery. Over four runs fire and magic took 0.31 to 0.34 ms of main thread a frame at the median and at most 0.54 ms at the 95th percentile. Every part of the effects together took 1.35 to 1.55 ms with them and 0.65 to 0.73 ms without, the difference being mostly the particles they add. The particles stayed under their cap of 5,000, at 4,971 at most, and the lights at 20 of 24. On the graphics card their cost was below the noise of the measure: a 2560 by 1440 frame drawn and waited for took between 0.55 ms less and 0.87 ms more with them in the runs on a quiet machine, and the opaque and transparent passes the card reported moved by less than a quarter of a millisecond.

## What the engine adds for the look

New exports that report and change nothing. Each is kept in a ring in the embedding layer, outside the simulation and its hash, the way the battle record is kept. They are new functions with no change to existing structs, so `OKX_API_VERSION` stays at 23.

| Export | What it gives |
|---|---|
| `okx_blasts(since, out, cap)` | each blast since an id: its tick, the firing def and weapon slot or the feature or death behind it, player, place, direction of travel, radius in pixels, damage, flags (fire starter, units only, water, direct hit), the unit and the feature struck |
| `okx_weapon_info(def, slot, out)` | a weapon's name, type, subtype, damage kind, explosion class, `areaofeffect`, flags, glow size and shake, so Stars of Darien sorts weapons into kinds with a table it can test |
| `okx_feature_events(since, out, cap)` | hit, dying with the death animation's length, dead with the old and new def, caught fire with the burn's length, burnt, swept and placed, each with the feature, its place and the blast that did it |
| `okx_piece_events(since, out, cap)` | a unit's piece exploded, how, and its pose at that moment |
| `okx_wind(speed, heading)` | the simulation's wind, once Layer 0 has it |

## Layer 2: remastered battlefield rules

Rules beyond the original, chosen per room. Each lives in the engine behind a bit in the battle's config, the way Crusades balance does: carried by CREATE_ROOM, the room snapshot and START_GAME, saved, written in the replay header and part of the state hash. openkingdoms.net's lobby gets the same checkbox and the relay passes the bits. Both clients then run the same rules, and a room with them on plays in either. The default is off, so the original's game stays the default, and a "Remastered battlefield" preset turns on the set the owner picks.

| Rule | Cost | Effect |
|---|---|---|
| More can be destroyed: rocks, ruins, spires and grass, but not lodestones, sacred sites, waves or sound emitters | small, a table of hit points and dead stages per kind | rocks shatter into rubble and open paths, maps play differently |
| The twelve sweeping spells reach scenery | small | a monarch's Wind Wave flattens a forest, a big swing in late games |
| Fire hurts units in it, using the TreeBurn the data names | small to medium | forests become traps, the computer must learn to avoid them |
| Grass fire that spreads over grass ground as well as scenery | medium, a fire grid in the simulation | whole fields burn, a new tactic for Taros |
| Rubble that blocks: walls, buildings and 3D walls leave rubble that blocks until swept, which pays mana | small | breaches have to be cleared, sweeping matters |
| Craters that slow movement | medium, a movement cost in pathing and its cache | artillery shapes the ground |
| Cover, as in Defilade: shots passing a wall, rubble or building can be stopped by it | large, new hit rules and computer behaviour | infantry hides behind ruins |
| Ground that really deforms | very large, heights feed movement, slopes, water depth, sight and building | not now |

Chunks and debris stay in the look. Simulating them would need identical floating point on every machine and is not worth it.

## Performance and quality

On the RTX 3070 at 1440p, in the perf probe's five-kingdom battle, destruction may add at most 2 ms of main thread time and 2.5 ms of GPU time at High. Splitting takes at most 2 ms a frame in battle. Blasts off screen leave scars and swaps but no particles or chunks, and far ones get fewer.

A new Battle effects option sets the budgets, picked automatically from the graphics card and the measured frame time the first time the game runs.

| Setting | Flying chunks | Rubble kept | Lights | Scar map | Ground |
|---|---|---|---|---|---|
| Low | 150 | 300 | 4 | 1 texel per 16 px | colour only, scars fade |
| Medium | 600 | 1,500 | 8 | 1 per 8 px | colour and normals |
| High | 1,500 | 4,000 | 24 | 1 per 4 px | dips in the mesh |
| Ultra | 3,000 | 8,000 | 48 | 1 per 4 px | dips, longer smoke |

More than 4 lights needs URP's Forward+ path. Every Oku shader has to keep its variants for it, and the built player's smoke run with the GPU has to pass, since variants stripped from a player build once left Alpha 1 drawing nothing. Particles stay in the existing batched effect meshes with Burst, and the VFX Graph package waits until counts pass about 20,000.

## Workstreams

Five streams, each with its own files. The one shared surface, the backend contract, is written first so the rest can build against the mock while the engine work lands.

A. Engine and contract. First, in Stars of Darien only, the contract: `IGameBackend` gains blasts, feature events, piece events, weapon info and wind, and `MockBackend` emits them from its staged fights. Then the reporting exports (`ok_embed.h`, `ok_embed.c`, a recorder called from the detonation code in `units.c` and from EXPLODE in `cob_vm.c`) on unity-embed with `test_embed`. Then Layer 0 on main through `land-pr.sh`: `features.c`, `tak_features.h`, `tak_world.h`, `units.c`, wind in the world, `sim_hash.c`, `savegame.c`, replays, the pathing cache, the classic view's death and burn animations, and the parity notes and tests. Then feature events and wind. It owns `OkEngine.cs`, `EngineBackend.cs`, `IGameBackend.cs` and `MockBackend.cs`.

B. Explosions and light. New `Fx/FxBlast.cs` (the kinds table and each blast's parts), `Fx/FxParticles.cs` (pooled sparks, embers, dirt and smoke for every stream to use), `Fx/FxShock.cs`, `Fx/FxQuality.cs` (the option and its budgets, read by all), changes to `EffectRenderer.cs` and `FxLights.cs`, `OkuEffect.shader`, a new smoke shader, Forward+ in `OkuRenderer.asset` and the option in `GameOptions.cs`. It owns `EffectRenderer.cs`.

C. Scenery that breaks. New `Destruction/Fracture.cs` (the seeded splitter, pure and testable), `FractureCache.cs` (split while loading, within budget), `Debris.cs` and `DebrisDraw.cs` (the chunk pool and its drawing), `FeatureFalls.cs` (trees, walls, buildings and bodies), `PieceFalls.cs` (death pieces), the interior shading in `OkuModel.shader`, and in `EntityRenderer.cs` holding a dying feature until its swap and matching features by what they are, so one removal does not rebuild the rest. It owns `EntityRenderer.cs` and lists the dead stages that still need hand-built models.

D. Scars and craters. New `Scars/ScarMap.cs` (the textures, stamping and caps), `ScarStamps.cs` (crater, scorch, frost, blight, wet and fissure shapes), `GroundOffset.cs` (the height change for anything standing on the ground), `OkuTerrain.shader`, small hooks in `TerrainView.cs` and `TerrainBuilder.cs`, and the scorch in `FxDecals.cs` moved over to the scar map. C and B call `GroundOffset` through one small hook each.

E. Fire, magic and weather. New `Fx/FxFire.cs` (burning scenery, char and burnt stages), `Fx/FxMagic.cs` (lightning splits, frost, blight, wet, holy light, earth ripples and wind vortices, built from B's particles and D's stamps), the engine's wind in `Atmosphere.cs`, and camera shake from `shakemagnitude`.

The order: the contract first, a day's work. Then the engine exports, B, C and D in parallel on the mock. Then Layer 0 on main while they integrate the real exports. E starts once B's particles and D's stamps have landed, and finishes on the engine's fire events. Shared files have one owner each, and the others change them only through the hooks above, rebasing often.

Each stream comes with failing-first tests. EditMode covers the kinds table against every weapon, the splitter's determinism and chunk counts, the caps and the scar stamps. PlayMode on the mock covers a blast's parts, a destroyed feature throwing chunks and settling to its dead stage, units sitting in craters and fire spreading its look. Real-engine checks, skipped without the game files, cover a tree, a wall and a forest fire. The engine adds simulation tests for hit points, dead stages, fire, spread and wind, and a save round trip.

## Milestones

Each milestone ends with captures for the owner, frame strips and contact sheets rendered offscreen.

1. Blasts on the mock: a cannon volley, a mortar, a fireball and a lightning strike, today's look beside the new one.
2. Scenery breaking on the mock: a tree toppling, a wall crumbling, a hut caving in, a stone body shattering.
3. Scars: a field from above after a three-minute fight, at a low sun.
4. Layer 0 in the real engine: cannoneers and catapults breaching an Aramon wall and clearing a grove, and a Fire Mage's forest fire spreading downwind over a minute, in Stars of Darien and in the browser's classic view.
5. Magic on the land: one strip for each kind of magic.
6. Performance: the perf probe at each setting, before and after.

## Decisions for the owner

The owner said yes to all seven on 2026-10-03, with one change to the third: Stars of Darien always plays the remastered battlefield rules. The rules stay a room option, off by default, on openkingdoms.net and the classic desktop game. A room that Stars of Darien hosts has them on, and Stars of Darien only joins rooms that have them on.

1. Bring the original's scenery destruction and fire into the shared engine for everyone. Recommended, as it is the original's game. Battles change from today's: forests burn, walls and fences open, ruins keep blocking.
2. Fix the splash radius to half of `areaofeffect` with the original's falloff, as its own issue in the same release. Recommended. It changes every splash weapon against today.
3. Offer remastered battlefield rules as a room option, off by default, which openkingdoms.net runs too. Recommended.
4. Which rules make the first set. Recommended: more can be destroyed, the sweeping spells reach scenery, fire hurts, and rubble blocks until swept. Craters that slow and cover come later, and ground that really deforms is not planned.
5. Craters drawn as real dips with units sitting in them, up to 6 pixels for a cannon and 10 for the largest spells. Recommended.
6. Scars that last the whole battle. Recommended, with Low letting them fade.
7. Moving to URP's Forward+ path for more explosion lights. Recommended, with the built player's GPU smoke run as the check.
