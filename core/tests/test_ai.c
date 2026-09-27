#include "test_framework.h"
#include "ok_ai.h"
#include "ok_sim.h"
#include "ok_turn.h"

#include <stdint.h>

#define F(cells) ((int32_t)((cells) * OK_FIXED_ONE))

static int32_t count_alive(OkSim *s, int player) {
    static OkUnitView v[OK_SIM_MAX_UNITS];
    int32_t n = ok_sim_snapshot(s, v, OK_SIM_MAX_UNITS), alive = 0;
    for (int32_t i = 0; i < n; i++)
        alive += v[i].player == player && v[i].state != OK_UNIT_DEAD;
    return alive;
}

TEST(the_ai_raises_waves_on_schedule) {
    OkSim *s = ok_sim_create(1, 64, 64);
    OkAi *ai = ok_ai_create(1, 50, 50, 3);
    uint8_t buf[OK_CMD_BYTES * 256];
    int32_t sizes[3] = { 0, 0, 0 };
    for (int t = 0; t < OK_AI_FIRST_WAVE + 2 * OK_AI_WAVE_PERIOD + 10; t++) {
        int32_t before = ok_ai_waves_left(ai);
        int32_t n = ok_ai_think(ai, s, buf, sizeof buf);
        ASSERT_EQ_INT(0, n % OK_CMD_BYTES);
        for (int32_t i = 0; i < n; i += OK_CMD_BYTES) ok_sim_push_command(s, buf + i, OK_CMD_BYTES);
        if (ok_ai_waves_left(ai) != before) {
            int wave = 3 - before;
            ASSERT_EQ_INT(OK_AI_FIRST_WAVE + wave * OK_AI_WAVE_PERIOD, t);
            sizes[wave] = n / OK_CMD_BYTES;
        }
        ok_sim_tick(s);
    }
    ASSERT_EQ_INT(0, ok_ai_waves_left(ai));
    ASSERT_EQ_INT(OK_AI_WAVE_BASE, sizes[0]);
    ASSERT_EQ_INT(OK_AI_WAVE_BASE + 2, sizes[1]);
    ASSERT_EQ_INT(OK_AI_WAVE_BASE + 4, sizes[2]);
    ASSERT_EQ_INT(3 * OK_AI_WAVE_BASE + 6, count_alive(s, 1));
    ok_ai_destroy(ai);
    ok_sim_destroy(s);
}

TEST(the_ai_waves_overrun_a_small_garrison) {
    OkSim *s = ok_sim_create(2, 64, 64);
    OkAi *ai = ok_ai_create(1, 50, 50, 4);
    uint8_t c[OK_CMD_BYTES], buf[OK_CMD_BYTES * 256];
    for (int i = 0; i < 3; i++) {
        ok_cmd_spawn(c, 0, OK_KIND_SOLDIER, F(10 + i), F(10));
        ok_sim_push_command(s, c, OK_CMD_BYTES);
    }
    int t;
    for (t = 0; t < 20000; t++) {
        if (t % OK_TURN_TICKS == 0) {
            int32_t n = ok_ai_think(ai, s, buf, sizeof buf);
            for (int32_t i = 0; i < n; i += OK_CMD_BYTES) ok_sim_push_command(s, buf + i, OK_CMD_BYTES);
        }
        ok_sim_tick(s);
        if (t > 10 && count_alive(s, 0) == 0) break;
    }
    printf("(garrison gone at tick %d) ", t);
    ASSERT(t < 20000);
    ASSERT_EQ_INT(1, ok_sim_winner(s));
    ok_ai_destroy(ai);
    ok_sim_destroy(s);
}

/* Two computer players, each on its own peer, their commands carried by
 * the relay. The sims agree every turn and the game ends. */
TEST(ai_against_ai_through_the_relay_agrees_and_ends) {
    OkRelay *r = ok_relay_create();
    OkPeer *pa = ok_peer_create(), *pb = ok_peer_create();
    OkSim *a = ok_sim_create(21, 64, 64), *b = ok_sim_create(21, 64, 64);
    for (int y = 16; y < 48; y++) {
        ok_sim_set_blocked(a, 32, y, 1);
        ok_sim_set_blocked(b, 32, y, 1);
    }
    OkAi *ai0 = ok_ai_create(0, 8, 8, 2), *ai1 = ok_ai_create(1, 56, 56, 4);
    uint8_t buf[OK_CMD_BYTES * 256], frame[OK_TURN_FRAME_MAX];
    int turn;
    for (turn = 0; turn < 20000; turn++) {
        /* Each AI runs on its own machine and reads its own sim. */
        int32_t n = ok_ai_think(ai0, a, buf, sizeof buf);
        for (int32_t i = 0; i < n; i += OK_CMD_BYTES) ok_relay_command(r, 0, buf + i, OK_CMD_BYTES);
        n = ok_ai_think(ai1, b, buf, sizeof buf);
        for (int32_t i = 0; i < n; i += OK_CMD_BYTES) ok_relay_command(r, 1, buf + i, OK_CMD_BYTES);
        int32_t len = ok_relay_close_turn(r, frame, sizeof frame);
        ok_peer_receive(pa, frame, len);
        ok_peer_receive(pb, frame, len);
        for (int k = 0; k < OK_TURN_TICKS; k++) {
            ok_peer_step(pa, a);
            ok_peer_step(pb, b);
        }
        ASSERT(ok_sim_hash(a) == ok_sim_hash(b));
        if (ok_ai_waves_left(ai0) == 0 && ok_ai_waves_left(ai1) == 0 &&
            ok_sim_winner(a) != -1)
            break;
    }
    printf("(winner %d after %d turns) ", ok_sim_winner(a), turn);
    ASSERT(turn < 20000);
    ASSERT_EQ_INT(1, ok_sim_winner(a));
    ok_ai_destroy(ai0);
    ok_ai_destroy(ai1);
    ok_sim_destroy(a);
    ok_sim_destroy(b);
    ok_peer_destroy(pa);
    ok_peer_destroy(pb);
    ok_relay_destroy(r);
}

int main(void) {
    TEST_SUITE("ok_ai");
    RUN(the_ai_raises_waves_on_schedule);
    RUN(the_ai_waves_overrun_a_small_garrison);
    RUN(ai_against_ai_through_the_relay_agrees_and_ends);
    TEST_REPORT();
}
