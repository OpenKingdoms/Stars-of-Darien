/* ok_sim.h - the one interface between the simulation core and a host
 * such as Unity. C linkage, an opaque handle, and plain structs of
 * 32-bit fields so a C# [StructLayout(Sequential)] mirror is blittable.
 *
 * The core owns every piece of game state and all randomness. The host
 * sends commands, advances ticks, and reads back what to draw. Two sims
 * fed the same seed and the same commands in the same ticks report the
 * same ok_sim_hash on every platform, which is the lockstep contract. */
#ifndef OK_SIM_H
#define OK_SIM_H

#include <stdint.h>

#if defined(_WIN32) && defined(OK_SIM_SHARED)
#  if defined(OK_SIM_BUILD)
#    define OK_API __declspec(dllexport)
#  else
#    define OK_API __declspec(dllimport)
#  endif
#elif defined(__GNUC__) && defined(OK_SIM_SHARED)
#  define OK_API __attribute__((visibility("default")))
#else
#  define OK_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Bumped whenever a function or struct below changes shape. The host
 * checks it at startup and refuses a plugin it was not built against. */
#define OK_SIM_ABI_VERSION 1

/* World units are 1/65536 of a map cell (16.16 fixed point). */
#define OK_FIXED_ONE 65536

/* Ticks per second of simulated time. */
#define OK_SIM_TICK_RATE 30

typedef struct OkSim OkSim;

/* One unit as the host draws it. heading is in 1/65536 turns so the
 * hash never depends on a float. */
typedef struct OkUnitView {
    int32_t id;
    int32_t player;
    int32_t x, y;
    int32_t heading;
    int32_t state;  /* OK_UNIT_IDLE, OK_UNIT_MOVING */
} OkUnitView;

enum { OK_UNIT_IDLE = 0, OK_UNIT_MOVING = 1 };

/* Something that happened this tick that the host may want to show or
 * play. a and b mean what the kind says. */
typedef struct OkEvent {
    int32_t kind;   /* OK_EVENT_* */
    int32_t tick;
    int32_t unit;
    int32_t a, b;
} OkEvent;

enum {
    OK_EVENT_SPAWNED = 1,  /* a = x, b = y */
    OK_EVENT_ARRIVED = 2   /* a = x, b = y */
};

/* Commands travel as bytes, the same bytes a lockstep turn carries.
 * Every command is OK_CMD_BYTES long, little endian:
 *   u8 kind, u8 player, u16 reserved, i32 unit, i32 x, i32 y */
#define OK_CMD_BYTES 16
enum {
    OK_CMD_SPAWN = 1,  /* unit ignored, spawns at x,y for player */
    OK_CMD_MOVE  = 2   /* unit moves toward x,y */
};

OK_API uint32_t ok_sim_abi_version(void);

/* map_w and map_h are in cells. NULL when out of memory. */
OK_API OkSim  *ok_sim_create(uint32_t seed, int32_t map_w, int32_t map_h);
OK_API void    ok_sim_destroy(OkSim *sim);

/* Queue one command for the next tick. 0 on success, -1 when the
 * bytes are not a command this ABI knows. */
OK_API int32_t ok_sim_push_command(OkSim *sim, const uint8_t *bytes, int32_t len);

OK_API void     ok_sim_tick(OkSim *sim);
OK_API uint32_t ok_sim_tick_count(const OkSim *sim);

/* FNV-1a over everything that decides the future of the game. */
OK_API uint64_t ok_sim_hash(const OkSim *sim);

/* Copy up to cap units into out, returns how many exist. */
OK_API int32_t ok_sim_snapshot(const OkSim *sim, OkUnitView *out, int32_t cap);

/* Move up to cap pending events into out and forget them. Returns how
 * many were written. */
OK_API int32_t ok_sim_drain_events(OkSim *sim, OkEvent *out, int32_t cap);

/* Helpers for hosts that would rather not pack bytes by hand. They
 * write OK_CMD_BYTES into out and return that count. */
OK_API int32_t ok_cmd_spawn(uint8_t *out, int32_t player, int32_t x, int32_t y);
OK_API int32_t ok_cmd_move(uint8_t *out, int32_t player, int32_t unit, int32_t x, int32_t y);

#ifdef __cplusplus
}
#endif

#endif /* OK_SIM_H */
