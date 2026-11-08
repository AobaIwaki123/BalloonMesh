#include "balloon_protocol.h"

#include <string.h>

static void write_u16_le(uint8_t *out, uint16_t value) {
    out[0] = (uint8_t)(value & 0xFFu);
    out[1] = (uint8_t)((value >> 8) & 0xFFu);
}

static uint16_t read_u16_le(const uint8_t *data) {
    return (uint16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8));
}

static void write_u32_le(uint8_t *out, uint32_t value) {
    out[0] = (uint8_t)(value & 0xFFu);
    out[1] = (uint8_t)((value >> 8) & 0xFFu);
    out[2] = (uint8_t)((value >> 16) & 0xFFu);
    out[3] = (uint8_t)((value >> 24) & 0xFFu);
}

static uint32_t read_u32_le(const uint8_t *data) {
    return (uint32_t)data[0] |
           ((uint32_t)data[1] << 8) |
           ((uint32_t)data[2] << 16) |
           ((uint32_t)data[3] << 24);
}

uint64_t bm_tick40_from_unix_ms(int64_t unix_ms) {
    int64_t delta = unix_ms - BM_LED_TICK_ORIGIN_UNIX_MS;
    if (delta <= 0) {
        return 0;
    }
    return (uint64_t)(delta / 10);
}

int64_t bm_tick40_to_unix_ms(uint64_t tick) {
    return BM_LED_TICK_ORIGIN_UNIX_MS + (int64_t)(tick * 10u);
}

uint64_t bm_unpack_tick40_le(const uint8_t *p) {
    uint64_t value = 0;
    for (int i = 0; i < 5; ++i) {
        value |= ((uint64_t)p[i]) << (8 * i);
    }
    return value & 0xFFFFFFFFFFull;
}

void bm_pack_tick40_le(uint64_t tick, uint8_t *p) {
    for (int i = 0; i < 5; ++i) {
        p[i] = (uint8_t)((tick >> (8 * i)) & 0xFFu);
    }
}

bool bm_timestamp_is_older_or_equal(uint32_t a, uint32_t b) {
    return (int32_t)(a - b) <= 0;
}

size_t bm_encode_led_set(uint8_t *out, size_t out_len, const bm_led_set_t *value) {
    if (out == NULL || value == NULL || out_len < 8) {
        return 0;
    }
    bm_pack_tick40_le(value->execute_tick, out);
    out[5] = value->r;
    out[6] = value->g;
    out[7] = value->b;
    return 8;
}

bool bm_decode_led_set(const uint8_t *data, size_t len, bm_led_set_t *out) {
    if (data == NULL || out == NULL || len != 8) {
        return false;
    }
    out->execute_tick = bm_unpack_tick40_le(data);
    out->r = data[5];
    out->g = data[6];
    out->b = data[7];
    return true;
}

size_t bm_encode_led_cancel(uint8_t *out, size_t out_len, uint64_t execute_tick) {
    if (out == NULL || out_len < 5) {
        return 0;
    }
    bm_pack_tick40_le(execute_tick, out);
    return 5;
}

bool bm_decode_led_cancel(const uint8_t *data, size_t len, uint64_t *execute_tick) {
    if (data == NULL || execute_tick == NULL || len != 5) {
        return false;
    }
    *execute_tick = bm_unpack_tick40_le(data);
    return true;
}

size_t bm_encode_led_reset_all(uint8_t *out, size_t out_len, uint64_t cutoff_tick) {
    if (out == NULL || out_len < 5) {
        return 0;
    }
    bm_pack_tick40_le(cutoff_tick, out);
    return 5;
}

bool bm_decode_led_reset_all(const uint8_t *data, size_t len, uint64_t *cutoff_tick) {
    return bm_decode_led_cancel(data, len, cutoff_tick);
}

size_t bm_encode_led_set_now(uint8_t *out, size_t out_len, const bm_led_set_now_t *value) {
    if (out == NULL || value == NULL || out_len < 8) {
        return 0;
    }
    bm_pack_tick40_le(value->issued_tick, out);
    out[5] = value->r;
    out[6] = value->g;
    out[7] = value->b;
    return 8;
}

bool bm_decode_led_set_now(const uint8_t *data, size_t len, bm_led_set_now_t *out) {
    if (data == NULL || out == NULL || len != 8) {
        return false;
    }
    out->issued_tick = bm_unpack_tick40_le(data);
    out->r = data[5];
    out->g = data[6];
    out->b = data[7];
    return true;
}

