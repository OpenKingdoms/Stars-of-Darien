/* ok_grid.h - the map as the pathfinder sees it, a grid of open and
 * blocked cells, and an A* search over it. Everything here is a pure
 * function of the grid and integer math, so every peer finds the same
 * path. Positions are 16.16 fixed point in cells, as in ok_sim.h. */
#ifndef OK_GRID_H
#define OK_GRID_H

#include <stdint.h>

#define OK_GRID_MAX_SIDE  256
#define OK_GRID_MAX_CELLS (OK_GRID_MAX_SIDE * OK_GRID_MAX_SIDE)

typedef struct OkGrid {
    int32_t w, h;
    uint8_t blocked[OK_GRID_MAX_CELLS];
} OkGrid;

/* Working memory for one search, reused between searches. Large, so it
 * lives inside the sim rather than on the stack. */
typedef struct OkPathScratch {
    uint32_t stamp;
    uint32_t seen[OK_GRID_MAX_CELLS];    /* == stamp: g and parent are set */
    uint32_t closed[OK_GRID_MAX_CELLS];  /* == stamp: expanded */
    int32_t  g[OK_GRID_MAX_CELLS];
    int32_t  parent[OK_GRID_MAX_CELLS];
    int32_t  heap_pos[OK_GRID_MAX_CELLS];
    int32_t  heap[OK_GRID_MAX_CELLS];
    int32_t  heap_n;
} OkPathScratch;

/* 0, or -1 when the size is out of range. Every cell starts open. */
int ok_grid_init(OkGrid *g, int32_t w, int32_t h);
void ok_grid_set_blocked(OkGrid *g, int32_t cx, int32_t cy, int blocked);
/* 1 for an open cell, 0 for a blocked one or one off the map. */
int ok_grid_open(const OkGrid *g, int32_t cx, int32_t cy);
/* The cell holding a fixed point position is open. */
int ok_grid_open_at(const OkGrid *g, int32_t x, int32_t y);

/* 1 when a walker with a small clearance can go straight from one fixed
 * point position to another without touching a blocked cell. */
int ok_grid_line_open(const OkGrid *g, int32_t x0, int32_t y0,
                      int32_t x1, int32_t y1);

/* A* over 8 neighbours, never cutting a blocked corner. Writes the cells
 * to walk, as y * w + x, from the first step to the last, and returns how
 * many. When the goal is blocked or cut off, the path ends at the reached
 * cell nearest to it. A path longer than cap keeps its first cap cells,
 * and the walker asks again when it gets there. 0 when already there. */
int32_t ok_path_find(const OkGrid *g, OkPathScratch *s,
                     int32_t sx, int32_t sy, int32_t gx, int32_t gy,
                     int32_t *out, int32_t cap);

/* Straighten a path found above into waypoints, keeping a cell only
 * where the straight line from the last kept point would be blocked.
 * from_x, from_y is the walker's fixed point position. Rewrites cells
 * in place and returns the new count. */
int32_t ok_path_smooth(const OkGrid *g, int32_t from_x, int32_t from_y,
                       int32_t *cells, int32_t n);

#endif /* OK_GRID_H */
