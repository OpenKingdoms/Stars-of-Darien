#include "test_framework.h"
#include "ok_sim.h"

#include <stdint.h>

#define F(cells) ((int32_t)((cells) * OK_FIXED_ONE))

static void spawn(OkSim *s, int player, int kind, double x, double y) {
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_spawn(c, player, kind, F(x), F(y));
    ok_sim_push_command(s, c, OK_CMD_BYTES);
}

static void move(OkSim *s, int player, int unit, double x, double y) {
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_move(c, player, unit, F(x), F(y));
    ok_sim_push_command(s, c, OK_CMD_BYTES);
}

static void attack(OkSim *s, int player, int unit, int target) {
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_attack(c, player, unit, target);
    ok_sim_push_command(s, c, OK_CMD_BYTES);
}

static OkUnitView view_of(OkSim *s, int id) {
    static OkUnitView v[OK_SIM_MAX_UNITS];
    ok_sim_snapshot(s, v, OK_SIM_MAX_UNITS);
    return v[id];
}

/* A wall down x = 30 with a gap at y 30 to 33. */
static void build_wall(OkSim *s) {
    for (int y = 0; y < 64; y++)
        if (y < 30 || y > 33) ok_sim_set_blocked(s, 30, y, 1);
}

/* A fixed script of spawns and moves through the gap, far from any
 * enemy, the same for every run. */
