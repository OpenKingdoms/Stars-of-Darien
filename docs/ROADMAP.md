# Roadmap

Stars of Darien is a remaster of Total Annihilation: Kingdoms in Unity, running on the OpenKingdoms engine. The engine runs inside Unity as `okengine`, a native library built from the OpenKingdoms branch `unity-embed` with an embedding API, `include/ok_embed.h` (okx API 23 today). `unity/Assets/Engine/OkEngine.cs` binds that API, `EngineBackend.cs` offers it to the game through `IGameBackend`, and the mock engine offers the same interface without game files. The engine owns every rule and all randomness, and every order goes back through its own command queue.

## Where it stands

The first alpha was a Windows build for playtesters with skirmish only. What a player gets today:

- The main menu on the original's own screens, with Skirmish, Load game, Map editor and Options working. Multiplayer works in the editor but is shut in the skirmish-only alpha, and the Adventure door only says it comes later.
- Skirmish setup with the map browser (search, player and size filters, six sort orders), start positions picked by click or drag or dealt at random, kingdoms, up to four seats of computers at four difficulties, teams, line of sight, map revealed, monarch expendable, a unit limit, weather and game speed. The loading screen shows the engine's own progress.
- The battle with the engine's rules and AI. The HUD follows the original's layout in a Carolingian skin that scales to any screen, with the mana pool, unit panel, build menus with queue badges, the minimap, the clock and the original's cursors. Classic controls leave selection to the engine as the original does, and Modern controls add right-click orders.
- Orders with Shift to queue and see a selection's orders, Ctrl to replace the order in hand and keep the queue, control groups, the original's select keys such as Ctrl+Z, a formation drag with shapes, pace and facing, patrol, guard, attack ground, spells and stances, rally points, build counts of one or five and repeat on factory buttons, and turned buildings.
- Fog of war, the original's victory and defeat screens with their tallies and pages of graphs, kingdoms and the battle's annals, save and load from the pause menu, and the engine's own unit voices, battle sounds, interface sounds and music.
- The modern look: URP, sun and shadows, weather, a new sea with surf and wakes, fog drawn the way the original draws it, the original's effects remastered, and 3D models for every scenery type the maps use. About three hundred are hand-built, and the rest are carved models that the game paints at load from the player's own files.
- F9 pictures and Shift+F9 clips, a map editor in the game, and in the Unity editor the Unit Browser, Map Browser, animation editor and Studio Mode.
- Multiplayer rooms over the OpenKingdoms relay: connect, list, host, join by code, seats with sides, teams and ready, the host's rules and map, room chat, and a started battle in lockstep.

The old milestones are all reached in part or in full. The first real map in 3D, the playable skirmish, the game flow from menu to battle, the modern look, the studio tools and the 3D scenery are done. Multiplayer works but is not yet ready for playtesters, and the campaign has not started.

## Parity with the browser game

The browser game at openkingdoms.net is the same engine with its own C front end, so everything in its simulation reaches Stars of Darien for free. What does not reach it is what lives only in that front end or has no okx call yet. This table was checked against the code of both, OpenKingdoms at `f520698` and this repository at `d0b8d3d`. Engine work means new okx calls or fields on `unity-embed`, and Unity work means this repository alone.

