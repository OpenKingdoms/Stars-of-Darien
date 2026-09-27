/* ok_ai.c - the wave opponent. Written for this project. */
#include "ok_ai.h"

#include <stdlib.h>

struct OkAi {
    int32_t player;
    int32_t base_cx, base_cy;
    int32_t waves_left;
    int32_t waves_sent;
    int32_t next_wave;   /* tick */
    int32_t send_at;     /* tick, or -1 when no wave is mustering */
    OkUnitView views[OK_SIM_MAX_UNITS];
};

OkAi *ok_ai_create(int32_t player, int32_t base_cx, int32_t base_cy, int32_t waves) {
    OkAi *ai = (OkAi *)calloc(1, sizeof *ai);
    if (!ai) return NULL;
    ai->player = player;
    ai->base_cx = base_cx;
    ai->base_cy = base_cy;
    ai->waves_left = waves < 0 ? 0 : waves;
    ai->next_wave = OK_AI_FIRST_WAVE;
    ai->send_at = -1;
    return ai;
}

void ok_ai_destroy(OkAi *ai) { free(ai); }

int32_t ok_ai_waves_left(const OkAi *ai) { return ai ? ai->waves_left : 0; }

static int32_t sq_dist(const OkUnitView *a, const OkUnitView *b) {
    int64_t dx = ((int64_t)a->x - b->x) / 256, dy = ((int64_t)a->y - b->y) / 256;
    int64_t d = dx * dx + dy * dy;
    return d > INT32_MAX ? INT32_MAX : (int32_t)d;
}

/* Raise a wave on open cells in a square spiral around the base. */
static int32_t raise_wave(OkAi *ai, const OkSim *sim, uint8_t *out, int32_t cap) {
    int32_t size = OK_AI_WAVE_BASE + 2 * ai->waves_sent;
    int32_t written = 0, placed = 0;
    for (int32_t ring = 0; ring < 12 && placed < size; ring++) {
        for (int32_t dy = -ring; dy <= ring && placed < size; dy++) {
            for (int32_t dx = -ring; dx <= ring && placed < size; dx++) {
                if (dx != -ring && dx != ring && dy != -ring && dy != ring) continue;
                int32_t cx = ai->base_cx + dx, cy = ai->base_cy + dy;
                if (ok_sim_is_blocked(sim, cx, cy)) continue;
                if (cap - written < OK_CMD_BYTES) return written;
                int32_t kind = placed % 3 == 2 ? OK_KIND_ARCHER : OK_KIND_SOLDIER;
                written += ok_cmd_spawn(out + written, ai->player, kind,
                                        cx * OK_FIXED_ONE + OK_FIXED_ONE / 2,
                                        cy * OK_FIXED_ONE + OK_FIXED_ONE / 2);
                placed++;
            }
        }
    }
    return written;
}

/* Every idle unit of ours goes for the enemy nearest to it. */
static int32_t send_idle(OkAi *ai, const OkSim *sim, uint8_t *out, int32_t cap) {
    int32_t n = ok_sim_snapshot(sim, ai->views, OK_SIM_MAX_UNITS);
    if (n > OK_SIM_MAX_UNITS) n = OK_SIM_MAX_UNITS;
    int32_t written = 0;
    for (int32_t i = 0; i < n; i++) {
        const OkUnitView *u = &ai->views[i];
        if (u->player != ai->player || u->state != OK_UNIT_IDLE) continue;
        int32_t best = -1, best_d = INT32_MAX;
        for (int32_t j = 0; j < n; j++) {
            const OkUnitView *t = &ai->views[j];
            if (t->player == ai->player || t->state == OK_UNIT_DEAD) continue;
            int32_t d = sq_dist(u, t);
            if (d < best_d) { best_d = d; best = j; }
        }
        if (best < 0) break;
        if (cap - written < OK_CMD_BYTES) break;
        written += ok_cmd_attack(out + written, ai->player, u->id, best);
    }
    return written;
}

int32_t ok_ai_think(OkAi *ai, const OkSim *sim, uint8_t *out, int32_t cap) {
    if (!ai || !sim || !out) return 0;
    int32_t tick = (int32_t)ok_sim_tick_count(sim);
    int32_t written = 0;
    if (ai->send_at >= 0 && tick >= ai->send_at) {
        written += send_idle(ai, sim, out + written, cap - written);
        ai->send_at = -1;
    }
    /* Its waves spent, it sends whatever stands idle now and then, so a
     * game never stalls with two armies waiting apart. */
    if (ai->waves_left == 0 && tick >= ai->next_wave) {
        written += send_idle(ai, sim, out + written, cap - written);
        ai->next_wave = tick + OK_AI_MOP_UP;
    }
    if (ai->waves_left > 0 && tick >= ai->next_wave) {
        written += raise_wave(ai, sim, out + written, cap - written);
        ai->waves_left--;
        ai->waves_sent++;
        ai->next_wave += OK_AI_WAVE_PERIOD;
        ai->send_at = tick + OK_AI_MUSTER;
    }
    return written;
}