static OkSim *play_script(uint32_t seed, int ticks) {
    OkSim *s = ok_sim_create(seed, 64, 64);
    build_wall(s);
    for (int i = 0; i < 10; i++) spawn(s, 0, i % 2, 4 + i, 10 + i % 7);
    for (int i = 0; i < 10; i++) spawn(s, 1, i % 2, 50 + i % 3, 50 + i);
    ok_sim_tick(s);
    for (int i = 0; i < 10; i++) move(s, 0, i, 50 - i, 10 + i % 5);
    for (int i = 0; i < 10; i++) move(s, 1, 10 + i, 40 + i, 60);
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
 * Windows and Linux, and the Unity tests check the same value through
 * the plugin. A change here is a change to the rules and moves the pin
 * on purpose. */
TEST(the_script_hash_is_pinned) {
    OkSim *s = play_script(7, 300);
    uint64_t h = ok_sim_hash(s);
    printf("(hash %016llx) ", (unsigned long long)h);
    ASSERT(h == 0xa7d1bdd26c061facull);
    ok_sim_destroy(s);
}

TEST(a_unit_walks_to_its_goal_and_says_so) {
    OkSim *s = ok_sim_create(1, 32, 32);
    spawn(s, 0, OK_KIND_SOLDIER, 2, 2);
    ok_sim_tick(s);
    move(s, 0, 0, 10, 2);
    for (int t = 0; t < 200; t++) ok_sim_tick(s);

    OkUnitView v = view_of(s, 0);
    ASSERT_EQ_INT(F(10), v.x);
    ASSERT_EQ_INT(OK_UNIT_IDLE, v.state);

    OkEvent ev[8];
    int n = ok_sim_drain_events(s, ev, 8);
    ASSERT_EQ_INT(2, n);
    ASSERT_EQ_INT(OK_EVENT_SPAWNED, ev[0].kind);
    ASSERT_EQ_INT(OK_EVENT_ARRIVED, ev[1].kind);
    ASSERT_EQ_INT(0, ok_sim_drain_events(s, ev, 8));
    ok_sim_destroy(s);
}

TEST(a_unit_walks_around_a_wall_and_never_stands_in_it) {
    OkSim *s = ok_sim_create(1, 64, 64);
    build_wall(s);
    spawn(s, 0, OK_KIND_SOLDIER, 20.5, 5.5);
    ok_sim_tick(s);
    move(s, 0, 0, 40.5, 5.5);
    int t;
    for (t = 0; t < 2000; t++) {
        ok_sim_tick(s);
        OkUnitView v = view_of(s, 0);
        ASSERT(!ok_sim_is_blocked(s, v.x / OK_FIXED_ONE, v.y / OK_FIXED_ONE));
        if (v.state == OK_UNIT_IDLE) break;
    }
    OkUnitView v = view_of(s, 0);
    ASSERT_EQ_INT(F(40.5), v.x);
    ASSERT_EQ_INT(F(5.5), v.y);
    /* Straight across is 20 cells. Round by the gap is over 50. */
    ASSERT(t > 400);
    ok_sim_destroy(s);
}

TEST(an_archer_shoots_from_range_without_closing_in) {
    OkSim *s = ok_sim_create(3, 32, 32);
    spawn(s, 0, OK_KIND_ARCHER, 5, 5);
    spawn(s, 1, OK_KIND_SOLDIER, 10, 5);
    ok_sim_tick(s);
    attack(s, 0, 0, 1);
    ok_sim_tick(s);
    ok_sim_tick(s);
    OkUnitView a = view_of(s, 0), b = view_of(s, 1);
    ASSERT_EQ_INT(F(5), a.x);
    ASSERT_EQ_INT(OK_UNIT_ATTACKING, a.state);
    ASSERT(b.hp < b.max_hp);
    ok_sim_destroy(s);
}

TEST(a_soldier_chases_and_kills_and_the_death_is_reported) {
    OkSim *s = ok_sim_create(5, 32, 32);
    spawn(s, 0, OK_KIND_SOLDIER, 2, 2);
    spawn(s, 1, OK_KIND_ARCHER, 25, 20);
    ok_sim_tick(s);
    attack(s, 0, 0, 1);
    int died = 0, hits = 0;
    OkEvent ev[64];
    for (int t = 0; t < 3000 && !died; t++) {
        ok_sim_tick(s);
        int n = ok_sim_drain_events(s, ev, 64);
        for (int i = 0; i < n; i++) {
            if (ev[i].kind == OK_EVENT_ATTACKED && ev[i].unit == 0) hits++;
            if (ev[i].kind == OK_EVENT_DIED) {
                ASSERT_EQ_INT(1, ev[i].unit);
                ASSERT_EQ_INT(0, ev[i].a);
                ASSERT_EQ_INT(1, ev[i].b);
                died = 1;
            }
        }
    }
    ASSERT(died);
    ASSERT(hits >= 4);
    ASSERT_EQ_INT(OK_UNIT_DEAD, view_of(s, 1).state);
    ASSERT_EQ_INT(0, ok_sim_winner(s));
    ok_sim_destroy(s);
}

TEST(stop_drops_a_move) {
    OkSim *s = ok_sim_create(1, 32, 32);
    spawn(s, 0, OK_KIND_SOLDIER, 2, 2);
    ok_sim_tick(s);
    move(s, 0, 0, 30, 2);
    for (int t = 0; t < 10; t++) ok_sim_tick(s);
    uint8_t c[OK_CMD_BYTES];
    ok_cmd_stop(c, 0, 0);
    ok_sim_push_command(s, c, OK_CMD_BYTES);
    ok_sim_tick(s);
    int32_t x = view_of(s, 0).x;
    for (int t = 0; t < 10; t++) ok_sim_tick(s);
    ASSERT_EQ_INT(OK_UNIT_IDLE, view_of(s, 0).state);
    ASSERT_EQ_INT(x, view_of(s, 0).x);
    ok_sim_destroy(s);
}

TEST(a_player_cannot_order_another_players_unit_or_attack_its_own) {
    OkSim *s = ok_sim_create(1, 32, 32);
    spawn(s, 0, OK_KIND_SOLDIER, 1, 1);
    spawn(s, 0, OK_KIND_SOLDIER, 3, 1);
    ok_sim_tick(s);
    move(s, 1, 0, 20, 1);
    attack(s, 0, 0, 1);
    ok_sim_tick(s);
    OkUnitView v = view_of(s, 0);
    ASSERT_EQ_INT(OK_UNIT_IDLE, v.state);
    ASSERT_EQ_INT(-1, v.target);
    ok_sim_destroy(s);
}

TEST(a_spawn_on_a_blocked_cell_is_refused) {
    OkSim *s = ok_sim_create(1, 16, 16);
    ok_sim_set_blocked(s, 4, 4, 1);
    spawn(s, 0, OK_KIND_SOLDIER, 4.5, 4.5);
    ok_sim_tick(s);
    ASSERT_EQ_INT(0, ok_sim_snapshot(s, NULL, 0));
    ok_sim_destroy(s);
}

TEST(bytes_that_are_not_a_command_are_refused) {
    OkSim *s = ok_sim_create(1, 8, 8);
    uint8_t junk[OK_CMD_BYTES] = { 99 };
    ASSERT_EQ_INT(-1, ok_sim_push_command(s, junk, OK_CMD_BYTES));
    ASSERT_EQ_INT(-1, ok_sim_push_command(s, junk, 3));
    ASSERT_EQ_INT(OK_SIM_ABI_VERSION, (int)ok_sim_abi_version());
    ASSERT_NULL(ok_sim_create(1, 0, 8));
    ASSERT_NULL(ok_sim_create(1, OK_SIM_MAX_SIDE + 1, 8));
    ok_sim_destroy(s);
}

/* Two armies on either side of the wall. Blue marches through the gap
 * and red waits, and whoever meets whom fights until one side is gone. */
static OkSim *play_battle(int *ticks_out, int *deaths_out) {
    OkSim *s = ok_sim_create(11, 64, 64);
    build_wall(s);
    for (int i = 0; i < 12; i++) spawn(s, 0, i % 3 == 2, 8 + i % 4, 26 + i / 4 * 2);
    for (int i = 0; i < 12; i++) spawn(s, 1, i % 3 == 2, 50 + i % 4, 28 + i / 4 * 2);
    ok_sim_tick(s);
    for (int i = 0; i < 12; i++) move(s, 0, i, 45 + i % 3, 28 + i / 3);
    int deaths = 0, t;
    OkEvent ev[256];
    for (t = 0; t < 9000 && ok_sim_winner(s) == -1; t++) {
        ok_sim_tick(s);
        int n = ok_sim_drain_events(s, ev, 256);
        for (int i = 0; i < n; i++) deaths += ev[i].kind == OK_EVENT_DIED;
    }
    *ticks_out = t;
    *deaths_out = deaths;
    return s;
}

TEST(a_headless_battle_ends_with_a_winner) {
    int ticks, deaths;
    OkSim *s = play_battle(&ticks, &deaths);
    int32_t w = ok_sim_winner(s);
    printf("(winner %d after %d ticks, %d dead) ", w, ticks, deaths);
    ASSERT(w == 0 || w == 1);
    ASSERT(ticks < 9000);
    /* Every dead unit was reported exactly once. */
    OkUnitView v[64];
    int n = ok_sim_snapshot(s, v, 64), dead = 0;
    for (int i = 0; i < n; i++) dead += v[i].state == OK_UNIT_DEAD;
    ASSERT_EQ_INT(dead, deaths);
    ASSERT(dead >= 12);
    ok_sim_destroy(s);
}

TEST(the_battle_hash_is_pinned) {
    int ticks, deaths;
    OkSim *s = play_battle(&ticks, &deaths);
    uint64_t h = ok_sim_hash(s);
    printf("(hash %016llx) ", (unsigned long long)h);
    ASSERT(h == 0x1431334bfd09ab0dull);
    ok_sim_destroy(s);
}

int main(void) {
    TEST_SUITE("ok_sim");
    RUN(two_sims_fed_the_same_commands_agree);
    RUN(a_different_seed_is_a_different_game);
    RUN(the_script_hash_is_pinned);
    RUN(a_unit_walks_to_its_goal_and_says_so);
    RUN(a_unit_walks_around_a_wall_and_never_stands_in_it);
    RUN(an_archer_shoots_from_range_without_closing_in);
    RUN(a_soldier_chases_and_kills_and_the_death_is_reported);
    RUN(stop_drops_a_move);
    RUN(a_player_cannot_order_another_players_unit_or_attack_its_own);
    RUN(a_spawn_on_a_blocked_cell_is_refused);
    RUN(bytes_that_are_not_a_command_are_refused);
    RUN(a_headless_battle_ends_with_a_winner);
    RUN(the_battle_hash_is_pinned);
    TEST_REPORT();
}
