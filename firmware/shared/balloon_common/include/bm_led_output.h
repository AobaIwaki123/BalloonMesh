#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Shared RGB LED output for every XIAO ESP32C3 firmware project.
 * Pins and PWM settings come from bm_config.h:
 *   R = D0 / GPIO2, G = D1 / GPIO3, B = D2 / GPIO4
 *   5 kHz, 8 bit.
 */
void bm_led_output_init(void);
void bm_led_output_set(uint8_t r, uint8_t g, uint8_t b);

#ifdef __cplusplus
}
#endif
