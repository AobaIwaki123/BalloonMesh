#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void bm_neighbor_link_init(void);

/* Requester side: starts a 20 s collection window and sends one REQUEST. */
bool bm_neighbor_link_begin_collection(uint32_t now_ms);
void bm_neighbor_link_finish_collection(uint8_t batch_id);
/* Discards a collection without reporting (used when FTM succeeded 3+ times). */
void bm_neighbor_link_abort_collection(void);

/* Called from the Mesh callback. */
void bm_neighbor_link_on_request(void);
void bm_neighbor_link_on_probe(uint16_t src, int8_t rssi_dbm, uint8_t ttl);

#ifdef __cplusplus
}
#endif
