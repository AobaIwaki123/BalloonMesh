#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void bm_led_output_init(void);
void bm_led_output_set(uint8_t r, uint8_t g, uint8_t b);

#ifdef __cplusplus
}
#endif
