#include "bm_led_output.h"

#include <math.h>

#include "driver/ledc.h"
#include "esp_err.h"
#include "esp_log.h"

#include "bm_config.h"

static const char *TAG = "led-out";

#define BM_LED_DUTY_MAX ((1u << BM_LED_PWM_RESOLUTION) - 1u)

/* Maps commanded linear 0-255 values through (v/255)^gamma so the physical
 * brightness slider matches perception; see BM_LED_GAMMA in bm_config.h.
 * The table is as wide as the PWM resolution, not 8 bit: with a 2.2 curve the
 * bottom of the range lands far below one 8-bit step, so a commanded dim
 * colour used to come out as "off".  Any non-zero command keeps at least one
 * duty step. */
static uint32_t s_gamma_lut[256];
static bool s_ready;

static void init_gamma_lut(void) {
    for (int i = 0; i < 256; ++i) {
        float gamma = (float)BM_LED_GAMMA;
        float scaled;
        if (gamma > 0.0f) {
            scaled = (float)BM_LED_DUTY_MAX * powf((float)i / 255.0f, gamma) + 0.5f;
        } else {
            scaled = (float)BM_LED_DUTY_MAX * ((float)i / 255.0f) + 0.5f;
        }
        if (scaled > (float)BM_LED_DUTY_MAX) {
            scaled = (float)BM_LED_DUTY_MAX;
        }
        uint32_t duty = (uint32_t)scaled;
        if (duty == 0u && i > 0) {
            duty = 1u;
        }
        s_gamma_lut[i] = duty;
    }
}

static bool configure_channel(ledc_channel_t channel, int gpio) {
    ledc_channel_config_t config = {
        .gpio_num = gpio,
        .speed_mode = LEDC_LOW_SPEED_MODE,
        .channel = channel,
        .intr_type = LEDC_INTR_DISABLE,
        .timer_sel = LEDC_TIMER_0,
        .duty = 0,
        .hpoint = 0,
    };
    esp_err_t err = ledc_channel_config(&config);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "LEDC channel %d (GPIO %d) failed: %s", (int)channel, gpio,
                 esp_err_to_name(err));
        return false;
    }
    return true;
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
    /* A failing LED driver must not panic the node: telemetry, time sync and
     * the queue stay useful, and the operator can see the fault in the log. */
    esp_err_t err = ledc_timer_config(&timer);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "LEDC timer config failed: %s", esp_err_to_name(err));
        return;
    }
    bool ok = configure_channel(LEDC_CHANNEL_0, BM_LED_R_GPIO);
    ok = configure_channel(LEDC_CHANNEL_1, BM_LED_G_GPIO) && ok;
    ok = configure_channel(LEDC_CHANNEL_2, BM_LED_B_GPIO) && ok;
    s_ready = ok;
    if (!s_ready) {
        return;
    }
    bm_led_output_set(0, 0, 0);
}

static void set_channel(ledc_channel_t channel, uint32_t duty) {
    esp_err_t err = ledc_set_duty(LEDC_LOW_SPEED_MODE, channel, duty);
    if (err == ESP_OK) {
        err = ledc_update_duty(LEDC_LOW_SPEED_MODE, channel);
    }
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "LEDC channel %d update failed: %s", (int)channel,
                 esp_err_to_name(err));
    }
}

void bm_led_output_set(uint8_t r, uint8_t g, uint8_t b) {
    if (!s_ready) {
        return;
    }
    set_channel(LEDC_CHANNEL_0, s_gamma_lut[r]);
    set_channel(LEDC_CHANNEL_1, s_gamma_lut[g]);
    set_channel(LEDC_CHANNEL_2, s_gamma_lut[b]);
}
