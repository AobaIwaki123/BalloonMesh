#include "bm_anchor_protocol.h"

#include <string.h>

uint16_t bm_crc16_ccitt(const uint8_t *data, size_t length) {
    uint16_t crc = 0xFFFFu;
    for (size_t i = 0; i < length; ++i) {
        crc ^= (uint16_t)data[i] << 8;
        for (int bit = 0; bit < 8; ++bit) {
            if ((crc & 0x8000u) != 0u) {
                crc = (uint16_t)((crc << 1) ^ 0x1021u);
            } else {
                crc = (uint16_t)(crc << 1);
            }
        }
    }
    return crc;
}

size_t bm_anchor_request_encode(uint8_t *out, size_t out_len, const uint8_t token[8]) {
    if (out == NULL || token == NULL || out_len < BM_ANCHOR_REQUEST_SIZE) {
        return 0;
    }
    memset(out, 0, BM_ANCHOR_REQUEST_SIZE);
    memcpy(&out[0], &(uint32_t){BM_ANCHOR_REQUEST_MAGIC}, 4);
    out[4] = BM_ANCHOR_PROTO_VERSION;
    memcpy(&out[8], token, 8);
    return BM_ANCHOR_REQUEST_SIZE;
}

bool bm_anchor_request_validate(const uint8_t *data, size_t length, const uint8_t expected_token[8]) {
    if (data == NULL || expected_token == NULL || length < BM_ANCHOR_REQUEST_SIZE) {
        return false;
    }
    uint32_t magic = 0;
    memcpy(&magic, &data[0], 4);
    if (magic != BM_ANCHOR_REQUEST_MAGIC || data[4] != BM_ANCHOR_PROTO_VERSION) {
        return false;
    }
    return memcmp(&data[8], expected_token, 8) == 0;
}

size_t bm_anchor_response_encode(uint8_t *out,
                                 size_t out_len,
                                 uint8_t anchor_id,
                                 uint8_t flags,
                                 int64_t utc_ms,
                                 int32_t lat_e7,
                                 int32_t lon_e7,
                                 int32_t alt_cm,
                                 uint16_t h_acc_cm,
                                 const uint8_t token[8]) {
    if (out == NULL || token == NULL || out_len < BM_ANCHOR_RESPONSE_SIZE) {
        return 0;
    }
    memset(out, 0, BM_ANCHOR_RESPONSE_SIZE);
    uint32_t magic = BM_ANCHOR_RESPONSE_MAGIC;
    memcpy(&out[0], &magic, 4);
    out[4] = BM_ANCHOR_PROTO_VERSION;
    out[5] = anchor_id;
    out[6] = flags;
    out[7] = 0;
    memcpy(&out[8], &utc_ms, 8);
    memcpy(&out[16], &lat_e7, 4);
    memcpy(&out[20], &lon_e7, 4);
    memcpy(&out[24], &alt_cm, 4);
    memcpy(&out[28], &h_acc_cm, 2);
    memcpy(&out[32], token, 8);
    uint16_t crc = bm_crc16_ccitt(out, BM_ANCHOR_RESPONSE_SIZE - 2);
    memcpy(&out[38], &crc, 2);
    return BM_ANCHOR_RESPONSE_SIZE;
}

bool bm_anchor_response_parse(const uint8_t *data,
                              size_t length,
                              bm_anchor_response_t *out) {
    if (data == NULL || out == NULL || length != BM_ANCHOR_RESPONSE_SIZE) {
        return false;
    }
    uint16_t expected_crc = 0;
    memcpy(&expected_crc, &data[38], 2);
    if (bm_crc16_ccitt(data, BM_ANCHOR_RESPONSE_SIZE - 2) != expected_crc) {
        return false;
    }
    memcpy(out, data, sizeof(*out));
    return out->magic == BM_ANCHOR_RESPONSE_MAGIC &&
           out->version == BM_ANCHOR_PROTO_VERSION;
}
