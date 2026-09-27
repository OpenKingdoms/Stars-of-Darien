/* ok_path.c - the grid and its A* search. Written for this project. */
#include "ok_grid.h"
#include "ok_sim.h"

#include <string.h>

#define COST_STRAIGHT 10
#define COST_DIAGONAL 14

/* How far a walker keeps from blocked cells when a line is checked, in
 * fixed point. Under half a cell, so a path of neighbouring cells that
 * cuts no corner always passes. */
#define CLEARANCE (OK_FIXED_ONE * 3 / 10)

int ok_grid_init(OkGrid *g, int32_t w, int32_t h) {
    if (!g || w < 1 || h < 1 || w > OK_GRID_MAX_SIDE || h > OK_GRID_MAX_SIDE)
        return -1;
    g->w = w;
    g->h = h;
    memset(g->blocked, 0, sizeof g->blocked);
    return 0;
}

void ok_grid_set_blocked(OkGrid *g, int32_t cx, int32_t cy, int blocked) {
    if (cx < 0 || cy < 0 || cx >= g->w || cy >= g->h) return;
    g->blocked[cy * g->w + cx] = blocked ? 1 : 0;
}

int ok_grid_open(const OkGrid *g, int32_t cx, int32_t cy) {
    if (cx < 0 || cy < 0 || cx >= g->w || cy >= g->h) return 0;
    return !g->blocked[cy * g->w + cx];
}

/* Floor division by the fixed point one, right for negatives too. */
static int32_t cell_of(int32_t v) {
    return v >= 0 ? v / OK_FIXED_ONE : -((-v + OK_FIXED_ONE - 1) / OK_FIXED_ONE);
}

int ok_grid_open_at(const OkGrid *g, int32_t x, int32_t y) {
    return ok_grid_open(g, cell_of(x), cell_of(y));
}

static int box_open(const OkGrid *g, int32_t x, int32_t y) {
    return ok_grid_open_at(g, x - CLEARANCE, y - CLEARANCE)
        && ok_grid_open_at(g, x + CLEARANCE, y - CLEARANCE)
        && ok_grid_open_at(g, x - CLEARANCE, y + CLEARANCE)
        && ok_grid_open_at(g, x + CLEARANCE, y + CLEARANCE);
}

int ok_grid_line_open(const OkGrid *g, int32_t x0, int32_t y0,
                      int32_t x1, int32_t y1) {
    int64_t dx = (int64_t)x1 - x0, dy = (int64_t)y1 - y0;
    int64_t adx = dx < 0 ? -dx : dx, ady = dy < 0 ? -dy : dy;
    int64_t span = adx > ady ? adx : ady;
    /* A sample every quarter cell. */
    int64_t steps = span / (OK_FIXED_ONE / 4) + 1;
    for (int64_t i = 0; i <= steps; i++) {
        int32_t x = (int32_t)(x0 + dx * i / steps);
        int32_t y = (int32_t)(y0 + dy * i / steps);
        if (!box_open(g, x, y)) return 0;
    }
    return 1;
}

static int32_t octile(int32_t ax, int32_t ay, int32_t bx, int32_t by) {
    int32_t dx = ax > bx ? ax - bx : bx - ax;
    int32_t dy = ay > by ? ay - by : by - ay;
    int32_t lo = dx < dy ? dx : dy, hi = dx < dy ? dy : dx;
    return COST_STRAIGHT * hi + (COST_DIAGONAL - COST_STRAIGHT) * lo;
}

/* The open list is a binary heap of cells keyed by f, then h, then cell
 * index, so equal paths always resolve the same way. */
typedef struct {
    const OkGrid *g;
    OkPathScratch *s;
    int32_t gx, gy;
} Search;

static int32_t h_of(const Search *q, int32_t c) {
    return octile(c % q->g->w, c / q->g->w, q->gx, q->gy);
}

static int less(const Search *q, int32_t a, int32_t b) {
    int32_t ha = h_of(q, a), hb = h_of(q, b);
    int32_t fa = q->s->g[a] + ha, fb = q->s->g[b] + hb;
    if (fa != fb) return fa < fb;
    if (ha != hb) return ha < hb;
    return a < b;
}

static void heap_set(OkPathScratch *s, int32_t i, int32_t c) {
    s->heap[i] = c;
    s->heap_pos[c] = i;
}

static void sift_up(const Search *q, int32_t i) {
    OkPathScratch *s = q->s;
    int32_t c = s->heap[i];
    while (i > 0) {
        int32_t p = (i - 1) / 2;
        if (!less(q, c, s->heap[p])) break;
        heap_set(s, i, s->heap[p]);
        i = p;
    }
    heap_set(s, i, c);
}

