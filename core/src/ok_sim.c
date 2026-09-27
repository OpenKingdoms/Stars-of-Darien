/* ok_sim.c - the simulation behind ok_sim.h: two or more teams of units
 * that walk around obstacles, chase, and fight hand to hand or at range.
 * Integer math throughout, and every loop runs in unit id order, so the
 * same commands give the same game on every platform. */
#include "ok_sim.h"
#include "ok_grid.h"
#include "tak_bytes.h"
#include "tak_trig.h"

#include <stdlib.h>
#include <string.h>

#define OK_MAX_PENDING  1024
#define OK_MAX_EVENTS   4096

/* A path keeps this many straightened waypoints, and a search returns
 * at most this many cells. A longer route is walked in pieces. */
#define OK_WAYPOINTS    32
#define OK_PATH_CELLS   256

/* A chasing unit looks for a new route to its moving target this often. */
#define REPATH_TICKS    15
/* A walker near a crowded goal that stops gaining ground stops there. */
#define STALL_TICKS     20
#define STALL_NEAR      (OK_FIXED_ONE * 2)
/* One that gains nothing for this long anywhere looks for a new route. */
#define STUCK_TICKS     90

/* Units closer than this push apart, by at most PUSH_MAX a tick. */
#define SEPARATION      (OK_FIXED_ONE * 7 / 10)
#define PUSH_MAX        (OK_FIXED_ONE / 16)

/* The rules for each kind, in fixed point where they are distances. */
typedef struct {
    int32_t hp;
    int32_t speed;     /* per tick */
    int32_t range;     /* centre to centre */
    int32_t aggro;     /* an idle unit takes on enemies this close */
    int32_t damage;
    int32_t spread;    /* damage is damage + a draw below spread */
    int32_t cooldown;  /* ticks between blows */
} Rules;

static const Rules RULES[OK_KIND_COUNT] = {
    /* soldier */ { 100, OK_FIXED_ONE * 3 / 32, OK_FIXED_ONE * 5 / 4,
                    OK_FIXED_ONE * 7, 14, 5, 24 },
    /* archer  */ {  60, OK_FIXED_ONE * 5 / 64, OK_FIXED_ONE * 6,
                    OK_FIXED_ONE * 7,  9, 4, 36 },
};

enum { ORDER_NONE = 0, ORDER_MOVE = 1, ORDER_ATTACK = 2 };

typedef struct {
    int32_t player, kind;
    int32_t x, y, heading, state;
    int32_t hp, cooldown;
    int32_t order, target;
    int32_t goal_x, goal_y;
    int32_t need_path, truncated, repath_at;
    int32_t path_n, path_i;
    int32_t wp_x[OK_WAYPOINTS], wp_y[OK_WAYPOINTS];
    int32_t best_dist, stall;
} Unit;

struct OkSim {
    uint32_t tick;
    uint32_t rand;
    int32_t  unit_count;
    Unit     units[OK_SIM_MAX_UNITS];
    int32_t  pending_count;
    uint8_t  pending[OK_MAX_PENDING][OK_CMD_BYTES];
    int32_t  event_count;
    OkEvent  events[OK_MAX_EVENTS];
    OkGrid   grid;
    /* Working memory, never part of the state. */
    int32_t  cells[OK_PATH_CELLS];
    OkPathScratch scratch;
};

uint32_t ok_sim_abi_version(void) { return OK_SIM_ABI_VERSION; }

OkSim *ok_sim_create(uint32_t seed, int32_t map_w, int32_t map_h) {
    if (map_w < 1 || map_h < 1 || map_w > OK_SIM_MAX_SIDE || map_h > OK_SIM_MAX_SIDE)
        return NULL;
    OkSim *s = (OkSim *)calloc(1, sizeof *s);
    if (!s) return NULL;
    s->rand = seed ? seed : 1u;
    ok_grid_init(&s->grid, map_w, map_h);
    return s;
}

void ok_sim_destroy(OkSim *sim) { free(sim); }

int32_t ok_sim_set_blocked(OkSim *sim, int32_t cx, int32_t cy, int32_t blocked) {
    if (!sim || cx < 0 || cy < 0 || cx >= sim->grid.w || cy >= sim->grid.h) return -1;
    ok_grid_set_blocked(&sim->grid, cx, cy, blocked);
    return 0;
}

