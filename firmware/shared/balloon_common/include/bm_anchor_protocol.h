/*
 * Protocol A local anchor data exchange.
 *
 * Both endpoints are ESP32 devices, so the wire format is little-endian.
 * A request is a fixed 16-byte datagram; a response is a fixed 40-byte
 * datagram protected by CRC-16/CCITT-FALSE.
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define BM_ANCHOR_REQUEST_MAGIC 0x51414D42u  /* "BMAQ" little-endian */
#define BM_ANCHOR_RESPONSE_MAGIC 0x52414D42u /* "BMAR" little-endian */
#define BM_ANCHOR_PROTO_VERSION 1u
#define BM_ANCHOR_REQUEST_SIZE 16u
#define BM_ANCHOR_RESPONSE_SIZE 40u

#define BM_ANCHOR_FLAG_TIME_VALID 0x01u
#define BM_ANCHOR_FLAG_POSITION_VALID 0x02u
#define BM_ANCHOR_FLAG_AUTHENTICATED 0x04u

typedef struct {
    uint32_t magic;
    uint8_t version;
    uint8_t reserved[3];
    uint8_t token[8];
} __attribute__((packed)) bm_anchor_request_t;

typedef struct {
    uint32_t magic;
    uint8_t version;
    uint8_t anchor_id;
    uint8_t flags;
    uint8_t reserved;
    uint64_t utc_ms;
    int32_t lat_e7;
    int32_t lon_e7;
    int32_t alt_cm;
    uint16_t h_acc_cm;
    uint8_t token[8];
    uint16_t crc;
} __attribute__((packed)) bm_anchor_response_t;

uint16_t bm_crc16_ccitt(const uint8_t *data, size_t length);
size_t bm_anchor_request_encode(uint8_t *out, size_t out_len, const uint8_t token[8]);
bool bm_anchor_request_validate(const uint8_t *data, size_t length, const uint8_t expected_token[8]);
size_t bm_anchor_response_encode(uint8_t *out,
                                 size_t out_len,
                                 uint8_t anchor_id,
                                 uint8_t flags,
                                 int64_t utc_ms,
                                 int32_t lat_e7,
                                 int32_t lon_e7,
                                 int32_t alt_cm,
                                 uint16_t h_acc_cm,
                                 const uint8_t token[8]);
bool bm_anchor_response_parse(const uint8_t *data,
                              size_t length,
                              bm_anchor_response_t *out);

#ifdef __cplusplus
}
#endif
