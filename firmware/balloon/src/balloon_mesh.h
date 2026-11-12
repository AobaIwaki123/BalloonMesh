#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void bm_balloon_mesh_init(void);
bool bm_balloon_mesh_is_provisioned(void);
uint16_t bm_balloon_mesh_primary_addr(void);
bool bm_balloon_mesh_send(uint32_t opcode,
                          uint16_t dst,
                          uint8_t ttl,
                          const uint8_t *data,
                          uint16_t length);
bool bm_balloon_mesh_publish_queue_status(uint8_t kind, uint16_t queue_count);

#ifdef __cplusplus
}
#endif
