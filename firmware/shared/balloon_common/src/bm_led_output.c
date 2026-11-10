#include "bm_led_output.h"

#include "driver/ledc.h"
#include "esp_err.h"

#include "bm_config.h"

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
    set_channel(LEDC_CHANNEL_0, r);
    set_channel(LEDC_CHANNEL_1, g);
    set_channel(LEDC_CHANNEL_2, b);
}
