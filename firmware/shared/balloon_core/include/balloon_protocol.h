/*
 * Balloon Mesh - transport-independent message codec.
 *
 * The Bluetooth Mesh Vendor Opcode carries the message kind.  The payloads in
 * this file are exactly the bytes that follow the 3-byte Vendor Opcode in a
 * non-segmented access message (maximum 8 bytes).
 *
 * Provisional values (see docs/decisions.md):
 *   Company ID        0x02E5 (Espressif)
 *   Message opcodes   0x01..0x0A
 *
 * These values are deliberately centralised here so that a production
 * deployment can change them without touching business logic.
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define BM_COMPANY_ID 0x02E5u
#define BM_NON_SEGMENTED_PAYLOAD_MAX 8u

#define BM_OP_LED_SET 0x01u
#define BM_OP_LED_CANCEL 0x02u
#define BM_OP_LED_RESET_ALL 0x03u
#define BM_OP_LED_SET_NOW 0x04u
#define BM_OP_LED_RESET_AND_SET 0x05u
#define BM_OP_QUEUE_STATUS 0x06u
#define BM_OP_FTM_REPORT 0x07u
#define BM_OP_NEIGHBOR_REQUEST 0x08u
#define BM_OP_NEIGHBOR_PROBE 0x09u
#define BM_OP_NEIGHBOR_REPORT 0x0Au

/* 24-bit Mesh vendor opcode.  This matches ESP-IDF's
 * ESP_BLE_MESH_MODEL_OP_3(op, company_id) representation. */
#define BM_VENDOR_OPCODE(op) \
    ((uint32_t)(0xC00000u | ((uint32_t)((uint8_t)(op) & 0x3Fu) << 16) | (uint32_t)(BM_COMPANY_ID)))

#define BM_QUEUE_STATUS_NEAR_FULL 0x01u
#define BM_QUEUE_STATUS_RECOVERED 0x02u

#define BM_FTM_TIMESTAMP_UNKNOWN 0xFFFFFFFFu

#define BM_LED_TICK_ORIGIN_UNIX_MS INT64_C(1735689600000) /* 2025-01-01T00:00:00Z */

typedef struct {
    uint8_t r;
    uint8_t g;
    uint8_t b;
} bm_rgb_t;

typedef struct {
    uint64_t execute_tick;
    uint8_t r;
    uint8_t g;
    uint8_t b;
} bm_led_set_t;

typedef struct {
    uint64_t cutoff_tick;
} bm_led_cutoff_t;

typedef struct {
    uint64_t issued_tick;
    uint8_t r;
    uint8_t g;
    uint8_t b;
} bm_led_set_now_t;

typedef struct {
    uint8_t anchor_id;
    uint16_t distance_cm;
    uint32_t timestamp;
    uint8_t batch_id;
} bm_ftm_report_t;

typedef struct {
    uint16_t mesh_addr;
    int8_t rssi_dbm;
} bm_neighbor_entry_t;

/* Two neighbour observations per packet is the default local decision. */
#define BM_NEIGHBOR_REPORT_MAX_ENTRIES 2u

typedef struct {
    uint8_t batch_id;
    uint8_t packet_index;
    uint8_t entry_count;
    bm_neighbor_entry_t entries[BM_NEIGHBOR_REPORT_MAX_ENTRIES];
} bm_neighbor_report_t;

typedef struct {
    uint8_t kind;
    uint16_t queue_count;
} bm_queue_status_t;

/* 40-bit LED tick helpers (2025-01-01 UTC, 10 ms units, little-endian). */
uint64_t bm_tick40_from_unix_ms(int64_t unix_ms);
int64_t bm_tick40_to_unix_ms(uint64_t tick);
uint64_t bm_unpack_tick40_le(const uint8_t *p);
void bm_pack_tick40_le(uint64_t tick, uint8_t *p);

/* LED commands. */
size_t bm_encode_led_set(uint8_t *out, size_t out_len, const bm_led_set_t *value);
bool bm_decode_led_set(const uint8_t *data, size_t len, bm_led_set_t *out);
size_t bm_encode_led_cancel(uint8_t *out, size_t out_len, uint64_t execute_tick);
bool bm_decode_led_cancel(const uint8_t *data, size_t len, uint64_t *execute_tick);
size_t bm_encode_led_reset_all(uint8_t *out, size_t out_len, uint64_t cutoff_tick);
bool bm_decode_led_reset_all(const uint8_t *data, size_t len, uint64_t *cutoff_tick);
size_t bm_encode_led_set_now(uint8_t *out, size_t out_len, const bm_led_set_now_t *value);
bool bm_decode_led_set_now(const uint8_t *data, size_t len, bm_led_set_now_t *out);
size_t bm_encode_led_reset_and_set(uint8_t *out, size_t out_len, uint64_t cutoff_tick, bm_rgb_t rgb);
bool bm_decode_led_reset_and_set(const uint8_t *data, size_t len, uint64_t *cutoff_tick, bm_rgb_t *rgb);

/* Telemetry / status. */
size_t bm_encode_ftm_report(uint8_t *out, size_t out_len, const bm_ftm_report_t *value);
bool bm_decode_ftm_report(const uint8_t *data, size_t len, bm_ftm_report_t *out);
size_t bm_encode_queue_status(uint8_t *out, size_t out_len, const bm_queue_status_t *value);
bool bm_decode_queue_status(const uint8_t *data, size_t len, bm_queue_status_t *out);
size_t bm_encode_neighbor_report(uint8_t *out, size_t out_len, const bm_neighbor_report_t *value);
bool bm_decode_neighbor_report(const uint8_t *data, size_t len, bm_neighbor_report_t *out);

/* True when a <= b in the wrapping 32-bit FTM timestamp domain. */
bool bm_timestamp_is_older_or_equal(uint32_t a, uint32_t b);

#ifdef __cplusplus
}
#endif
