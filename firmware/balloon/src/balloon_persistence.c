#include "balloon_persistence.h"

#include "esp_log.h"
#include "nvs.h"
#include "nvs_flash.h"

static const char *TAG = "persistence";
#define BM_NVS_NAMESPACE "bm_ledq"
#define BM_NVS_KEY "snapshot"

/* The snapshot is close to 4 KB and is written on a timer for the whole show.
 * A static buffer keeps that off the heap, which also carries the BLE Mesh and
 * Wi-Fi stacks.  Load runs once from setup() and save only from the single
 * save task, so there is one user at a time. */
#define BM_SNAPSHOT_BUFFER_SIZE \
    (BM_LEDQ_SNAPSHOT_MAX_TOTAL_ENTRIES * 8u + 64u)
static uint8_t s_snapshot_buffer[BM_SNAPSHOT_BUFFER_SIZE];

bool bm_persistence_load_queue(bm_led_queue_t *queue) {
    if (queue == NULL) {
        return false;
    }
    nvs_handle_t handle;
    esp_err_t err = nvs_open(BM_NVS_NAMESPACE, NVS_READONLY, &handle);
    if (err != ESP_OK) {
        ESP_LOGI(TAG, "no saved LED queue (%s)", esp_err_to_name(err));
        return false;
    }

    size_t length = 0;
    err = nvs_get_blob(handle, BM_NVS_KEY, NULL, &length);
    if (err != ESP_OK || length == 0) {
        nvs_close(handle);
        ESP_LOGI(TAG, "LED queue snapshot not present");
        return false;
    }
    if (length > sizeof(s_snapshot_buffer)) {
        nvs_close(handle);
        ESP_LOGW(TAG, "snapshot too large (%u B); ignoring", (unsigned)length);
        return false;
    }

    err = nvs_get_blob(handle, BM_NVS_KEY, s_snapshot_buffer, &length);
    nvs_close(handle);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "snapshot read failed: %s", esp_err_to_name(err));
        return false;
    }

    bool restored = bm_ledq_deserialize(queue, s_snapshot_buffer, length);
    if (restored) {
        ESP_LOGI(TAG, "restored %u reservations", queue->count);
    } else {
        ESP_LOGW(TAG, "snapshot rejected (corrupt/incompatible)");
    }
    return restored;
}

bool bm_persistence_save_queue(const bm_led_queue_t *queue) {
    if (queue == NULL) {
        return false;
    }
    size_t capacity = bm_ledq_serialized_size();
    if (capacity > sizeof(s_snapshot_buffer)) {
        ESP_LOGE(TAG, "snapshot buffer too small (%u < %u)",
                 (unsigned)sizeof(s_snapshot_buffer), (unsigned)capacity);
        return false;
    }
    size_t length = bm_ledq_serialize(queue, s_snapshot_buffer, capacity);
    if (length == 0) {
        return false;
    }

    nvs_handle_t handle;
    esp_err_t err = nvs_open(BM_NVS_NAMESPACE, NVS_READWRITE, &handle);
    if (err == ESP_OK) {
        err = nvs_set_blob(handle, BM_NVS_KEY, s_snapshot_buffer, length);
        if (err == ESP_OK) {
            err = nvs_commit(handle);
        }
        nvs_close(handle);
    }
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "snapshot save failed: %s", esp_err_to_name(err));
        return false;
    }
    return true;
}
