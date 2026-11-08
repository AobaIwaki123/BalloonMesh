#include "balloon_time_link.h"

#include <string.h>

#include "esp_log.h"
#include "esp_timer.h"

#include "balloon_clock.h"
#include "balloon_led_controller.h"
#include "balloon_mesh.h"
#include "balloon_protocol.h"
#include "bm_config.h"

static const char *TAG = "time-link";

void bm_time_link_init(void) {
}

bool bm_time_link_request(void) {
    bool sent = bm_balloon_mesh_send(BM_VENDOR_OPCODE(BM_OP_TIME_REQUEST),
                                     BM_GROUP_TIME, 7, NULL, 0);
    ESP_LOGI(TAG, "time fallback request sent=%d", (int)sent);
    return sent;
}

void bm_time_link_on_request(void) {
    if (!bm_clock_is_synced()) {
        return;
    }
    int64_t utc_ms = bm_clock_now_ms();
    uint8_t payload[8];
    if (bm_encode_time_report(payload, sizeof(payload), utc_ms) == 8) {
        bm_balloon_mesh_send(BM_VENDOR_OPCODE(BM_OP_TIME_REPORT),
                             BM_GROUP_TIME, 7, payload, 8);
    }
}

void bm_time_link_on_report(const uint8_t *data, uint16_t length) {
    if (bm_clock_is_synced()) {
        return;
    }
    int64_t utc_ms = 0;
    if (!bm_decode_time_report(data, length, &utc_ms)) {
        return;
    }
    uint64_t old_tick = 0;
    uint64_t new_tick = 0;
    bool was_synced = false;
    if (bm_clock_apply_anchor_time(utc_ms, esp_timer_get_time(),
                                   &old_tick, &new_tick, &was_synced)) {
        bm_led_controller_on_clock_sync(old_tick, new_tick, was_synced);
        ESP_LOGI(TAG, "clock synced from Mesh peer");
    }
}
