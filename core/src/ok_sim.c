/* ok_sim.c - the first simulation behind ok_sim.h: units that spawn and
 * walk to a point. Small on purpose. It exists to prove the boundary,
 * the lockstep hash and the Unity binding before real rules arrive. */
#include "ok_sim.h"
#include "tak_bytes.h"
#include "tak_sim_rand.h"
#include "tak_trig.h"

#include <stdlib.h>
#include <string.h>

#define OK_MAX_UNITS    4096
#define OK_MAX_PENDING  1024
#define OK_MAX_EVENTS   4096

/* Walking speed in fixed point per tick: an eighth of a cell. */
#define OK_WALK_SPEED   (OK_FIXED_ONE / 8)

typedef struct {
    int32_t player;
    int32_t x, y;
    int32_t goal_x, goal_y;
    int32_t heading;
    int32_t state;
} Unit;

struct OkSim {
    uint32_t tick;
    uint32_t rand;
    int32_t  map_w, map_h;   /* fixed point */
    int32_t  unit_count;
    Unit     units[OK_MAX_UNITS];
    int32_t  pending_count;
    uint8_t  pending[OK_MAX_PENDING][OK_CMD_BYTES];
    int32_t  event_count;
    OkEvent  events[OK_MAX_EVENTS];
};

uint32_t ok_sim_abi_version(void) { return OK_SIM_ABI_VERSION; }

OkSim *ok_sim_create(uint32_t seed, int32_t map_w, int32_t map_h) {
    OkSim *s = (OkSim *)calloc(1, sizeof *s);
    if (!s) return NULL;
    s->rand = seed ? seed : 1u;
    s->map_w = map_w * OK_FIXED_ONE;
    s->map_h = map_h * OK_FIXED_ONE;
    return s;
}

void ok_sim_destroy(OkSim *sim) { free(sim); }

int32_t ok_sim_push_command(OkSim *sim, const uint8_t *bytes, int32_t len) {
    if (!sim || !bytes || len != OK_CMD_BYTES) return -1;
    uint8_t kind = tak_get_u8(bytes);
    if (kind != OK_CMD_SPAWN && kind != OK_CMD_MOVE) return -1;
    if (sim->pending_count >= OK_MAX_PENDING) return -1;
    memcpy(sim->pending[sim->pending_count++], bytes, OK_CMD_BYTES);
    return 0;
}

static void emit(OkSim *s, int32_t kind, int32_t unit, int32_t a, int32_t b) {
    if (s->event_count >= OK_MAX_EVENTS) return;
    OkEvent *e = &s->events[s->event_count++];
    e->kind = kind;
    e->tick = (int32_t)s->tick;
    e->unit = unit;
    e->a = a;
    e->b = b;
}

static int32_t clamp_i32(int32_t v, int32_t lo, int32_t hi) {
    return v < lo ? lo : v > hi ? hi : v;
}

static void apply(OkSim *s, const uint8_t *c) {
    int32_t player = tak_get_u8(c + 1);
    int32_t unit = tak_get_i32(c + 4);
    int32_t x = clamp_i32(tak_get_i32(c + 8), 0, s->map_w);
    int32_t y = clamp_i32(tak_get_i32(c + 12), 0, s->map_h);
    switch (tak_get_u8(c)) {
    case OK_CMD_SPAWN: {
        if (s->unit_count >= OK_MAX_UNITS) return;
        int32_t id = s->unit_count++;
        Unit *u = &s->units[id];
        memset(u, 0, sizeof *u);
        u->player = player;
        u->x = u->goal_x = x;
        u->y = u->goal_y = y;
        /* A spawn faces somewhere of the sim's choosing, which puts the
         * generator in the hash from the first tick. */
        s->rand = TAK_SimRandStep(s->rand);
        u->heading = (int32_t)(s->rand >> 16);
        emit(s, OK_EVENT_SPAWNED, id, x, y);
        break;
    }
    case OK_CMD_MOVE:
        if (unit < 0 || unit >= s->unit_count) return;
        if (s->units[unit].player != player) return;
        s->units[unit].goal_x = x;
        s->units[unit].goal_y = y;
        s->units[unit].state = OK_UNIT_MOVING;
        break;
    }
}

static uint32_t isqrt64(uint64_t v) {
    uint64_t r = 0, bit = (uint64_t)1 << 62;
    while (bit > v) bit >>= 2;
    while (bit) {
        if (v >= r + bit) { v -= r + bit; r = (r >> 1) + bit; }
        else r >>= 1;
        bit >>= 2;
    }
    return (uint32_t)r;
}