size_t bm_encode_led_reset_and_set(uint8_t *out, size_t out_len, uint64_t cutoff_tick, bm_rgb_t rgb) {
    if (out == NULL || out_len < 8) {
        return 0;
    }
    bm_pack_tick40_le(cutoff_tick, out);
    out[5] = rgb.r;
    out[6] = rgb.g;
    out[7] = rgb.b;
    return 8;
}

bool bm_decode_led_reset_and_set(const uint8_t *data, size_t len, uint64_t *cutoff_tick, bm_rgb_t *rgb) {
    if (data == NULL || cutoff_tick == NULL || rgb == NULL || len != 8) {
        return false;
    }
    *cutoff_tick = bm_unpack_tick40_le(data);
    rgb->r = data[5];
    rgb->g = data[6];
    rgb->b = data[7];
    return true;
}

size_t bm_encode_ftm_report(uint8_t *out, size_t out_len, const bm_ftm_report_t *value) {
    if (out == NULL || value == NULL || out_len < 8) {
        return 0;
    }
    out[0] = value->anchor_id;
    write_u16_le(&out[1], value->distance_cm);
    write_u32_le(&out[3], value->timestamp);
    out[7] = value->batch_id;
    return 8;
}

bool bm_decode_ftm_report(const uint8_t *data, size_t len, bm_ftm_report_t *out) {
    if (data == NULL || out == NULL || len != 8) {
        return false;
    }
    out->anchor_id = data[0];
    out->distance_cm = read_u16_le(&data[1]);
    out->timestamp = read_u32_le(&data[3]);
    out->batch_id = data[7];
    return true;
}

size_t bm_encode_queue_status(uint8_t *out, size_t out_len, const bm_queue_status_t *value) {
    if (out == NULL || value == NULL || out_len < 3) {
        return 0;
    }
    out[0] = value->kind;
    write_u16_le(&out[1], value->queue_count);
    return 3;
}

bool bm_decode_queue_status(const uint8_t *data, size_t len, bm_queue_status_t *out) {
    if (data == NULL || out == NULL || len != 3) {
        return false;
    }
    out->kind = data[0];
    out->queue_count = read_u16_le(&data[1]);
    return true;
}

size_t bm_encode_neighbor_report(uint8_t *out, size_t out_len, const bm_neighbor_report_t *value) {
    if (out == NULL || value == NULL || out_len < 8 ||
        value->entry_count > BM_NEIGHBOR_REPORT_MAX_ENTRIES) {
        return 0;
    }
    out[0] = value->batch_id;
    out[1] = value->packet_index;
    out[2] = value->entry_count;
    memset(&out[3], 0, 6);
    for (uint8_t i = 0; i < value->entry_count; ++i) {
        write_u16_le(&out[3 + i * 3], value->entries[i].mesh_addr);
        out[3 + i * 3 + 2] = (uint8_t)value->entries[i].rssi_dbm;
    }
    return 8;
}

bool bm_decode_neighbor_report(const uint8_t *data, size_t len, bm_neighbor_report_t *out) {
    if (data == NULL || out == NULL || len != 8) {
        return false;
    }
    out->batch_id = data[0];
    out->packet_index = data[1];
    out->entry_count = data[2];
    if (out->entry_count > BM_NEIGHBOR_REPORT_MAX_ENTRIES) {
        return false;
    }
    for (uint8_t i = 0; i < BM_NEIGHBOR_REPORT_MAX_ENTRIES; ++i) {
        out->entries[i].mesh_addr = read_u16_le(&data[3 + i * 3]);
        out->entries[i].rssi_dbm = (int8_t)data[3 + i * 3 + 2];
    }
    return true;
}

size_t bm_encode_time_report(uint8_t *out, size_t out_len, int64_t utc_ms) {
    if (out == NULL || out_len < 8) {
        return 0;
    }
    uint64_t value = (uint64_t)utc_ms;
    for (int i = 0; i < 8; ++i) {
        out[i] = (uint8_t)((value >> (8 * i)) & 0xFFu);
    }
    return 8;
}

bool bm_decode_time_report(const uint8_t *data, size_t len, int64_t *utc_ms) {
    if (data == NULL || utc_ms == NULL || len != 8) {
        return false;
    }
    uint64_t value = 0;
    for (int i = 0; i < 8; ++i) {
        value |= ((uint64_t)data[i]) << (8 * i);
    }
    *utc_ms = (int64_t)value;
    return true;
}
