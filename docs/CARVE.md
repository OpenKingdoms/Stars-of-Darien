# What comes over from OpenKingdoms

Only files written solely by Zach move here. Check with `git log --format='%ae' -- <file>` in OpenKingdoms before copying. Jiri Doubravsky's lines (menus, options, HUD, sound volume) stay behind unless he agrees in writing to a permissive license.

## Already here

Copied from OpenKingdoms at 964dbef.

| File | Why |
| --- | --- |
| `src/core/trig.c`, `include/tak_trig.h` | The engine's own trig, bit-identical on every platform |
| `src/core/sha256.c`, `include/tak_sha256.h` | Data and map fingerprints |
| `include/tak_bytes.h` | Little-endian packing for commands and the wire |
| `include/test_framework.h` and the trig and bytes tests | The same harness |

`include/tak_sim_rand.h` came over at 964dbef too and left again: it cites `legacy:` three times, which breaks the first rule below. The sim now draws from a xorshift32 generator written in `ok_sim.c`.

## Written fresh instead of carved

Checked against OpenKingdoms at 964dbef. A file comes over only with no `legacy:` citation, with Zach as its only author, and on the list below. These failed, so the core has its own versions.

| Wanted | Why it stayed behind | Written here |
| --- | --- | --- |
| `src/game/pathing.c`, `src/game/occupancy.c` | 9 `legacy:` citations each | `src/ok_path.c`, `include/ok_grid.h`: A* on a grid of open and blocked cells, and line of sight smoothing |
| `src/net/turnclock.c`, `include/tak_net_turn.h` | Both are clean, but they need `include/tak_net_protocol.h`, which cites `legacy:194618-194647` | `src/ok_turn.c`, `include/ok_turn.h`: a relay that closes numbered turns and stamps each command with its sender, and a peer that runs a turn only once it holds it |

The wave opponent in `src/ok_ai.c` is new and borrows nothing from `ai.c` or the planners.

## Next, in order

1. The lockstep network: `src/net/protocol.c`, `turnclock.c`, `client.c`, `relay.c`, `relay_link.c`, `link_native.c`, `link_web.c`, `net_socket.c`, `room.c`, `ledger.c`, `server_main.c`, with their tests. `tak_net_protocol.h` includes `tak_map_fingerprint.h`, so bring a trimmed fingerprint or cut that include first. The TA:K order decoder in `command_exec.c` does not come over. The new game's commands are the `OK_CMD_*` bytes.
2. Pathfinding: `src/game/pathing.c` and `occupancy.c`. They read `GameWorld`, `terrain` and `moveinfo`. Put a small grid interface in front of them first (passability per move class, heights, occupancy). Turn the few gameplay rules taken from the original (footprint settle time, water depth gate, gates passable to their owner) into rule data.
3. The AI planners: `ai_goap.c`, `ai_htn.c`, `ai_plan.c`, `ai_influence.c`, `ai_squad.c`, `ai_tasknet.c`, `ai_facts.c`. They are engines with behaviour as tables. The TA:K tables stay behind.
4. The sim hash and probe pattern: `sim_hash.c` and `sim_probe.c` as a model for `ok_sim_hash`, and the CI job that runs one scripted game on every platform and compares hashes.
5. The glTF loader and model store, if a tool needs them outside Unity.

## Never

- `src/render/units.c`: the unit simulation rebuilt from the original, with 354 references to the decompilation
- `src/game/ai.c`, `mission*.c`, `savegame.c`, `economy.c`, `features.c`, `fog.c`, `sides.c`
- Every loader for the original formats: HPI, GAF, 3DO, COB, TNT, TDF, Bink
- `src/ui` and `src/sound`
- Anything that cites `legacy:` for a behaviour, unless the behaviour becomes data and the lawyer has said that is fine