static void step_unit(OkSim *s, int32_t id) {
    Unit *u = &s->units[id];
    if (u->state != OK_UNIT_MOVING) return;
    int64_t dx = (int64_t)u->goal_x - u->x;
    int64_t dy = (int64_t)u->goal_y - u->y;
    uint32_t dist = isqrt64((uint64_t)(dx * dx + dy * dy));
    if (dist <= OK_WALK_SPEED) {
        u->x = u->goal_x;
        u->y = u->goal_y;
        u->state = OK_UNIT_IDLE;
        emit(s, OK_EVENT_ARRIVED, id, u->x, u->y);
        return;
    }
    u->x += (int32_t)(dx * OK_WALK_SPEED / dist);
    u->y += (int32_t)(dy * OK_WALK_SPEED / dist);
    /* The engine's own trig gives the same bits on every platform. */
    float turns = tak_atan2f((float)dy, (float)dx) / 6.28318530718f;
    if (turns < 0.0f) turns += 1.0f;
    u->heading = (int32_t)(turns * 65536.0f) & 0xFFFF;
}

void ok_sim_tick(OkSim *sim) {
    if (!sim) return;
    for (int32_t i = 0; i < sim->pending_count; i++) apply(sim, sim->pending[i]);
    sim->pending_count = 0;
    for (int32_t i = 0; i < sim->unit_count; i++) step_unit(sim, i);
    sim->tick++;
}

uint32_t ok_sim_tick_count(const OkSim *sim) { return sim ? sim->tick : 0; }

static uint64_t fnv(uint64_t h, int32_t v) {
    uint32_t u = (uint32_t)v;
    for (int i = 0; i < 4; i++) {
        h ^= (uint8_t)(u >> (8 * i));
        h *= 1099511628211ull;
    }
    return h;
}

uint64_t ok_sim_hash(const OkSim *sim) {
    uint64_t h = 1469598103934665603ull;
    if (!sim) return h;
    h = fnv(h, (int32_t)sim->tick);
    h = fnv(h, (int32_t)sim->rand);
    h = fnv(h, sim->unit_count);
    for (int32_t i = 0; i < sim->unit_count; i++) {
        const Unit *u = &sim->units[i];
        h = fnv(h, u->player);
        h = fnv(h, u->x);
        h = fnv(h, u->y);
        h = fnv(h, u->goal_x);
        h = fnv(h, u->goal_y);
        h = fnv(h, u->heading);
        h = fnv(h, u->state);
    }
    return h;
}

int32_t ok_sim_snapshot(const OkSim *sim, OkUnitView *out, int32_t cap) {
    if (!sim) return 0;
    for (int32_t i = 0; out && i < sim->unit_count && i < cap; i++) {
        const Unit *u = &sim->units[i];
        out[i].id = i;
        out[i].player = u->player;
        out[i].x = u->x;
        out[i].y = u->y;
        out[i].heading = u->heading;
        out[i].state = u->state;
    }
    return sim->unit_count;
}

int32_t ok_sim_drain_events(OkSim *sim, OkEvent *out, int32_t cap) {
    if (!sim || !out || cap <= 0) return 0;
    int32_t n = sim->event_count < cap ? sim->event_count : cap;
    memcpy(out, sim->events, (size_t)n * sizeof *out);
    memmove(sim->events, sim->events + n,
            (size_t)(sim->event_count - n) * sizeof *out);
    sim->event_count -= n;
    return n;
}

static int32_t pack(uint8_t *out, int32_t kind, int32_t player,
                    int32_t unit, int32_t x, int32_t y) {
    if (!out) return 0;
    memset(out, 0, OK_CMD_BYTES);
    tak_put_u8(out, (uint8_t)kind);
    tak_put_u8(out + 1, (uint8_t)player);
    tak_put_i32(out + 4, unit);
    tak_put_i32(out + 8, x);
    tak_put_i32(out + 12, y);
    return OK_CMD_BYTES;
}

int32_t ok_cmd_spawn(uint8_t *out, int32_t player, int32_t x, int32_t y) {
    return pack(out, OK_CMD_SPAWN, player, -1, x, y);
}

int32_t ok_cmd_move(uint8_t *out, int32_t player, int32_t unit, int32_t x, int32_t y) {
    return pack(out, OK_CMD_MOVE, player, unit, x, y);
}
