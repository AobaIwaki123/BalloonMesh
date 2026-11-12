#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "balloon_led_queue.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Returns true when the status reached the mesh.  Failed sends are retried. */
typedef bool (*bm_queue_status_sender_t)(uint8_t kind, uint16_t queue_count);

typedef struct {
    uint16_t count;
    bm_rgb_t current;
    bool clock_synced;
    bool has_cutoff;
    uint64_t cutoff_tick;
    bool near_full;
} bm_led_controller_state_t;

void bm_led_controller_init(bm_queue_status_sender_t sender);

/* Handles a Mesh vendor opcode. Unknown opcodes are ignored. */
void bm_led_controller_handle_opcode(uint32_t opcode,
                                     const uint8_t *data,
                                     uint16_t length);

/* Called after an anchor time correction has been applied to the clock. */
void bm_led_controller_on_clock_sync(uint64_t old_tick,
                                     uint64_t new_tick,
                                     bool was_synced);

void bm_led_controller_get_state(bm_led_controller_state_t *out);

#ifdef __cplusplus
}
#endif
