#pragma once

#include <stdbool.h>

#include "balloon_led_queue.h"

#ifdef __cplusplus
extern "C" {
#endif

bool bm_persistence_load_queue(bm_led_queue_t *queue);
bool bm_persistence_save_queue(const bm_led_queue_t *queue);

#ifdef __cplusplus
}
#endif
