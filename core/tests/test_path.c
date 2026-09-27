#include "test_framework.h"
#include "ok_grid.h"
#include "ok_sim.h"

#include <stdlib.h>

static OkGrid grid;
static OkPathScratch scratch;
static int32_t cells[512];

static int adjacent(int32_t a, int32_t b, int32_t w) {
    int32_t dx = a % w - b % w, dy = a / w - b / w;
    return dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1 && (dx || dy);
}

TEST(an_open_grid_gives_a_straight_path) {
    ok_grid_init(&grid, 16, 16);
    int32_t n = ok_path_find(&grid, &scratch, 1, 1, 9, 1, cells, 512);
    ASSERT_EQ_INT(8, n);
    ASSERT_EQ_INT(1 * 16 + 9, cells[n - 1]);
    for (int32_t i = 0; i < n; i++) ASSERT_EQ_INT(1, cells[i] / 16);
}

TEST(a_path_goes_round_a_wall_one_step_at_a_time) {
    ok_grid_init(&grid, 16, 16);
    for (int y = 0; y < 12; y++) ok_grid_set_blocked(&grid, 8, y, 1);
    int32_t n = ok_path_find(&grid, &scratch, 2, 2, 14, 2, cells, 512);
    ASSERT(n > 12);
    ASSERT_EQ_INT(2 * 16 + 14, cells[n - 1]);
    ASSERT(adjacent(cells[0], 2 * 16 + 2, 16));
    for (int32_t i = 0; i < n; i++) {
        ASSERT(ok_grid_open(&grid, cells[i] % 16, cells[i] / 16));
        if (i > 0) ASSERT(adjacent(cells[i - 1], cells[i], 16));
    }
}

TEST(a_diagonal_never_cuts_a_blocked_corner) {
    ok_grid_init(&grid, 4, 4);
    ok_grid_set_blocked(&grid, 1, 0, 1);
    int32_t n = ok_path_find(&grid, &scratch, 0, 0, 1, 1, cells, 512);
    /* Down, then right, since (1,0) blocks the diagonal. */
    ASSERT_EQ_INT(2, n);
    ASSERT_EQ_INT(1 * 4 + 0, cells[0]);
    ASSERT_EQ_INT(1 * 4 + 1, cells[1]);
}

TEST(a_blocked_goal_ends_at_the_nearest_open_cell) {
    ok_grid_init(&grid, 16, 16);
    for (int y = 4; y <= 6; y++)
        for (int x = 4; x <= 6; x++) ok_grid_set_blocked(&grid, x, y, 1);
    int32_t n = ok_path_find(&grid, &scratch, 0, 5, 5, 5, cells, 512);
    ASSERT(n > 0);
    ASSERT_EQ_INT(5 * 16 + 3, cells[n - 1]);
}

TEST(a_sealed_goal_ends_as_close_as_it_can) {
    ok_grid_init(&grid, 16, 16);
    for (int y = 0; y < 16; y++) ok_grid_set_blocked(&grid, 8, y, 1);
    int32_t n = ok_path_find(&grid, &scratch, 2, 5, 12, 5, cells, 512);
    ASSERT(n > 0);
    ASSERT_EQ_INT(5 * 16 + 7, cells[n - 1]);
}

TEST(a_long_path_keeps_its_first_cells) {
    ok_grid_init(&grid, 64, 4);
    int32_t full = ok_path_find(&grid, &scratch, 0, 0, 63, 0, cells, 512);
    ASSERT_EQ_INT(63, full);
    int32_t n = ok_path_find(&grid, &scratch, 0, 0, 63, 0, cells, 10);
    ASSERT_EQ_INT(10, n);
    ASSERT_EQ_INT(1, cells[0]);
    ASSERT_EQ_INT(10, cells[9]);
}

TEST(smoothing_keeps_only_the_corners) {
    ok_grid_init(&grid, 16, 16);
    for (int y = 0; y < 12; y++) ok_grid_set_blocked(&grid, 8, y, 1);
    int32_t n = ok_path_find(&grid, &scratch, 2, 2, 14, 2, cells, 512);
    int32_t from_x = 2 * OK_FIXED_ONE + OK_FIXED_ONE / 2;
    int32_t from_y = from_x;
    int32_t m = ok_path_smooth(&grid, from_x, from_y, cells, n);
    ASSERT(m >= 2 && m < n);
    ASSERT_EQ_INT(2 * 16 + 14, cells[m - 1]);
    /* Every leg of the smoothed path is a clear straight line. */
    int32_t ax = from_x, ay = from_y;
    for (int32_t i = 0; i < m; i++) {
        int32_t bx = (cells[i] % 16) * OK_FIXED_ONE + OK_FIXED_ONE / 2;
        int32_t by = (cells[i] / 16) * OK_FIXED_ONE + OK_FIXED_ONE / 2;
        ASSERT(ok_grid_line_open(&grid, ax, ay, bx, by));
        ax = bx;
        ay = by;
    }
}

TEST(the_same_search_gives_the_same_path) {
    ok_grid_init(&grid, 32, 32);
    srand(3);
    for (int i = 0; i < 200; i++) ok_grid_set_blocked(&grid, rand() % 32, rand() % 32, 1);
    ok_grid_set_blocked(&grid, 0, 0, 0);
    int32_t first[512];
    int32_t n = ok_path_find(&grid, &scratch, 0, 0, 31, 31, first, 512);
    for (int k = 0; k < 3; k++) {
        int32_t m = ok_path_find(&grid, &scratch, 0, 0, 31, 31, cells, 512);
        ASSERT_EQ_INT(n, m);
        for (int32_t i = 0; i < n; i++) ASSERT_EQ_INT(first[i], cells[i]);
    }
}

int main(void) {
    TEST_SUITE("ok_path");
    RUN(an_open_grid_gives_a_straight_path);
    RUN(a_path_goes_round_a_wall_one_step_at_a_time);
    RUN(a_diagonal_never_cuts_a_blocked_corner);
    RUN(a_blocked_goal_ends_at_the_nearest_open_cell);
    RUN(a_sealed_goal_ends_as_close_as_it_can);
    RUN(a_long_path_keeps_its_first_cells);
    RUN(smoothing_keeps_only_the_corners);
    RUN(the_same_search_gives_the_same_path);
    TEST_REPORT();
}
