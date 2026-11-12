#include "gateway_spool.h"

#include <stdlib.h>
#include <string.h>

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "nvs.h"

static const char *TAG = "gateway-spool";
#define BM_SPOOL_CAPACITY 128u
#define BM_SPOOL_HEADER 10u
#define BM_SPOOL_EVENT_BYTES 24u
#define BM_SPOOL_BLOB_MAX (BM_SPOOL_HEADER + BM_SPOOL_CAPACITY * BM_SPOOL_EVENT_BYTES)
#define BM_NVS_NAMESPACE "bm_gw"
#define BM_NVS_KEY "spool"

typedef struct {
    SemaphoreHandle_t lock;
    bm_gateway_event_t events[BM_SPOOL_CAPACITY];
    uint32_t sequences[BM_SPOOL_CAPACITY];
    uint32_t next_sequence;
    uint16_t head;
    uint16_t count;
    bool dirty;
    bool ready;
} bm_gateway_spool_t;

static bm_gateway_spool_t s_spool;

static void write_u16(uint8_t *out, uint16_t value) {
    out[0] = (uint8_t)(value & 0xFFu);
    out[1] = (uint8_t)((value >> 8) & 0xFFu);
}

static uint16_t read_u16(const uint8_t *data) {
    return (uint16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8));
}

static void write_u32(uint8_t *out, uint32_t value) {
    out[0] = (uint8_t)(value & 0xFFu);
    out[1] = (uint8_t)((value >> 8) & 0xFFu);
    out[2] = (uint8_t)((value >> 16) & 0xFFu);
    out[3] = (uint8_t)((value >> 24) & 0xFFu);
}

static uint32_t read_u32(const uint8_t *data) {
    return (uint32_t)data[0] |
           ((uint32_t)data[1] << 8) |
           ((uint32_t)data[2] << 16) |
           ((uint32_t)data[3] << 24);
}

static void encode_event(uint8_t *out, const bm_gateway_event_t *event) {
    memset(out, 0, BM_SPOOL_EVENT_BYTES);
    write_u16(&out[0], event->src);
    write_u16(&out[2], event->dst);
    write_u32(&out[4], event->opcode);
    out[8] = event->length;
    out[9] = (uint8_t)event->rssi;
    out[10] = event->ttl;
    memcpy(&out[12], event->payload, 8);
    write_u32(&out[20], event->received_ms);
}

static void decode_event(const uint8_t *data, bm_gateway_event_t *event) {
    memset(event, 0, sizeof(*event));
    event->src = read_u16(&data[0]);
    event->dst = read_u16(&data[2]);
    event->opcode = read_u32(&data[4]);
    event->length = data[8] > 8 ? 8 : data[8];
    event->rssi = (int8_t)data[9];
    event->ttl = data[10];
    memcpy(event->payload, &data[12], 8);
    event->received_ms = read_u32(&data[20]);
}

static void load_from_nvs(void) {
    nvs_handle_t handle;
    if (nvs_open(BM_NVS_NAMESPACE, NVS_READONLY, &handle) != ESP_OK) {
        return;
    }
    size_t length = 0;
    if (nvs_get_blob(handle, BM_NVS_KEY, NULL, &length) != ESP_OK ||
        length < BM_SPOOL_HEADER || length > BM_SPOOL_BLOB_MAX) {
        nvs_close(handle);
        return;
    }
    uint8_t *buffer = (uint8_t *)malloc(length);
    if (buffer == NULL) {
        nvs_close(handle);
        return;
    }
    if (nvs_get_blob(handle, BM_NVS_KEY, buffer, &length) == ESP_OK &&
        buffer[0] == 'G' && buffer[1] == 'S' && buffer[2] == 'P' && buffer[3] == '1') {
        uint16_t count = read_u16(&buffer[6]);
        if (count <= BM_SPOOL_CAPACITY &&
            length >= BM_SPOOL_HEADER + (size_t)count * BM_SPOOL_EVENT_BYTES) {
            for (uint16_t i = 0; i < count; ++i) {
                decode_event(&buffer[BM_SPOOL_HEADER + (size_t)i * BM_SPOOL_EVENT_BYTES],
                             &s_spool.events[i]);
                s_spool.sequences[i] = i;
            }
            s_spool.head = 0;
            s_spool.count = count;
            s_spool.next_sequence = count;
            ESP_LOGI(TAG, "restored %u pending events", count);
        }
    }
    free(buffer);
    nvs_close(handle);
}