| Browser feature | Stars of Darien | What it takes |
| --- | --- | --- |
| Combat parity, veterans scale attack and armour, standing orders, melee by type, an offensive AI (#342) | Has it, since the rules run in the engine | Nothing |
| Footprint slope for buildings (#341), dragon breath from the head (#354) | Has it, from the engine | Nothing |
| Veteran rank shown on units | Has it. `okx_unit_record` gives a living unit's kills and veteran level for the HUD's count and shield | Nothing |
| Rally points | Has it, by a ground click with a factory selected | Nothing |
| Build counts with Shift and Ctrl | Has it on factory buttons | Nothing |
| Shift order overlay (#337) | Has it, from `okx_unit_orders` | Nothing |
| Right click on a builder's button drops its queued buildings of that kind (#337) | Lacks it | Unity: a right click on a mobile builder's card that calls `okx_factory_add` with a negative count and counts its build legs from `okx_unit_orders`. An engine flag for all would be tidier |
| Start positions, map search | Has both | Nothing |
| Game speed levels and pause | Partly. Normal and Fast only, and the pause menu stops the clock | Unity: more speed steps and a pause key, since the host sets the pace of `okx_tick` |
| End screen with units built, kills, losses, time and score | Has it on the original's own screens, with graphs, a page a kingdom and the annals (`docs/BATTLE_RESULTS.md`) | Nothing |
| Replays of every fresh battle, played from the main menu (#352) | Lacks it | Engine: record through okx battles and `okx_replay_*` to list, open and step. Unity: a Replays page, a playback bar and locked orders |
| Typed + commands and power codes (#349) | Lacks it, and there is no chat line in battle | Engine: `okx_console` and a power codes field on `OkxSkirmish`. Unity: a chat line in battle |
| Skirmish rules for power codes, slow game and Crusades balance | Lacks them | Engine: fields on `OkxSkirmish`. Unity: checkboxes |
| Options for music, sound and unit voices apart, resolution | Partly. One volume and music on or off | Engine: music and voice controls beyond `okx_audio`. Unity: the options |
| Original pixel scale (#336) | Lacks it, as the 3D view has no pixel grid | Unity: a classic camera and HUD at one game pixel to one screen pixel, if wanted at all |
| Waiting for a player in battle, desync notice (#346) | Lacks it, although `okx_net_match` already reports both | Unity: read `okx_net_match` and show the line |
| Ping of each host in the room list and your own (#346) | Lacks it | Engine: ping fields on `OkxNetRoom` and `OkxNetSeat` and your own ping. Unity: the columns |
| Chat in battle | Lacks it. Room chat works only in the lobby | Unity: a chat line in battle over `okx_net_chat` |
| Leaderboard by device and live games (#347) | Lacks it. An embedded battle never reports its result, since only the C front end calls `TAK_Match_ReportResult` | Engine: report the result from the embedded tick and hand out the device token. Unity: a link or page for the leaderboard |
| Notices when someone hosts a game (#345) | Lacks it | Unity: poll the relay's `/api/rooms` and notify |
| Rejoin after a drop | Lacks it | Engine: rejoin through okx. Unity: the rejoin flow on the loading screen |
| Campaign, the Book of Deeds, briefings, objectives, mission clips | Lacks it. The Adventure door is shut | Engine: `okx_mission_*` for loading a mission, objectives, briefings and the story's progress. Unity: the book, briefings and clip playback |
| Map editor, formation drag, Studio Mode | Stars of Darien has these and the browser game does not | Nothing |

Neither game has starting resources, key rebinding or watching a battle as a spectator, although the relay can take watchers.

## Plan

The order puts first what playtesters of a skirmish-only alpha would miss most, then multiplayer in Stars of Darien, then the campaign, then a campaign for each kingdom in a beta.

### Alpha 2

1. A right click on a builder's card drops its queued buildings of that kind, as in the browser game. Unity only. Small.
2. Finer game speed and a pause key in battle, as the browser game's speed levels. Unity only. Small.
3. Replays: every fresh skirmish recorded, a Replays page on the main menu, and playback with pause and speed. This needs the engine to record okx battles and a `okx_replay_*` API. Large.
4. A chat line in battle that runs typed + commands, with power codes when the rules allow them, and the skirmish rules for power codes, slow game and Crusades balance. This needs `okx_console` and the new `OkxSkirmish` fields. Medium.
5. Options for music, sound and unit voices apart, and a resolution setting. Small on each side.

### Multiplayer in Stars of Darien

6. Open the Multiplayer door, with chat in battle, the waiting line and the desync notice from `okx_net_match`. Mostly Unity. Medium.
7. Ping in the room list and on each seat, results reported to the relay so battles count on the leaderboard, a way to reach the leaderboard, and notices when someone hosts. This needs ping fields, result reporting from the embedded tick and the device token in the engine. Medium.
8. Rejoin after a drop, and watching a battle once the browser game offers it too. Large.

### The campaign

9. The Book of Deeds, mission briefings, objectives and the mission clips, for Darien and then the Iron Plague. This needs `okx_mission_*` and the story's progress in the engine and a book, briefings and clip playback here. Large.

### Beta

10. A campaign for each kingdom, Aramon, Veruna, Taros and Zhon. It belongs in a beta and is not part of Alpha 1, and it builds on the campaign above. Large.

### Later

- An Original pixel scale for the classic camera, if playtesters ask for the old look.
- Key rebinding and starting resources, which neither game has yet.

## Drop-in models

One naming rule on both sides: `<unitname>.glb` for a unit and `<feature>.glb` for a feature, matched without regard to case. While iterating in the editor, models go in `unity/Assets/Overrides/Units` and `unity/Assets/Overrides/Features` (glb, gltf, fbx or prefab), and `docs/STUDIO.md` covers scale, pivot and facing. For a shipped or modded build, `.glb` files in `unity/Assets/StreamingAssets/Overrides/` are read by the engine's own glTF loader. That loader goes by the model's object name, which for most units is the unit name, and a node named like a piece of the original model follows that piece's pose.
