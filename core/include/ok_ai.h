/* ok_ai.h - a computer opponent that raises waves at its base and sends
 * them at the nearest enemy. It reads the sim and writes commands, which
 * travel through the turn relay like a player's, so only the machine
 * that runs it needs it and the sims stay in lockstep. */
#ifndef OK_AI_H
#define OK_AI_H

#include "ok_sim.h"

#ifdef __cplusplus
extern "C" {
#endif

/* The first wave comes after OK_AI_FIRST_WAVE ticks, then one every
 * OK_AI_WAVE_PERIOD. Wave n (from 0) has OK_AI_WAVE_BASE + 2n units, a
 * third of them archers. */
#define OK_AI_FIRST_WAVE   300
#define OK_AI_WAVE_PERIOD  600
#define OK_AI_WAVE_BASE    4
/* A wave sets off this long after it appears, so it moves as a group. */
#define OK_AI_MUSTER       30
/* With every wave raised, idle units set off this often. */
#define OK_AI_MOP_UP       150

typedef struct OkAi OkAi;

/* base_cx, base_cy is the cell the waves gather around. NULL when out
 * of memory. */
OK_API OkAi   *ok_ai_create(int32_t player, int32_t base_cx, int32_t base_cy, int32_t waves);
OK_API void    ok_ai_destroy(OkAi *ai);

/* Look at the sim and write this player's commands into out, whole
 * commands only. Returns the bytes written. Call it once a turn. */
OK_API int32_t ok_ai_think(OkAi *ai, const OkSim *sim, uint8_t *out, int32_t cap);

/* Waves not yet raised. */
OK_API int32_t ok_ai_waves_left(const OkAi *ai);

#ifdef __cplusplus
}
#endif

#endif /* OK_AI_H */
