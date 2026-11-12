#include "balloon_neighbor_link.h"

#include <string.h>

#include "esp_log.h"
#include "esp_random.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#include "balloon_mesh.h"
#include "balloon_neighbor.h"
#include "balloon_protocol.h"
#include "bm_config.h"

static const char *TAG = "neighbor";

typedef struct {
    SemaphoreHandle_t lock;
    bm_neighbor_collector_t collector;
    bool collecting;
    uint8_t batch_id;

    bool probing;
    uint8_t probes_sent;
    uint32_t next_probe_ms;
} bm_neighbor_link_state_t;

static bm_neighbor_link_state_t s_state;

static uint32_t now_ms(void) {
    return (uint32_t)(esp_timer_get_time() / 1000);
}

static void responder_task(void *argument) {
    (void)argument;
    for (;;) {
        bool send_probe = false;
        if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(50)) == pdTRUE) {
            uint32_t now = now_ms();
            if (s_state.probing &&
                (int32_t)(now - s_state.next_probe_ms) >= 0) {
                s_state.probes_sent++;
                send_probe = true;
                if (s_state.probes_sent >= BM_NEIGHBOR_PROBE_TX_COUNT) {
                    s_state.probing = false;
                } else {
                    s_state.next_probe_ms = now + 4000u + (esp_random() % 5000u);
                }
            }
            xSemaphoreGive(s_state.lock);
        }
        if (send_probe) {
            bm_balloon_mesh_send(BM_VENDOR_OPCODE(BM_OP_NEIGHBOR_PROBE),
                                 BM_GROUP_NEIGHBOR_PROBE, 0, NULL, 0);
        }
        vTaskDelay(pdMS_TO_TICKS(25));
    }
}

void bm_neighbor_link_init(void) {
    memset(&s_state, 0, sizeof(s_state));
    s_state.lock = xSemaphoreCreateMutex();
    bm_neighbor_init(&s_state.collector);
    xTaskCreate(responder_task, "neighbor_probe", 3072, NULL, 5, NULL);
}

bool bm_neighbor_link_begin_collection(uint32_t start_ms) {
    if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(100)) != pdTRUE) {
        return false;
    }
    bm_neighbor_begin(&s_state.collector, start_ms, BM_NEIGHBOR_PROBE_WINDOW_MS);
    s_state.collecting = true;
    xSemaphoreGive(s_state.lock);

    bool sent = bm_balloon_mesh_send(BM_VENDOR_OPCODE(BM_OP_NEIGHBOR_REQUEST),
                                     BM_GROUP_NEIGHBOR_PROBE, 0, NULL, 0);
    ESP_LOGI(TAG, "collection started (request sent=%d)", (int)sent);
    return sent;
}

void bm_neighbor_link_finish_collection(uint8_t batch_id) {
    bm_neighbor_result_t results[BM_NEIGHBOR_REPORT_COUNT];
    uint8_t count = 0;

    if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(100)) == pdTRUE) {
        bm_neighbor_end(&s_state.collector);
        count = bm_neighbor_results(&s_state.collector, results,
                                    BM_NEIGHBOR_REPORT_COUNT);
        s_state.collecting = false;
        xSemaphoreGive(s_state.lock);
    }

    for (uint8_t i = 0; i < count; i += BM_NEIGHBOR_REPORT_MAX_ENTRIES) {
        bm_neighbor_report_t report = {
            .batch_id = batch_id,
            .packet_index = (uint8_t)(i / BM_NEIGHBOR_REPORT_MAX_ENTRIES),
            .entry_count = (uint8_t)((count - i) > BM_NEIGHBOR_REPORT_MAX_ENTRIES
                                         ? BM_NEIGHBOR_REPORT_MAX_ENTRIES
                                         : (count - i)),
        };
        for (uint8_t j = 0; j < report.entry_count; ++j) {
            report.entries[j].mesh_addr = results[i + j].mesh_addr;
            report.entries[j].rssi_dbm = results[i + j].rssi_dbm;
        }
        uint8_t payload[8];
        if (bm_encode_neighbor_report(payload, sizeof(payload), &report) == 8) {
            bm_balloon_mesh_send(BM_VENDOR_OPCODE(BM_OP_NEIGHBOR_REPORT),
                                 BM_GROUP_NEIGHBOR_REPORT, 7, payload, 8);
        }
    }
    ESP_LOGI(TAG, "collection finished: %u neighbours in %u packet(s)",
             count, (unsigned)((count + 1) / 2));
}

void bm_neighbor_link_abort_collection(void) {
    if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(100)) == pdTRUE) {
        bm_neighbor_end(&s_state.collector);
        s_state.collecting = false;
        xSemaphoreGive(s_state.lock);
    }
}

void bm_neighbor_link_on_request(void) {
    if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(20)) != pdTRUE) {
        return;
    }
    if (!s_state.probing) {
        s_state.probing = true;
        s_state.probes_sent = 0;
        s_state.next_probe_ms = now_ms();
    }
    xSemaphoreGive(s_state.lock);
}

void bm_neighbor_link_on_probe(uint16_t src, int8_t rssi_dbm, uint8_t ttl) {
    if (ttl != 0) {
        return;
    }
    if (xSemaphoreTake(s_state.lock, pdMS_TO_TICKS(20)) != pdTRUE) {
        return;
    }
    if (s_state.collecting) {
        bm_neighbor_add(&s_state.collector, src, rssi_dbm, now_ms());
    }
    xSemaphoreGive(s_state.lock);
}