int32_t ok_sim_is_blocked(const OkSim *sim, int32_t cx, int32_t cy) {
    return sim ? !ok_grid_open(&sim->grid, cx, cy) : 1;
}

int32_t ok_sim_push_command(OkSim *sim, const uint8_t *bytes, int32_t len) {
    if (!sim || !bytes || len != OK_CMD_BYTES) return -1;
    uint8_t kind = tak_get_u8(bytes);
    if (kind < OK_CMD_SPAWN || kind > OK_CMD_STOP) return -1;
    if (sim->pending_count >= OK_MAX_PENDING) return -1;
    memcpy(sim->pending[sim->pending_count++], bytes, OK_CMD_BYTES);
    return 0;
}

/* xorshift32, seeded non-zero. The only randomness in the game. */
static uint32_t draw(OkSim *s) {
    uint32_t x = s->rand;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    s->rand = x;
    return x;
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

static int32_t dist(int32_t ax, int32_t ay, int32_t bx, int32_t by) {
    int64_t dx = (int64_t)bx - ax, dy = (int64_t)by - ay;
    return (int32_t)isqrt64((uint64_t)(dx * dx + dy * dy));
}

static int alive(const Unit *u) { return u->state != OK_UNIT_DEAD; }

static int32_t cell_centre(int32_t c) { return c * OK_FIXED_ONE + OK_FIXED_ONE / 2; }

/* The engine's own trig gives the same bits on every platform. */
static void face(Unit *u, int32_t dx, int32_t dy) {
    if (dx == 0 && dy == 0) return;
    float turns = tak_atan2f((float)dy, (float)dx) / 6.28318530718f;
    if (turns < 0.0f) turns += 1.0f;
    u->heading = (int32_t)(turns * 65536.0f) & 0xFFFF;
}

static void clear_path(Unit *u) {
    u->path_n = 0;
    u->path_i = 0;
    u->need_path = 0;
    u->truncated = 0;
}

static void plan(OkSim *s, Unit *u, int32_t gx, int32_t gy) {
    const int32_t w = s->grid.w;
    int32_t scx = u->x / OK_FIXED_ONE, scy = u->y / OK_FIXED_ONE;
    int32_t gcx = gx / OK_FIXED_ONE, gcy = gy / OK_FIXED_ONE;
    int32_t n = ok_path_find(&s->grid, &s->scratch, scx, scy, gcx, gcy,
                             s->cells, OK_PATH_CELLS);
    int reaches = n > 0 ? s->cells[n - 1] == gcy * w + gcx
                        : (scx == gcx && scy == gcy);
    int truncated = n == OK_PATH_CELLS;
    n = ok_path_smooth(&s->grid, u->x, u->y, s->cells, n);
    if (n > OK_WAYPOINTS) { n = OK_WAYPOINTS; truncated = 1; }
    for (int32_t i = 0; i < n; i++) {
        u->wp_x[i] = cell_centre(s->cells[i] % w);
        u->wp_y[i] = cell_centre(s->cells[i] / w);
    }
    /* A reachable goal is walked to exactly, not to its cell's centre. */
    if (reaches && !truncated) {
        if (n == 0) n = 1;
        u->wp_x[n - 1] = gx;
        u->wp_y[n - 1] = gy;
    }
    u->path_n = n;
    u->path_i = 0;
    u->need_path = 0;
    u->truncated = truncated;
}

/* Walk up to one tick's distance along the path. 1 when the path is
 * used up. A step into a blocked cell slides along it, and a unit that
 * cannot move at all asks for a new route. */
static int walk(OkSim *s, Unit *u) {
    int32_t budget = RULES[u->kind].speed;
    while (budget > 0 && u->path_i < u->path_n) {
        int32_t tx = u->wp_x[u->path_i], ty = u->wp_y[u->path_i];
        int64_t dx = (int64_t)tx - u->x, dy = (int64_t)ty - u->y;
        int32_t d = dist(u->x, u->y, tx, ty);
        face(u, (int32_t)dx, (int32_t)dy);
        if (d <= budget) {
            u->x = tx;
            u->y = ty;
            budget -= d;
            u->path_i++;
            continue;
        }
        int32_t nx = u->x + (int32_t)(dx * budget / d);
        int32_t ny = u->y + (int32_t)(dy * budget / d);
        if (ok_grid_open_at(&s->grid, nx, ny)) { u->x = nx; u->y = ny; }
        else if (ok_grid_open_at(&s->grid, nx, u->y)) u->x = nx;
        else if (ok_grid_open_at(&s->grid, u->x, ny)) u->y = ny;
        else u->need_path = 1;
        budget = 0;
    }
    return u->path_i >= u->path_n;
}

static void arrive(OkSim *s, int32_t id) {
    Unit *u = &s->units[id];
    u->order = ORDER_NONE;
    u->state = OK_UNIT_IDLE;
    clear_path(u);
    emit(s, OK_EVENT_ARRIVED, id, u->x, u->y);
}

static void start_move(Unit *u, int32_t x, int32_t y) {
    u->order = ORDER_MOVE;
    u->target = -1;
    u->goal_x = x;
    u->goal_y = y;
    clear_path(u);
    u->need_path = 1;
    u->best_dist = INT32_MAX;
    u->stall = 0;
    u->state = OK_UNIT_MOVING;
}

static void start_attack(Unit *u, int32_t target) {
    u->order = ORDER_ATTACK;
    u->target = target;
    clear_path(u);
    u->need_path = 1;
    u->repath_at = 0;
}

static void stand(Unit *u) {
    u->order = ORDER_NONE;
    u->target = -1;
    clear_path(u);
    u->state = OK_UNIT_IDLE;
}

static void apply(OkSim *s, const uint8_t *c) {
    int32_t player = tak_get_u8(c + 1);
    int32_t arg = tak_get_u16(c + 2);
    int32_t unit = tak_get_i32(c + 4);
    int32_t rx = tak_get_i32(c + 8);
    int32_t ry = tak_get_i32(c + 12);
    int32_t x = clamp_i32(rx, 0, s->grid.w * OK_FIXED_ONE - 1);
    int32_t y = clamp_i32(ry, 0, s->grid.h * OK_FIXED_ONE - 1);
    uint8_t kind = tak_get_u8(c);

    if (kind == OK_CMD_SPAWN) {
        if (arg < 0 || arg >= OK_KIND_COUNT) return;
        if (s->unit_count >= OK_SIM_MAX_UNITS) return;
        if (!ok_grid_open_at(&s->grid, x, y)) return;
        int32_t id = s->unit_count++;
        Unit *u = &s->units[id];
        memset(u, 0, sizeof *u);
        u->player = player;
        u->kind = arg;
        u->x = u->goal_x = x;
        u->y = u->goal_y = y;
        u->hp = RULES[arg].hp;
        u->target = -1;
        /* A spawn faces somewhere of the sim's choosing, which puts the
         * generator in the hash from the first tick. */
        u->heading = (int32_t)(draw(s) >> 16);
        emit(s, OK_EVENT_SPAWNED, id, x, y);
        return;
    }

    if (unit < 0 || unit >= s->unit_count) return;
    Unit *u = &s->units[unit];
    if (u->player != player || !alive(u)) return;
    switch (kind) {
    case OK_CMD_MOVE:
        start_move(u, x, y);
        break;
    case OK_CMD_ATTACK: {
        if (rx < 0 || rx >= s->unit_count) return;
        const Unit *t = &s->units[rx];
        if (!alive(t) || t->player == player) return;
        start_attack(u, rx);
        break;
    }
    case OK_CMD_STOP:
        stand(u);
        break;
    }
}

/* The nearest living enemy within reach, lowest id on a tie, or -1. */
static int32_t nearest_enemy(const OkSim *s, int32_t id, int32_t reach) {
    const Unit *u = &s->units[id];
    int32_t best = -1, best_d = reach + 1;
    for (int32_t i = 0; i < s->unit_count; i++) {
        const Unit *t = &s->units[i];
        if (!alive(t) || t->player == u->player) continue;
        int32_t d = dist(u->x, u->y, t->x, t->y);
        if (d < best_d) { best_d = d; best = i; }
    }
    return best;
}

static void strike(OkSim *s, int32_t id, int32_t tid) {
    Unit *u = &s->units[id];
    Unit *t = &s->units[tid];
    const Rules *r = &RULES[u->kind];
    int32_t dmg = r->damage + (r->spread > 0 ? (int32_t)(draw(s) % (uint32_t)r->spread) : 0);
    u->cooldown = r->cooldown;
    t->hp -= dmg;
    emit(s, OK_EVENT_ATTACKED, id, tid, dmg);
    if (t->hp <= 0) {
        t->hp = 0;
        stand(t);
        t->state = OK_UNIT_DEAD;
        emit(s, OK_EVENT_DIED, tid, id, t->player);
        u->order = ORDER_NONE;
        u->target = -1;
        u->state = OK_UNIT_IDLE;
    }
}

static void update_move(OkSim *s, int32_t id) {
    Unit *u = &s->units[id];
    if (u->need_path) plan(s, u, u->goal_x, u->goal_y);
    u->state = OK_UNIT_MOVING;
    if (walk(s, u) && !u->need_path) {
        if (u->truncated) u->need_path = 1;
        else { arrive(s, id); return; }
    }
    int32_t d = dist(u->x, u->y, u->goal_x, u->goal_y);
    if (d + OK_FIXED_ONE / 8 < u->best_dist) {
        u->best_dist = d;
        u->stall = 0;
        return;
    }
    u->stall++;
    if (u->stall >= STALL_TICKS && d < STALL_NEAR) arrive(s, id);
    else if (u->stall >= STUCK_TICKS) {
        u->need_path = 1;
        u->stall = 0;
        u->best_dist = d;
    }
}

static void update_attack(OkSim *s, int32_t id) {
    Unit *u = &s->units[id];
    const Unit *t = &s->units[u->target];
    int32_t d = dist(u->x, u->y, t->x, t->y);
    if (d <= RULES[u->kind].range) {
        clear_path(u);
        face(u, t->x - u->x, t->y - u->y);
        u->state = OK_UNIT_ATTACKING;
        if (u->cooldown == 0) strike(s, id, u->target);
        return;
    }
    u->state = OK_UNIT_MOVING;
    if (u->need_path || (int32_t)s->tick >= u->repath_at) {
        plan(s, u, t->x, t->y);
        u->repath_at = (int32_t)s->tick + REPATH_TICKS + id % 5;
    }
    walk(s, u);
}

static void update_unit(OkSim *s, int32_t id) {
    Unit *u = &s->units[id];
    if (!alive(u)) return;
    if (u->cooldown > 0) u->cooldown--;
    if (u->order == ORDER_ATTACK) {
        const Unit *t = &s->units[u->target];
        if (!alive(t)) stand(u);
    }
    if (u->order == ORDER_NONE) {
        int32_t e = nearest_enemy(s, id, RULES[u->kind].aggro);
        if (e >= 0) start_attack(u, e);
        else u->state = OK_UNIT_IDLE;
    }
    if (u->order == ORDER_MOVE) update_move(s, id);
    else if (u->order == ORDER_ATTACK) update_attack(s, id);
}

/* Units that overlap push apart, each by half the overlap, never into
 * a blocked cell. Two on the very same spot part along x by id. */
static void push(OkSim *s, Unit *u, int32_t px, int32_t py) {
    int32_t nx = u->x + px, ny = u->y + py;
    if (ok_grid_open_at(&s->grid, nx, ny)) { u->x = nx; u->y = ny; }
}

static void separate(OkSim *s) {
    for (int32_t i = 0; i < s->unit_count; i++) {
        Unit *a = &s->units[i];
        if (!alive(a)) continue;
        for (int32_t j = i + 1; j < s->unit_count; j++) {
            Unit *b = &s->units[j];
            if (!alive(b)) continue;
            int32_t dx = b->x - a->x, dy = b->y - a->y;
            if (dx >= SEPARATION || dx <= -SEPARATION ||
                dy >= SEPARATION || dy <= -SEPARATION) continue;
            int32_t d = dist(a->x, a->y, b->x, b->y);
            if (d >= SEPARATION) continue;
            int32_t amount = (SEPARATION - d) / 2;
            if (amount > PUSH_MAX) amount = PUSH_MAX;
            int32_t px, py;
            if (d == 0) { px = amount; py = 0; }
            else {
                px = (int32_t)((int64_t)dx * amount / d);
                py = (int32_t)((int64_t)dy * amount / d);
            }
            push(s, a, -px, -py);
            push(s, b, px, py);
        }
    }
}

void ok_sim_tick(OkSim *sim) {
    if (!sim) return;
    for (int32_t i = 0; i < sim->pending_count; i++) apply(sim, sim->pending[i]);
    sim->pending_count = 0;
    for (int32_t i = 0; i < sim->unit_count; i++) update_unit(sim, i);
    separate(sim);
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
    h = fnv(h, sim->grid.w);
    h = fnv(h, sim->grid.h);
    for (int32_t i = 0; i < sim->grid.w * sim->grid.h; i++) {
        h ^= sim->grid.blocked[i];
        h *= 1099511628211ull;
    }
    h = fnv(h, sim->unit_count);
    for (int32_t i = 0; i < sim->unit_count; i++) {
        const Unit *u = &sim->units[i];
        h = fnv(h, u->player);
        h = fnv(h, u->kind);
        h = fnv(h, u->x);
        h = fnv(h, u->y);
        h = fnv(h, u->heading);
        h = fnv(h, u->state);
        h = fnv(h, u->hp);
        h = fnv(h, u->cooldown);
        h = fnv(h, u->order);
        h = fnv(h, u->target);
        h = fnv(h, u->goal_x);
        h = fnv(h, u->goal_y);
        h = fnv(h, u->need_path);
        h = fnv(h, u->truncated);
        h = fnv(h, u->repath_at);
        h = fnv(h, u->best_dist);
        h = fnv(h, u->stall);
        h = fnv(h, u->path_n);
        h = fnv(h, u->path_i);
        for (int32_t k = 0; k < u->path_n; k++) {
            h = fnv(h, u->wp_x[k]);
            h = fnv(h, u->wp_y[k]);
        }
    }
    /* Commands queued for the next tick decide it too. */
    h = fnv(h, sim->pending_count);
    for (int32_t i = 0; i < sim->pending_count; i++)
        for (int k = 0; k < OK_CMD_BYTES; k++) {
            h ^= sim->pending[i][k];
            h *= 1099511628211ull;
        }
    return h;
}

int32_t ok_sim_snapshot(const OkSim *sim, OkUnitView *out, int32_t cap) {
    if (!sim) return 0;
    for (int32_t i = 0; out && i < sim->unit_count && i < cap; i++) {
        const Unit *u = &sim->units[i];
        out[i].id = i;
        out[i].player = u->player;
        out[i].kind = u->kind;
        out[i].x = u->x;
        out[i].y = u->y;
        out[i].heading = u->heading;
        out[i].state = u->state;
        out[i].hp = u->hp;
        out[i].max_hp = RULES[u->kind].hp;
        out[i].target = u->order == ORDER_ATTACK ? u->target : -1;
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

int32_t ok_sim_winner(const OkSim *sim) {
    if (!sim) return -2;
    int32_t found = -2;
    for (int32_t i = 0; i < sim->unit_count; i++) {
        const Unit *u = &sim->units[i];
        if (!alive(u)) continue;
        if (found == -2) found = u->player;
        else if (u->player != found) return -1;
    }
    return found;
}

static int32_t pack(uint8_t *out, int32_t kind, int32_t player, int32_t arg,
                    int32_t unit, int32_t x, int32_t y) {
    if (!out) return 0;
    memset(out, 0, OK_CMD_BYTES);
    tak_put_u8(out, (uint8_t)kind);
    tak_put_u8(out + 1, (uint8_t)player);
    tak_put_u16(out + 2, (uint16_t)arg);
    tak_put_i32(out + 4, unit);
    tak_put_i32(out + 8, x);
    tak_put_i32(out + 12, y);
    return OK_CMD_BYTES;
}

int32_t ok_cmd_spawn(uint8_t *out, int32_t player, int32_t kind, int32_t x, int32_t y) {
    return pack(out, OK_CMD_SPAWN, player, kind, -1, x, y);
}

int32_t ok_cmd_move(uint8_t *out, int32_t player, int32_t unit, int32_t x, int32_t y) {
    return pack(out, OK_CMD_MOVE, player, 0, unit, x, y);
}

int32_t ok_cmd_attack(uint8_t *out, int32_t player, int32_t unit, int32_t target) {
    return pack(out, OK_CMD_ATTACK, player, 0, unit, target, 0);
}

int32_t ok_cmd_stop(uint8_t *out, int32_t player, int32_t unit) {
    return pack(out, OK_CMD_STOP, player, 0, unit, 0, 0);
}
