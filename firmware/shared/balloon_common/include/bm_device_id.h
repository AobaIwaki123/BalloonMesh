#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Builds a stable per-device provisioning UUID from the Bluetooth MAC.
 * The first 10 bytes identify BalloonMesh; the last 6 bytes are the MAC. */
void bm_device_uuid(uint8_t uuid[16]);

/* Development-only unicast address derived from the Bluetooth MAC, returned in
 * [base, base + span).  BM_DEV_AUTO_PROV has no provisioner to allocate
 * addresses, and a compiled-in constant would give every device in the fleet
 * the same address (colliding unicast destinations plus a shared SEQ/replay
 * window).  A MAC-derived value is not collision proof, so it is for bench
 * use only; production images are provisioned.
 * Ranges in use: gateways 0x0002..0x00FF, balloons 0x0100..0x7FFF. */
uint16_t bm_device_dev_unicast(uint16_t base, uint16_t span);

#ifdef __cplusplus
}
#endif
