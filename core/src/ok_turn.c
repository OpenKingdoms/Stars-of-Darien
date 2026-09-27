/* ok_turn.c - the relay and peer halves of lockstep. Written for this
 * project. */
#include "ok_turn.h"
#include "tak_bytes.h"

#include <stdlib.h>
#include <string.h>

#define RELAY_QUEUE   1024
#define HASH_ROWS     16

typedef struct {
    uint32_t turn;
    uint32_t have;   /* seats that reported */
    int32_t  used;
    uint64_t first;  /* the first hash reported for the turn */
} HashRow;

struct OkRelay {
    uint32_t turn;          /* the open turn */
    int32_t  queued;
    uint8_t  queue[RELAY_QUEUE][OK_CMD_BYTES];
    HashRow  rows[HASH_ROWS];
    uint32_t desyncs;
};

typedef struct {
    int32_t  held;
    uint32_t turn;
    int32_t  count;
    uint8_t  cmds[OK_TURN_MAX_CMDS][OK_CMD_BYTES];
} Slot;

struct OkPeer {
    uint32_t next;       /* the turn being run or waited for */
    int32_t  sub;        /* ticks of it already run */
    Slot     slots[OK_TURN_WINDOW];
};

static int is_command(const uint8_t *bytes) {
    uint8_t k = tak_get_u8(bytes);
    return k >= OK_CMD_SPAWN && k <= OK_CMD_STOP;
}

OkRelay *ok_relay_create(void) { return (OkRelay *)calloc(1, sizeof(OkRelay)); }
void ok_relay_destroy(OkRelay *relay) { free(relay); }

int32_t ok_relay_command(OkRelay *r, int32_t seat, const uint8_t *bytes, int32_t len) {
    if (!r || !bytes || len != OK_CMD_BYTES) return -1;
    if (seat < 0 || seat >= OK_TURN_SEATS) return -1;
    if (!is_command(bytes) || r->queued >= RELAY_QUEUE) return -1;
    uint8_t *q = r->queue[r->queued++];
    memcpy(q, bytes, OK_CMD_BYTES);
    tak_put_u8(q + 1, (uint8_t)seat);
    return 0;
}

int32_t ok_relay_close_turn(OkRelay *r, uint8_t *out, int32_t cap) {
    if (!r || !out) return 0;
    int32_t n = r->queued < OK_TURN_MAX_CMDS ? r->queued : OK_TURN_MAX_CMDS;
    int32_t len = OK_TURN_HEADER + n * OK_CMD_BYTES;
    if (cap < len) return 0;
    tak_put_u32(out, r->turn);
    tak_put_u16(out + 4, (uint16_t)n);
    tak_put_u16(out + 6, 0);
    memcpy(out + OK_TURN_HEADER, r->queue, (size_t)n * OK_CMD_BYTES);
    memmove(r->queue, r->queue + n, (size_t)(r->queued - n) * OK_CMD_BYTES);
    r->queued -= n;
    r->turn++;
    return len;
}

uint32_t ok_relay_turns(const OkRelay *r) { return r ? r->turn : 0; }

int32_t ok_relay_report_hash(OkRelay *r, int32_t seat, uint32_t turn, uint64_t hash) {
    if (!r || seat < 0 || seat >= OK_TURN_SEATS) return 0;
    HashRow *row = &r->rows[turn % HASH_ROWS];
    if (!row->used || row->turn != turn) {
        row->used = 1;
        row->turn = turn;
        row->have = 1u << seat;
        row->first = hash;
        return 0;
    }
    row->have |= 1u << seat;
    if (hash == row->first) return 0;
    r->desyncs++;
    return 1;
}

uint32_t ok_relay_desyncs(const OkRelay *r) { return r ? r->desyncs : 0; }

OkPeer *ok_peer_create(void) { return (OkPeer *)calloc(1, sizeof(OkPeer)); }
void ok_peer_destroy(OkPeer *peer) { free(peer); }

int32_t ok_peer_receive(OkPeer *p, const uint8_t *frame, int32_t len) {
    if (!p || !frame || len < OK_TURN_HEADER) return -1;
    uint32_t turn = tak_get_u32(frame);
    int32_t count = tak_get_u16(frame + 4);
    if (count > OK_TURN_MAX_CMDS || len != OK_TURN_HEADER + count * OK_CMD_BYTES)
        return -1;
    for (int32_t i = 0; i < count; i++)
        if (!is_command(frame + OK_TURN_HEADER + i * OK_CMD_BYTES)) return -1;
    if (turn < p->next) return 0;
    if (turn - p->next >= OK_TURN_WINDOW) return -1;
    Slot *s = &p->slots[turn % OK_TURN_WINDOW];
    if (s->held && s->turn == turn) return 0;
    s->held = 1;
    s->turn = turn;
    s->count = count;
    memcpy(s->cmds, frame + OK_TURN_HEADER, (size_t)count * OK_CMD_BYTES);
    return 0;
}

int32_t ok_peer_step(OkPeer *p, OkSim *sim) {
    if (!p || !sim) return 0;
    Slot *s = &p->slots[p->next % OK_TURN_WINDOW];
    if (!s->held || s->turn != p->next) return 0;
    if (p->sub == 0)
        for (int32_t i = 0; i < s->count; i++)
            ok_sim_push_command(sim, s->cmds[i], OK_CMD_BYTES);
    ok_sim_tick(sim);
    if (++p->sub == OK_TURN_TICKS) {
        p->sub = 0;
        s->held = 0;
        p->next++;
    }
    return 1;
}

uint32_t ok_peer_turns(const OkPeer *p) { return p ? p->next : 0; }
