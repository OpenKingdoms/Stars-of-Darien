/* ok_turn.h - lockstep turns. A relay gathers every player's commands
 * and closes them into numbered turns on its own schedule. Each peer runs
 * a turn only once it holds that turn's frame, so every peer feeds its
 * sim the same commands on the same ticks and the hashes stay equal.
 *
 * Both halves are plain state machines with no sockets and no clock.
 * The host moves frames between them however it likes: a function call
 * in a single player game, a socket between machines. Frames may arrive
 * late, twice, or out of order.
 *
 * A turn frame, little endian:
 *   u32 turn, u16 count, u16 reserved, then count commands of
 *   OK_CMD_BYTES each, in the order the relay received them. */
#ifndef OK_TURN_H
#define OK_TURN_H

#include "ok_sim.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Sim ticks in one turn: 100 ms at 30 ticks a second. */
#define OK_TURN_TICKS      3
/* Commands carried by one turn. More wait for the next turn. */
#define OK_TURN_MAX_CMDS   64
#define OK_TURN_HEADER     8
#define OK_TURN_FRAME_MAX  (OK_TURN_HEADER + OK_TURN_MAX_CMDS * OK_CMD_BYTES)
/* Players a relay serves. The seat is the player number in the sim. */
#define OK_TURN_SEATS      8
/* How far ahead of its current turn a peer holds frames. */
#define OK_TURN_WINDOW     64

typedef struct OkRelay OkRelay;
typedef struct OkPeer OkPeer;

OK_API OkRelay *ok_relay_create(void);
OK_API void     ok_relay_destroy(OkRelay *relay);

/* A command from the player in seat for the open turn. The relay writes
 * the seat over the command's player byte, so nobody can give orders to
 * another player's units. 0, or -1 for a bad seat, bytes that are not a
 * command, or a full queue. */
OK_API int32_t  ok_relay_command(OkRelay *relay, int32_t seat,
                                 const uint8_t *bytes, int32_t len);

/* Close the open turn into out and open the next. Returns the frame
 * length, or 0 when cap is too small (the turn stays open). */
OK_API int32_t  ok_relay_close_turn(OkRelay *relay, uint8_t *out, int32_t cap);

/* Turns closed so far. */
OK_API uint32_t ok_relay_turns(const OkRelay *relay);

/* A peer's sim hash after it finished turn. 0 while everyone who
 * reported agrees, 1 when this hash differs from an earlier report for
 * the same turn. The relay keeps the last 16 turns. */
OK_API int32_t  ok_relay_report_hash(OkRelay *relay, int32_t seat,
                                     uint32_t turn, uint64_t hash);
/* Desyncs seen so far. */
OK_API uint32_t ok_relay_desyncs(const OkRelay *relay);

OK_API OkPeer  *ok_peer_create(void);
OK_API void     ok_peer_destroy(OkPeer *peer);

/* Hand the peer a frame from the relay. 0 when taken or when it is a
 * repeat of a turn already held or run, -1 when it is malformed or too
 * far ahead. */
OK_API int32_t  ok_peer_receive(OkPeer *peer, const uint8_t *frame, int32_t len);

/* Run one tick of sim. The first tick of a turn feeds the sim that
 * turn's commands. 1 when a tick ran, 0 when the peer is waiting for
 * the next turn's frame. */
OK_API int32_t  ok_peer_step(OkPeer *peer, OkSim *sim);

/* Turns this peer has fully run. After ok_peer_step finishes a turn,
 * this is the moment to report ok_sim_hash for turn ok_peer_turns - 1. */
OK_API uint32_t ok_peer_turns(const OkPeer *peer);

#ifdef __cplusplus
}
#endif

#endif /* OK_TURN_H */
