#include "test_framework.h"
#include "ok_sim.h"

#include <stdint.h>

/* A fixed script of spawns and moves, the same for every run. */
static OkSim *play_script(uint32_t seed, int ticks) {
    OkSim *s = ok_sim_create(seed, 64, 64);
    uint8_t c[OK_CMD_BYTES];
    for (int i = 0; i < 20; i++) {
        ok_cmd_spawn(c, i % 2, (4 + i) * OK_FIXED_ONE, (10 + i % 7) * OK_FIXED_ONE);
        ok_sim_push_command(s, c, OK_CMD_BYTES);
    }
    ok_sim_tick(s);
    for (int i = 0; i < 20; i++) {
        ok_cmd_move(c, i % 2, i, (50 - i) * OK_FIXED_ONE, (40 + i % 5) * OK_FIXED_ONE);
        ok_sim_push_command(s, c, OK_CMD_BYTES);
    }
    for (int t = 0; t < ticks; t++) ok_sim_tick(s);
    return s;
}

TEST(two_sims_fed_the_same_commands_agree) {
    OkSim *a = play_script(7, 300);
    OkSim *b = play_script(7, 300);
    ASSERT(ok_sim_hash(a) == ok_sim_hash(b));
    ok_sim_destroy(a);
    ok_sim_destroy(b);
}

TEST(a_different_seed_is_a_different_game) {
    OkSim *a = play_script(7, 10);
    OkSim *b = play_script(8, 10);
    ASSERT(ok_sim_hash(a) != ok_sim_hash(b));
    ok_sim_destroy(a);
    ok_sim_destroy(b);
}

/* The hash every platform must print for the script. CI runs this on
 * Windows, Linux and the browser build; a change here is a change to
 * the rules and moves the pin on purpose. */
TEST(the_script_hash_is_pinned) {
    OkSim *s = play_script(7, 300);
    uint64_t h = ok_sim_hash(s);
    printf("(hash %016llx) ", (unsigned long long)h);
    ASSERT(h == 0xa0eb36e86c93af9eull);
    ok_sim_destroy(s);
}

TEST(a_unit_walks_to_its_goal_and_says_so) {
    OkSim *s = ok_sim_create(1, 32, 32);
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_spawn(c, 0, 2 * OK_FIXED_ONE, 2 * OK_FIXED_ONE);
    ASSERT_EQ_INT(0, ok_sim_push_command(s, c, OK_CMD_BYTES));
    ok_sim_tick(s);
    ok_cmd_move(c, 0, 0, 10 * OK_FIXED_ONE, 2 * OK_FIXED_ONE);
    ok_sim_push_command(s, c, OK_CMD_BYTES);
    for (int t = 0; t < 100; t++) ok_sim_tick(s);

    OkUnitView v;
    ASSERT_EQ_INT(1, ok_sim_snapshot(s, &v, 1));
    ASSERT_EQ_INT(10 * OK_FIXED_ONE, v.x);
    ASSERT_EQ_INT(OK_UNIT_IDLE, v.state);

    OkEvent ev[8];
    int n = ok_sim_drain_events(s, ev, 8);
    ASSERT_EQ_INT(2, n);
    ASSERT_EQ_INT(OK_EVENT_SPAWNED, ev[0].kind);
    ASSERT_EQ_INT(OK_EVENT_ARRIVED, ev[1].kind);
    ASSERT_EQ_INT(0, ok_sim_drain_events(s, ev, 8));
    ok_sim_destroy(s);
}

TEST(a_player_cannot_move_another_players_unit) {
    OkSim *s = ok_sim_create(1, 32, 32);
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_spawn(c, 0, OK_FIXED_ONE, OK_FIXED_ONE);
    ok_sim_push_command(s, c, OK_CMD_BYTES);
    ok_sim_tick(s);
    ok_cmd_move(c, 1, 0, 20 * OK_FIXED_ONE, OK_FIXED_ONE);
    ok_sim_push_command(s, c, OK_CMD_BYTES);
    ok_sim_tick(s);
    OkUnitView v;
    ok_sim_snapshot(s, &v, 1);
    ASSERT_EQ_INT(OK_UNIT_IDLE, v.state);
    ok_sim_destroy(s);
}

TEST(bytes_that_are_not_a_command_are_refused) {
    OkSim *s = ok_sim_create(1, 8, 8);
    uint8_t junk[OK_CMD_BYTES] = { 99 };
    ASSERT_EQ_INT(-1, ok_sim_push_command(s, junk, OK_CMD_BYTES));
    ASSERT_EQ_INT(-1, ok_sim_push_command(s, junk, 3));
    ASSERT_EQ_INT(OK_SIM_ABI_VERSION, (int)ok_sim_abi_version());
    ok_sim_destroy(s);
}

int main(void) {
    TEST_SUITE("ok_sim");
    RUN(two_sims_fed_the_same_commands_agree);
    RUN(a_different_seed_is_a_different_game);
    RUN(the_script_hash_is_pinned);
    RUN(a_unit_walks_to_its_goal_and_says_so);
    RUN(a_player_cannot_move_another_players_unit);
    RUN(bytes_that_are_not_a_command_are_refused);
    TEST_REPORT();
}
