#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void bm_clock_init(void);
bool bm_clock_is_synced(void);
int64_t bm_clock_now_mono_us(void);
int64_t bm_clock_now_ms(void);
uint64_t bm_clock_now_tick40(void);

/* Applies an absolute anchor time. Returns false when the value is invalid.
 * The old/new ticks are used by the LED queue to discard reservations crossed
 * by a forward correction without executing them. */
bool bm_clock_apply_anchor_time(int64_t utc_ms,
                                int64_t mono_us,
                                uint64_t *old_tick,
                                uint64_t *new_tick,
                                bool *was_synced);

#ifdef __cplusplus
}
#endif
