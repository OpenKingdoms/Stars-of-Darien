#include "test_framework.h"
#include "ok_ai.h"
#include "ok_sim.h"
#include "ok_turn.h"

#include <stdint.h>
#include <string.h>

#define F(cells) ((int32_t)((cells) * OK_FIXED_ONE))
#define TURNS 400
#define DELAY_MAX 6

/* A made-up network for peer B: each frame arrives up to DELAY_MAX turns
 * late, some twice, in whatever order the delays give. */
typedef struct {
    int32_t  len;
    int32_t  due;
    uint8_t  bytes[OK_TURN_FRAME_MAX];
} Wire;

static Wire wire[TURNS * 2];
static int32_t wire_n;
static uint32_t net_rand = 12345;

static uint32_t net_draw(void) {
    net_rand = net_rand * 1103515245u + 12345u;
    return net_rand >> 16;
}

static void wire_send(const uint8_t *f, int32_t len, int32_t now) {
    int copies = net_draw() % 5 == 0 ? 2 : 1;
    for (int c = 0; c < copies && wire_n < TURNS * 2; c++) {
        Wire *w = &wire[wire_n++];
        w->len = len;
        w->due = now + (int32_t)(net_draw() % (DELAY_MAX + 1));
        memcpy(w->bytes, f, (size_t)len);
    }
}

static void wire_deliver(OkPeer *p, int32_t now) {
    for (int32_t i = 0; i < wire_n; i++) {
        if (wire[i].due > now || wire[i].len == 0) continue;
        ok_peer_receive(p, wire[i].bytes, wire[i].len);
        wire[i].len = 0;
    }
}

static void setup_map(OkSim *s) {
    for (int y = 0; y < 48; y++)
        if (y < 20 || y > 24) ok_sim_set_blocked(s, 24, y, 1);
}

/* The commands players send during turn t: spawns, then marches and
 * attacks. Seat 1 tries to forge a command as player 0 now and then. */
static void players_act(OkRelay *r, int t) {
    uint8_t c[OK_CMD_BYTES];
    if (t < 8) {
        ok_cmd_spawn(c, 0, t % 2, F(4 + t), F(10));
        ok_relay_command(r, 0, c, OK_CMD_BYTES);
        ok_cmd_spawn(c, 1, t % 2, F(40 + t % 4), F(30 + t / 4));
        ok_relay_command(r, 1, c, OK_CMD_BYTES);
    }
    if (t == 10)
        for (int u = 0; u < 16; u += 2) {
            ok_cmd_move(c, 0, u, F(36), F(28));
            ok_relay_command(r, 0, c, OK_CMD_BYTES);
        }
    if (t == 30) {
        ok_cmd_move(c, 0, 0, F(2), F(2));
        ok_relay_command(r, 1, c, OK_CMD_BYTES);
    }
    if (t == 50)
        for (int u = 1; u < 16; u += 2) {
            ok_cmd_attack(c, 1, u, 0);
            ok_relay_command(r, 1, c, OK_CMD_BYTES);
        }
}

TEST(two_peers_on_different_networks_stay_in_lockstep) {
    OkRelay *r = ok_relay_create();
    OkPeer *pa = ok_peer_create(), *pb = ok_peer_create();
    OkSim *a = ok_sim_create(9, 48, 48), *b = ok_sim_create(9, 48, 48);
    setup_map(a);
    setup_map(b);
    static uint64_t ha[TURNS], hb[TURNS];
    uint8_t frame[OK_TURN_FRAME_MAX];
    wire_n = 0;

    for (int t = 0; t < TURNS + DELAY_MAX + 2; t++) {
        if (t < TURNS) {
            players_act(r, t);
            int32_t len = ok_relay_close_turn(r, frame, sizeof frame);
            ASSERT(len >= OK_TURN_HEADER);
            ok_peer_receive(pa, frame, len);
            wire_send(frame, len, t);
        }
        wire_deliver(pb, t);
        /* Peer A runs as soon as it can. Peer B runs whatever has arrived. */
        for (int k = 0; k < OK_TURN_TICKS; k++) {
            uint32_t before = ok_peer_turns(pa);
            if (ok_peer_step(pa, a) && ok_peer_turns(pa) != before && before < TURNS) {
                ha[before] = ok_sim_hash(a);
                ASSERT_EQ_INT(0, ok_relay_report_hash(r, 0, before, ha[before]));
            }
        }
        for (;;) {
            uint32_t before = ok_peer_turns(pb);
            if (!ok_peer_step(pb, b)) break;
            if (ok_peer_turns(pb) != before && before < TURNS) {
                hb[before] = ok_sim_hash(b);
                ASSERT_EQ_INT(0, ok_relay_report_hash(r, 1, before, hb[before]));
            }
        }
    }
    ASSERT_EQ_INT(TURNS, (int)ok_peer_turns(pa));
    ASSERT_EQ_INT(TURNS, (int)ok_peer_turns(pb));
    for (int t = 0; t < TURNS; t++) ASSERT(ha[t] == hb[t]);
    ASSERT_EQ_INT(0, (int)ok_relay_desyncs(r));
    ASSERT_EQ_INT(TURNS * OK_TURN_TICKS, (int)ok_sim_tick_count(b));
    /* The game went somewhere: units spawned and some fought. */
    OkUnitView v[64];
    int n = ok_sim_snapshot(b, v, 64), hurt = 0;
    for (int i = 0; i < n; i++) hurt += v[i].hp < v[i].max_hp;
    ASSERT_EQ_INT(16, n);
    ASSERT(hurt > 0);

    ok_sim_destroy(a);
    ok_sim_destroy(b);
    ok_peer_destroy(pa);
    ok_peer_destroy(pb);
    ok_relay_destroy(r);
}

