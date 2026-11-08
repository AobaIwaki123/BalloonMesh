#include "balloon_clock.h"

#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/portmacro.h"

#include "balloon_protocol.h"

typedef struct {
    portMUX_TYPE lock;
    bool synced;
    int64_t base_utc_ms;
    int64_t base_mono_us;
} bm_clock_state_t;

static bm_clock_state_t s_clock = {
    .lock = portMUX_INITIALIZER_UNLOCKED,
    .synced = false,
    .base_utc_ms = 0,
    .base_mono_us = 0,
};

void bm_clock_init(void) {
    portENTER_CRITICAL(&s_clock.lock);
    s_clock.synced = false;
    s_clock.base_utc_ms = 0;
    s_clock.base_mono_us = 0;
    portEXIT_CRITICAL(&s_clock.lock);
}

int64_t bm_clock_now_mono_us(void) {
    return esp_timer_get_time();
}

bool bm_clock_is_synced(void) {
    bool synced;
    portENTER_CRITICAL(&s_clock.lock);
    synced = s_clock.synced;
    portEXIT_CRITICAL(&s_clock.lock);
    return synced;
}

int64_t bm_clock_now_ms(void) {
    portENTER_CRITICAL(&s_clock.lock);
    bool synced = s_clock.synced;
    int64_t base_utc_ms = s_clock.base_utc_ms;
    int64_t base_mono_us = s_clock.base_mono_us;
    portEXIT_CRITICAL(&s_clock.lock);
    if (!synced) {
        return 0;
    }
    int64_t elapsed_us = esp_timer_get_time() - base_mono_us;
    return base_utc_ms + elapsed_us / 1000;
}

uint64_t bm_clock_now_tick40(void) {
    return bm_tick40_from_unix_ms(bm_clock_now_ms());
}

bool bm_clock_apply_anchor_time(int64_t utc_ms,
                                int64_t mono_us,
                                uint64_t *old_tick,
                                uint64_t *new_tick,
                                bool *was_synced) {
    if (utc_ms < BM_LED_TICK_ORIGIN_UNIX_MS) {
        return false;
    }
    uint64_t previous_tick = 0;
    bool previous_synced = false;

    portENTER_CRITICAL(&s_clock.lock);
    previous_synced = s_clock.synced;
    if (previous_synced) {
        int64_t elapsed_us = mono_us - s_clock.base_mono_us;
        previous_tick = bm_tick40_from_unix_ms(s_clock.base_utc_ms + elapsed_us / 1000);
    }
    s_clock.synced = true;
    s_clock.base_utc_ms = utc_ms;
    s_clock.base_mono_us = mono_us;
    portEXIT_CRITICAL(&s_clock.lock);

    if (old_tick != NULL) {
        *old_tick = previous_tick;
    }
    if (new_tick != NULL) {
        *new_tick = bm_tick40_from_unix_ms(utc_ms);
    }
    if (was_synced != NULL) {
        *was_synced = previous_synced;
    }
    return true;
}
