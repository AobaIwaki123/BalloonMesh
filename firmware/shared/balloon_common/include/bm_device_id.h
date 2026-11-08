#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Builds a stable per-device provisioning UUID from the Bluetooth MAC.
 * The first 10 bytes identify BalloonMesh; the last 6 bytes are the MAC. */
void bm_device_uuid(uint8_t uuid[16]);

#ifdef __cplusplus
}
#endif
