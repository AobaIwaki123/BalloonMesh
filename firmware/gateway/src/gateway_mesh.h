#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint16_t src;
    uint16_t dst;
    uint32_t opcode;
    uint8_t length;
    uint8_t payload[8];
    int8_t rssi;
    uint8_t ttl;
    uint32_t received_ms;
} bm_gateway_event_t;

void bm_gateway_mesh_init(void);
bool bm_gateway_mesh_is_provisioned(void);
/* True only when provisioning is complete and both vendor models are bound to
 * the AppKey.  Downlinks can only be sent in this state. */
bool bm_gateway_mesh_is_ready(void);
uint16_t bm_gateway_mesh_primary_addr(void);
/* Immediate, unpaced transmit.  Prefer bm_gateway_mesh_enqueue(). */
bool bm_gateway_mesh_send(uint16_t dst,
                          uint32_t opcode,
                          uint8_t ttl,
                          const uint8_t *payload,
                          uint16_t length);

/* Queues a downlink for the paced sender task.  The advertising bearer carries
 * roughly ten to fifteen messages a second, while the server can push one
 * command per balloon at once; sending them inline dropped everything the
 * mesh's advertising buffers could not take (-ENOBUFS) with no retry, so LED
 * reservations went missing fleet-wide.  Returns false only when the queue
 * stayed full, which the caller should log. */
bool bm_gateway_mesh_enqueue(uint16_t dst,
                             uint32_t opcode,
                             uint8_t ttl,
                             const uint8_t *payload,
                             uint16_t length);

/* Number of downlinks waiting in the paced queue. */
uint16_t bm_gateway_mesh_pending_downlinks(void);

#ifdef __cplusplus
}
#endif
