/*
 * LED reservation queue for one balloon.
 *
 * Capacity policy and reset/cancel rules follow the hand-off specification:
 *   - 512 reservations, raw entry size 8 bytes (5-byte tick + RGB).
 *   - one reservation per execute tick; last received RGB wins.
 *   - when full, the reservation with the furthest future tick is displaced
 *     if the new command is earlier, otherwise the new command is dropped.
 *   - RESET cutoff is monotonic and rejects late reservations at or before it.
 *   - a small tombstone ring suppresses a cancelled tick that arrives late.
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "balloon_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

#define BM_LED_QUEUE_CAPACITY 512u
#define BM_LED_TOMBSTONE_CAPACITY 64u
#define BM_LED_FORWARD_JUMP_THRESHOLD_TICKS 100u /* 1 second */

typedef struct {
    uint8_t tick_le[5];
    uint8_t r;
    uint8_t g;
    uint8_t b;
} bm_led_entry_t;

typedef enum {
    BM_LEDQ_APPLIED = 0,
    BM_LEDQ_UPDATED,
    BM_LEDQ_IGNORED,
    BM_LEDQ_REJECTED_PAST,
    BM_LEDQ_REJECTED_CUTOFF,
    BM_LEDQ_REJECTED_EXECUTED,
    BM_LEDQ_REJECTED_CANCELLED,
    BM_LEDQ_REJECTED_FULL,
    BM_LEDQ_INVALID
} bm_ledq_result_t;

typedef struct {
    bm_led_entry_t entries[BM_LED_QUEUE_CAPACITY];
    uint16_t count;

    uint64_t cutoff_tick;
    bool has_cutoff;

    uint64_t last_now_tick;
    bool has_last_now;

    uint64_t last_executed_tick;
    bool has_last_executed;

    bm_rgb_t current;
    bool clock_synced;

    uint64_t tombstones[BM_LED_TOMBSTONE_CAPACITY];
    uint8_t tombstone_count;
    uint8_t tombstone_next;
} bm_led_queue_t;

typedef struct {
    bool near_full;
    bool initialized;
} bm_queue_monitor_t;

void bm_ledq_init(bm_led_queue_t *queue);
uint64_t bm_led_entry_tick(const bm_led_entry_t *entry);

bm_ledq_result_t bm_ledq_set(bm_led_queue_t *queue,
                             uint64_t execute_tick,
                             bm_rgb_t rgb,
                             uint64_t now_tick,
                             bool clock_synced);

bm_ledq_result_t bm_ledq_cancel(bm_led_queue_t *queue,
                                uint64_t execute_tick,
                                uint64_t now_tick,
                                bool clock_synced);

bm_ledq_result_t bm_ledq_reset(bm_led_queue_t *queue,
                               uint64_t cutoff_tick);

bm_ledq_result_t bm_ledq_reset_and_set(bm_led_queue_t *queue,
                                       uint64_t cutoff_tick,
                                       bm_rgb_t rgb);

bm_ledq_result_t bm_ledq_set_now(bm_led_queue_t *queue,
                                 uint64_t issued_tick,
                                 bm_rgb_t rgb);

/* Applies the first clock sync.  Past pending reservations are discarded. */
uint16_t bm_ledq_on_first_sync(bm_led_queue_t *queue, uint64_t now_tick);

/* Applies a resync.  A large forward jump discards crossed reservations. */
uint16_t bm_ledq_on_resync(bm_led_queue_t *queue,
                           uint64_t old_tick,
                           uint64_t new_tick);

/* Pops the earliest due reservation.  Returns true when an entry was executed. */
bool bm_ledq_take_due(bm_led_queue_t *queue,
                      uint64_t now_tick,
                      bm_led_entry_t *out);

/* Removes and reports the number of reservations at or before tick. */
uint16_t bm_ledq_discard_through(bm_led_queue_t *queue, uint64_t tick);

/* 1 = NEAR_FULL notification, 2 = RECOVERED notification, 0 = none. */
int bm_queue_monitor_update(bm_queue_monitor_t *monitor, uint16_t queue_count);

/* Fixed binary snapshot used by the balloon NVS persistence layer. */
size_t bm_ledq_serialized_size(void);
size_t bm_ledq_serialize(const bm_led_queue_t *queue, uint8_t *out, size_t out_len);
bool bm_ledq_deserialize(bm_led_queue_t *queue, const uint8_t *data, size_t length);

#ifdef __cplusplus
}
#endif
