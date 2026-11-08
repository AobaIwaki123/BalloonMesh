#include "balloon_persistence.h"

#include <stdlib.h>

#include "esp_log.h"
#include "nvs.h"
#include "nvs_flash.h"

static const char *TAG = "persistence";
#define BM_NVS_NAMESPACE "bm_ledq"
#define BM_NVS_KEY "snapshot"

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

    uint8_t *buffer = (uint8_t *)malloc(length);
    if (buffer == NULL) {
        nvs_close(handle);
        return false;
    }
    err = nvs_get_blob(handle, BM_NVS_KEY, buffer, &length);
    nvs_close(handle);
    if (err != ESP_OK) {
        free(buffer);
        ESP_LOGW(TAG, "snapshot read failed: %s", esp_err_to_name(err));
        return false;
    }

    bool restored = bm_ledq_deserialize(queue, buffer, length);
    free(buffer);
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
    uint8_t *buffer = (uint8_t *)malloc(capacity);
    if (buffer == NULL) {
        return false;
    }
    size_t length = bm_ledq_serialize(queue, buffer, capacity);
    if (length == 0) {
        free(buffer);
        return false;
    }

    nvs_handle_t handle;
    esp_err_t err = nvs_open(BM_NVS_NAMESPACE, NVS_READWRITE, &handle);
    if (err == ESP_OK) {
        err = nvs_set_blob(handle, BM_NVS_KEY, buffer, length);
        if (err == ESP_OK) {
            err = nvs_commit(handle);
        }
        nvs_close(handle);
    }
    free(buffer);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "snapshot save failed: %s", esp_err_to_name(err));
        return false;
    }
    return true;
}
