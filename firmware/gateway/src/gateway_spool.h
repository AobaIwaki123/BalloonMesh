#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "gateway_mesh.h"

#ifdef __cplusplus
extern "C" {
#endif

void bm_gateway_spool_init(void);
void bm_gateway_spool_add(const bm_gateway_event_t *event);
uint16_t bm_gateway_spool_count(void);
bool bm_gateway_spool_peek(uint16_t index, bm_gateway_event_t *out);
void bm_gateway_spool_drop(uint16_t count);

#ifdef __cplusplus
}
#endif