static void sift_down(const Search *q, int32_t i) {
    OkPathScratch *s = q->s;
    int32_t c = s->heap[i];
    for (;;) {
        int32_t l = 2 * i + 1, r = l + 1, m = i;
        int32_t best = c;
        if (l < s->heap_n && less(q, s->heap[l], best)) { m = l; best = s->heap[l]; }
        if (r < s->heap_n && less(q, s->heap[r], best)) { m = r; }
        if (m == i) break;
        heap_set(s, i, s->heap[m]);
        i = m;
    }
    heap_set(s, i, c);
}

static int32_t heap_pop(const Search *q) {
    OkPathScratch *s = q->s;
    int32_t top = s->heap[0];
    s->heap_n--;
    if (s->heap_n > 0) {
        heap_set(s, 0, s->heap[s->heap_n]);
        sift_down(q, 0);
    }
    return top;
}

int32_t ok_path_find(const OkGrid *g, OkPathScratch *s,
                     int32_t sx, int32_t sy, int32_t gx, int32_t gy,
                     int32_t *out, int32_t cap) {
    static const int32_t DX[8] = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static const int32_t DY[8] = { 0, 0, 1, -1, 1, -1, 1, -1 };
    if (!g || !s || sx < 0 || sy < 0 || sx >= g->w || sy >= g->h) return 0;
    if (gx < 0) gx = 0;
    if (gy < 0) gy = 0;
    if (gx >= g->w) gx = g->w - 1;
    if (gy >= g->h) gy = g->h - 1;

    if (++s->stamp == 0) {
        memset(s->seen, 0, sizeof s->seen);
        memset(s->closed, 0, sizeof s->closed);
        s->stamp = 1;
    }
    Search q = { g, s, gx, gy };
    const int32_t w = g->w;
    const int32_t start = sy * w + sx, goal = gy * w + gx;

    s->heap_n = 0;
    s->seen[start] = s->stamp;
    s->g[start] = 0;
    s->parent[start] = -1;
    heap_set(s, s->heap_n++, start);

    int32_t best = start, best_h = h_of(&q, start);
    while (s->heap_n > 0) {
        int32_t c = heap_pop(&q);
        s->closed[c] = s->stamp;
        int32_t hc = h_of(&q, c);
        if (hc < best_h || (hc == best_h && s->g[c] < s->g[best])) {
            best = c;
            best_h = hc;
        }
        if (c == goal) break;
        int32_t cx = c % w, cy = c / w;
        for (int k = 0; k < 8; k++) {
            int32_t nx = cx + DX[k], ny = cy + DY[k];
            if (!ok_grid_open(g, nx, ny)) continue;
            if (k >= 4 && (!ok_grid_open(g, cx + DX[k], cy) ||
                           !ok_grid_open(g, cx, cy + DY[k])))
                continue;
            int32_t n = ny * w + nx;
            if (s->closed[n] == s->stamp) continue;
            int32_t ng = s->g[c] + (k >= 4 ? COST_DIAGONAL : COST_STRAIGHT);
            if (s->seen[n] != s->stamp) {
                s->seen[n] = s->stamp;
                s->g[n] = ng;
                s->parent[n] = c;
                heap_set(s, s->heap_n++, n);
                sift_up(&q, s->heap_n - 1);
            } else if (ng < s->g[n]) {
                s->g[n] = ng;
                s->parent[n] = c;
                sift_up(&q, s->heap_pos[n]);
            }
        }
    }

    int32_t len = 0;
    for (int32_t c = best; c != start; c = s->parent[c]) len++;
    int32_t keep = len < cap ? len : cap;
    /* Walk back from the end, skipping the tail that does not fit. */
    int32_t i = len;
    for (int32_t c = best; c != start; c = s->parent[c]) {
        i--;
        if (i < keep) out[i] = c;
    }
    return keep;
}

static int32_t centre(int32_t cell_coord) {
    return cell_coord * OK_FIXED_ONE + OK_FIXED_ONE / 2;
}

int32_t ok_path_smooth(const OkGrid *g, int32_t from_x, int32_t from_y,
                       int32_t *cells, int32_t n) {
    int32_t out = 0, i = 0;
    int32_t ax = from_x, ay = from_y;
    while (i < n) {
        /* The furthest cell still in a straight line of sight. */
        int32_t j = i;
        for (int32_t k = n - 1; k > i; k--) {
            int32_t c = cells[k];
            if (ok_grid_line_open(g, ax, ay, centre(c % g->w), centre(c / g->w))) {
                j = k;
                break;
            }
        }
        cells[out++] = cells[j];
        ax = centre(cells[j] % g->w);
        ay = centre(cells[j] / g->w);
        i = j + 1;
    }
    return out;
}
