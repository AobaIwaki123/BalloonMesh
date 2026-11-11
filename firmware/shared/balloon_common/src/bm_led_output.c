#include "bm_led_output.h"

#include <math.h>

#include "driver/ledc.h"
#include "esp_err.h"

#include "bm_config.h"

/* Maps commanded linear 0-255 values through (v/255)^gamma so the physical
 * brightness slider matches perception; see BM_LED_GAMMA in bm_config.h. */
static uint8_t s_gamma_lut[256];

static void init_gamma_lut(void) {
    for (int i = 0; i < 256; ++i) {
        float gamma = (float)BM_LED_GAMMA;
        float scaled;
        if (gamma > 0.0f) {
            scaled = 255.0f * powf((float)i / 255.0f, gamma) + 0.5f;
            if (scaled > 255.0f) {
                scaled = 255.0f;
            }
        } else {
            scaled = (float)i;
        }
        s_gamma_lut[i] = (uint8_t)scaled;
    }
}

static void configure_channel(ledc_channel_t channel, int gpio) {
    ledc_channel_config_t config = {
        .gpio_num = gpio,
        .speed_mode = LEDC_LOW_SPEED_MODE,
        .channel = channel,
        .intr_type = LEDC_INTR_DISABLE,
        .timer_sel = LEDC_TIMER_0,
        .duty = 0,
        .hpoint = 0,
    };
    ESP_ERROR_CHECK(ledc_channel_config(&config));
}

void bm_led_output_init(void) {
    init_gamma_lut();
    ledc_timer_config_t timer = {
        .speed_mode = LEDC_LOW_SPEED_MODE,
        .duty_resolution = (ledc_timer_bit_t)BM_LED_PWM_RESOLUTION,
        .timer_num = LEDC_TIMER_0,
        .freq_hz = BM_LED_PWM_FREQ_HZ,
        .clk_cfg = LEDC_AUTO_CLK,
    };
    ESP_ERROR_CHECK(ledc_timer_config(&timer));
    configure_channel(LEDC_CHANNEL_0, BM_LED_R_GPIO);
    configure_channel(LEDC_CHANNEL_1, BM_LED_G_GPIO);
    configure_channel(LEDC_CHANNEL_2, BM_LED_B_GPIO);
    bm_led_output_set(0, 0, 0);
}

static void set_channel(ledc_channel_t channel, uint8_t value) {
    uint32_t duty = ((uint32_t)value * ((1u << BM_LED_PWM_RESOLUTION) - 1u)) / 255u;
    ESP_ERROR_CHECK(ledc_set_duty(LEDC_LOW_SPEED_MODE, channel, duty));
    ESP_ERROR_CHECK(ledc_update_duty(LEDC_LOW_SPEED_MODE, channel));
}

void bm_led_output_set(uint8_t r, uint8_t g, uint8_t b) {
    set_channel(LEDC_CHANNEL_0, s_gamma_lut[r]);
    set_channel(LEDC_CHANNEL_1, s_gamma_lut[g]);
    set_channel(LEDC_CHANNEL_2, s_gamma_lut[b]);
}
