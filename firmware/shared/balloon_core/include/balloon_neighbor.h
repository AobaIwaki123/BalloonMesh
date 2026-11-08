/*
 * Conditional BLE neighbour probe aggregation.
 *
 * Only executed when fewer than three anchors produced a usable FTM range.
 * REQUEST and PROBE are sent with TTL 0, so only directly received PROBEs are
 * associated with a neighbour Mesh source address.  Each neighbour's samples
 * are reduced with an integer median; the ten strongest are reported.
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define BM_NEIGHBOR_MAX_TRACKED 64u
#define BM_NEIGHBOR_MAX_SAMPLES 8u
#define BM_NEIGHBOR_REPORT_COUNT 10u
#define BM_NEIGHBOR_WINDOW_MS 20000u
#define BM_NEIGHBOR_PROBE_TX_COUNT 3u

typedef struct {
    uint16_t mesh_addr;
    int8_t samples[BM_NEIGHBOR_MAX_SAMPLES];
    uint8_t sample_count;
} bm_neighbor_observation_t;

typedef struct {
    bm_neighbor_observation_t items[BM_NEIGHBOR_MAX_TRACKED];
    uint8_t count;
    uint32_t window_start_ms;
    uint32_t window_duration_ms;
    bool active;
} bm_neighbor_collector_t;

typedef struct {
    uint16_t mesh_addr;
    int8_t rssi_dbm;
} bm_neighbor_result_t;

void bm_neighbor_init(bm_neighbor_collector_t *collector);
void bm_neighbor_begin(bm_neighbor_collector_t *collector,
                       uint32_t start_ms,
                       uint32_t duration_ms);
bool bm_neighbor_add(bm_neighbor_collector_t *collector,
                     uint16_t mesh_addr,
                     int8_t rssi_dbm,
                     uint32_t now_ms);
void bm_neighbor_end(bm_neighbor_collector_t *collector);

int8_t bm_median_i8(const int8_t *values, size_t count);

/* Returns 0..max_results, strongest first. */
uint8_t bm_neighbor_results(const bm_neighbor_collector_t *collector,
                            bm_neighbor_result_t *results,
                            uint8_t max_results);

#ifdef __cplusplus
}
#endif