static bool save_to_nvs(void) {
    uint8_t *buffer;
    size_t length;
    uint16_t count;
    /* Encode into the heap blob while holding the lock: a full snapshot
     * (128 * 24 B) would not fit on the save task's stack. */
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(200)) != pdTRUE) {
        return false;
    }
    count = s_spool.count;
    length = BM_SPOOL_HEADER + (size_t)count * BM_SPOOL_EVENT_BYTES;
    buffer = (uint8_t *)malloc(length);
    if (buffer == NULL) {
        s_spool.dirty = true;
        xSemaphoreGive(s_spool.lock);
        return false;
    }
    memset(buffer, 0, length);
    buffer[0] = 'G';
    buffer[1] = 'S';
    buffer[2] = 'P';
    buffer[3] = '1';
    buffer[4] = 1;
    write_u16(&buffer[6], count);
    write_u16(&buffer[8], 0);
    for (uint16_t i = 0; i < count; ++i) {
        encode_event(&buffer[BM_SPOOL_HEADER + (size_t)i * BM_SPOOL_EVENT_BYTES],
                     &s_spool.events[(s_spool.head + i) % BM_SPOOL_CAPACITY]);
    }
    s_spool.dirty = false;
    xSemaphoreGive(s_spool.lock);

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
        ESP_LOGW(TAG, "save failed: %s", esp_err_to_name(err));
        s_spool.dirty = true;
        return false;
    }
    return true;
}

static void save_task(void *argument) {
    (void)argument;
    for (;;) {
        vTaskDelay(pdMS_TO_TICKS(2000));
        bool dirty;
        if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(100)) == pdTRUE) {
            dirty = s_spool.dirty;
            xSemaphoreGive(s_spool.lock);
        } else {
            dirty = true;
        }
        if (dirty) {
            save_to_nvs();
        }
    }
}

void bm_gateway_spool_init(void) {
    memset(&s_spool, 0, sizeof(s_spool));
    s_spool.lock = xSemaphoreCreateMutex();
    load_from_nvs();
    s_spool.ready = true;
    xTaskCreate(save_task, "gateway_spool", 4096, NULL, 3, NULL);
}

void bm_gateway_spool_add(const bm_gateway_event_t *event) {
    if (!s_spool.ready || event == NULL) {
        return;
    }
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) != pdTRUE) {
        return;
    }
    if (s_spool.count >= BM_SPOOL_CAPACITY) {
        s_spool.head = (uint16_t)((s_spool.head + 1u) % BM_SPOOL_CAPACITY);
        s_spool.count--;
    }
    uint16_t tail = (uint16_t)((s_spool.head + s_spool.count) % BM_SPOOL_CAPACITY);
    s_spool.events[tail] = *event;
    s_spool.sequences[tail] = s_spool.next_sequence++;
    s_spool.count++;
    s_spool.dirty = true;
    xSemaphoreGive(s_spool.lock);
}

uint16_t bm_gateway_spool_count(void) {
    if (!s_spool.ready) {
        return 0;
    }
    uint16_t count = 0;
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) == pdTRUE) {
        count = s_spool.count;
        xSemaphoreGive(s_spool.lock);
    }
    return count;
}

bool bm_gateway_spool_peek(uint16_t index, bm_gateway_event_t *out) {
    return bm_gateway_spool_peek_seq(index, out, NULL);
}

bool bm_gateway_spool_peek_seq(uint16_t index, bm_gateway_event_t *out,
                               uint32_t *sequence) {
    if (!s_spool.ready || out == NULL) {
        return false;
    }
    bool ok = false;
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) == pdTRUE) {
        if (index < s_spool.count) {
            uint16_t slot = (uint16_t)((s_spool.head + index) % BM_SPOOL_CAPACITY);
            *out = s_spool.events[slot];
            if (sequence != NULL) {
                *sequence = s_spool.sequences[slot];
            }
            ok = true;
        }
        xSemaphoreGive(s_spool.lock);
    }
    return ok;
}

uint16_t bm_gateway_spool_snapshot(bm_gateway_event_t *events,
                                   uint32_t *sequences, uint16_t max) {
    if (!s_spool.ready || events == NULL || sequences == NULL || max == 0) {
        return 0;
    }
    uint16_t count = 0;
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) == pdTRUE) {
        count = s_spool.count > max ? max : s_spool.count;
        for (uint16_t i = 0; i < count; ++i) {
            uint16_t slot = (uint16_t)((s_spool.head + i) % BM_SPOOL_CAPACITY);
            events[i] = s_spool.events[slot];
            sequences[i] = s_spool.sequences[slot];
        }
        xSemaphoreGive(s_spool.lock);
    }
    return count;
}

void bm_gateway_spool_drop_through(uint32_t sequence) {
    if (!s_spool.ready) {
        return;
    }
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) != pdTRUE) {
        return;
    }
    while (s_spool.count > 0 &&
           (int32_t)(s_spool.sequences[s_spool.head] - sequence) <= 0) {
        s_spool.head = (uint16_t)((s_spool.head + 1u) % BM_SPOOL_CAPACITY);
        s_spool.count--;
    }
    s_spool.dirty = true;
    xSemaphoreGive(s_spool.lock);
}

void bm_gateway_spool_drop(uint16_t count) {
    if (!s_spool.ready || count == 0) {
        return;
    }
    if (xSemaphoreTake(s_spool.lock, pdMS_TO_TICKS(20)) != pdTRUE) {
        return;
    }
    if (count > s_spool.count) {
        count = s_spool.count;
    }
    s_spool.head = (uint16_t)((s_spool.head + count) % BM_SPOOL_CAPACITY);
    s_spool.count = (uint16_t)(s_spool.count - count);
    s_spool.dirty = true;
    xSemaphoreGive(s_spool.lock);
}
