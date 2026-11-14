#include <Arduino.h>

#include "esp_log.h"
#include "esp_random.h"
#include "nvs_flash.h"

#include "balloon_clock.h"
#include "balloon_led_controller.h"
#include "bm_led_output.h"
#include "balloon_mesh.h"
#include "balloon_neighbor_link.h"
#include "balloon_time_link.h"
#include "bm_config.h"
#if BM_ENABLE_POSITIONING
#include "balloon_positioning.h"
#endif

static const char *TAG = "balloon";

static int vprintf_to_serial(const char *fmt, va_list args) {
    char buf[256];
    int len = vsnprintf(buf, sizeof(buf), fmt, args);
    if (len > 0) {
        Serial.print(buf);
    }
    return len;
}

void setup() {
    Serial.begin(115200);
    delay(500);
    esp_log_set_vprintf(vprintf_to_serial);
    esp_log_level_set("*", ESP_LOG_INFO);
    ESP_LOGI(TAG, "BalloonMesh balloon firmware %s starting...", BM_FIRMWARE_VERSION);

#if defined(BM_DEV_AUTO_PROV) && BM_DEV_AUTO_PROV
    ESP_LOGW(TAG, "BM_DEV_AUTO_PROV enabled: erasing NVS");
    ESP_ERROR_CHECK(nvs_flash_erase());
#endif

    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(err);

    bm_clock_init();
    bm_led_output_init();
    bm_led_controller_init(bm_balloon_mesh_publish_queue_status);
    bm_neighbor_link_init();
    bm_time_link_init();
    bm_balloon_mesh_init();
#if BM_ENABLE_POSITIONING
    bm_positioning_start();
#else
    ESP_LOGI(TAG, "positioning disabled (BLE Mesh bring-up mode)");
#endif
}

void loop() {
    static uint32_t last = 0;
    static bool request_armed = false;
    static uint32_t request_at = 0;
    static uint32_t request_interval = 30000u;
    uint32_t now = millis();
    if (!request_armed) {
        request_armed = true;
        /* Per-device offset: the fleet powers up together. */
        request_at = now + 15000u + (uint32_t)(esp_random() % 15000u);
    }
    /* Ask the mesh for the time on its own schedule.  Every balloon is
     * unsynced right after a power-up, so a fixed 30 s interval put the whole
     * fleet's requests (and the replies to them) on the same broadcast group
     * at the same moments; back off while it stays unsynced. */
    if (!bm_clock_is_synced()) {
        if ((int32_t)(now - request_at) >= 0) {
            bm_time_link_request();
            if (request_interval < 240000u) {
                request_interval *= 2u;
            }
            request_at = now + request_interval + (uint32_t)(esp_random() % 10000u);
        }
    } else {
        request_interval = 30000u;
        request_at = now + request_interval;
    }
    if ((uint32_t)(now - last) >= 30000u) {
        last = now;
        bm_led_controller_state_t state = {};
        bm_led_controller_get_state(&state);
        ESP_LOGI(TAG, "addr=0x%04x provisioned=%d synced=%d queue=%u rgb=%u,%u,%u",
                 bm_balloon_mesh_primary_addr(),
                 (int)bm_balloon_mesh_is_provisioned(),
                 (int)state.clock_synced, state.count,
                 state.current.r, state.current.g, state.current.b);
    }
    delay(1000);
}