TEST(the_relay_stamps_the_sender_so_orders_cannot_be_forged) {
    OkRelay *r = ok_relay_create();
    uint8_t c[OK_CMD_BYTES], frame[OK_TURN_FRAME_MAX];
    ok_cmd_move(c, 0, 0, F(1), F(1));
    ASSERT_EQ_INT(0, ok_relay_command(r, 3, c, OK_CMD_BYTES));
    int32_t len = ok_relay_close_turn(r, frame, sizeof frame);
    ASSERT_EQ_INT(OK_TURN_HEADER + OK_CMD_BYTES, len);
    ASSERT_EQ_INT(3, frame[OK_TURN_HEADER + 1]);
    ASSERT_EQ_INT(-1, ok_relay_command(r, OK_TURN_SEATS, c, OK_CMD_BYTES));
    c[0] = 77;
    ASSERT_EQ_INT(-1, ok_relay_command(r, 0, c, OK_CMD_BYTES));
    ok_relay_destroy(r);
}

TEST(a_peer_waits_for_its_turn_and_ignores_repeats) {
    OkRelay *r = ok_relay_create();
    OkPeer *p = ok_peer_create();
    OkSim *s = ok_sim_create(1, 8, 8);
    uint8_t f0[OK_TURN_FRAME_MAX], f1[OK_TURN_FRAME_MAX];
    ASSERT_EQ_INT(0, ok_peer_step(p, s));
    int32_t l0 = ok_relay_close_turn(r, f0, sizeof f0);
    int32_t l1 = ok_relay_close_turn(r, f1, sizeof f1);
    ASSERT_EQ_INT(0, ok_peer_receive(p, f1, l1));
    ASSERT_EQ_INT(0, ok_peer_step(p, s));   /* turn 1 is no use without turn 0 */
    ASSERT_EQ_INT(0, ok_peer_receive(p, f0, l0));
    ASSERT_EQ_INT(0, ok_peer_receive(p, f0, l0));
    for (int k = 0; k < 2 * OK_TURN_TICKS; k++) ASSERT_EQ_INT(1, ok_peer_step(p, s));
    ASSERT_EQ_INT(0, ok_peer_step(p, s));
    ASSERT_EQ_INT(2, (int)ok_peer_turns(p));
    ASSERT_EQ_INT(0, ok_peer_receive(p, f0, l0));  /* old news */
    ASSERT_EQ_INT(-1, ok_peer_receive(p, f0, 5));  /* malformed */
    ok_sim_destroy(s);
    ok_peer_destroy(p);
    ok_relay_destroy(r);
}

TEST(more_commands_than_a_turn_holds_roll_into_the_next) {
    OkRelay *r = ok_relay_create();
    uint8_t c[OK_CMD_BYTES], frame[OK_TURN_FRAME_MAX];
    ok_cmd_stop(c, 0, 0);
    for (int i = 0; i < OK_TURN_MAX_CMDS + 5; i++)
        ASSERT_EQ_INT(0, ok_relay_command(r, 0, c, OK_CMD_BYTES));
    ASSERT_EQ_INT(OK_TURN_FRAME_MAX, ok_relay_close_turn(r, frame, sizeof frame));
    ASSERT_EQ_INT(OK_TURN_HEADER + 5 * OK_CMD_BYTES, ok_relay_close_turn(r, frame, sizeof frame));
    ASSERT_EQ_INT(OK_TURN_HEADER, ok_relay_close_turn(r, frame, sizeof frame));
    ASSERT_EQ_INT(3, (int)ok_relay_turns(r));
    ok_relay_destroy(r);
}

TEST(a_peer_that_strays_is_caught_by_the_relay) {
    OkRelay *r = ok_relay_create();
    OkPeer *pa = ok_peer_create(), *pb = ok_peer_create();
    OkSim *a = ok_sim_create(4, 16, 16), *b = ok_sim_create(4, 16, 16);
    uint8_t c[OK_CMD_BYTES], frame[OK_TURN_FRAME_MAX];
    ok_cmd_spawn(c, 0, OK_KIND_SOLDIER, F(2), F(2));
    ok_relay_command(r, 0, c, OK_CMD_BYTES);
    int caught = 0;
    for (int t = 0; t < 5; t++) {
        int32_t len = ok_relay_close_turn(r, frame, sizeof frame);
        ok_peer_receive(pa, frame, len);
        ok_peer_receive(pb, frame, len);
        /* B's host slips in a command that never went through the relay. */
        if (t == 2) {
            ok_cmd_move(c, 0, 0, F(9), F(9));
            ok_sim_push_command(b, c, OK_CMD_BYTES);
        }
        for (int k = 0; k < OK_TURN_TICKS; k++) {
            ok_peer_step(pa, a);
            ok_peer_step(pb, b);
        }
        ok_relay_report_hash(r, 0, (uint32_t)t, ok_sim_hash(a));
        caught |= ok_relay_report_hash(r, 1, (uint32_t)t, ok_sim_hash(b));
    }
    ASSERT(caught);
    ASSERT(ok_relay_desyncs(r) >= 1);
    ok_sim_destroy(a);
    ok_sim_destroy(b);
    ok_peer_destroy(pa);
    ok_peer_destroy(pb);
    ok_relay_destroy(r);
}

int main(void) {
    TEST_SUITE("ok_turn");
    RUN(two_peers_on_different_networks_stay_in_lockstep);
    RUN(the_relay_stamps_the_sender_so_orders_cannot_be_forged);
    RUN(a_peer_waits_for_its_turn_and_ignores_repeats);
    RUN(more_commands_than_a_turn_holds_roll_into_the_next);
    RUN(a_peer_that_strays_is_caught_by_the_relay);
    TEST_REPORT();
}
