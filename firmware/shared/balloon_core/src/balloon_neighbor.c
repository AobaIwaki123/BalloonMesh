#include "balloon_neighbor.h"

#include <string.h>

static bool window_contains(uint32_t start, uint32_t duration, uint32_t now) {
    return (uint32_t)(now - start) < duration;
}

void bm_neighbor_init(bm_neighbor_collector_t *collector) {
    if (collector == NULL) {
        return;
    }
    memset(collector, 0, sizeof(*collector));
    collector->window_duration_ms = BM_NEIGHBOR_WINDOW_MS;
}

void bm_neighbor_begin(bm_neighbor_collector_t *collector,
                       uint32_t start_ms,
                       uint32_t duration_ms) {
    if (collector == NULL) {
        return;
    }
    memset(collector, 0, sizeof(*collector));
    collector->window_start_ms = start_ms;
    collector->window_duration_ms = duration_ms == 0 ? BM_NEIGHBOR_WINDOW_MS : duration_ms;
    collector->active = true;
}

bool bm_neighbor_add(bm_neighbor_collector_t *collector,
                     uint16_t mesh_addr,
                     int8_t rssi_dbm,
                     uint32_t now_ms) {
    if (collector == NULL || !collector->active) {
        return false;
    }
    if (!window_contains(collector->window_start_ms, collector->window_duration_ms, now_ms)) {
        return false;
    }

    for (uint8_t i = 0; i < collector->count; ++i) {
        if (collector->items[i].mesh_addr == mesh_addr) {
            bm_neighbor_observation_t *item = &collector->items[i];
            if (item->sample_count < BM_NEIGHBOR_MAX_SAMPLES) {
                item->samples[item->sample_count++] = rssi_dbm;
                return true;
            }
            return false;
        }
    }

    if (collector->count >= BM_NEIGHBOR_MAX_TRACKED) {
        return false;
    }
    bm_neighbor_observation_t *item = &collector->items[collector->count++];
    item->mesh_addr = mesh_addr;
    item->samples[0] = rssi_dbm;
    item->sample_count = 1;
    return true;
}

void bm_neighbor_end(bm_neighbor_collector_t *collector) {
    if (collector != NULL) {
        collector->active = false;
    }
}

int8_t bm_median_i8(const int8_t *values, size_t count) {
    if (values == NULL || count == 0) {
        return INT8_MIN;
    }
    int8_t sorted[BM_NEIGHBOR_MAX_SAMPLES];
    if (count > BM_NEIGHBOR_MAX_SAMPLES) {
        count = BM_NEIGHBOR_MAX_SAMPLES;
    }
    for (size_t i = 0; i < count; ++i) {
        sorted[i] = values[i];
    }
    for (size_t i = 1; i < count; ++i) {
        int8_t value = sorted[i];
        size_t j = i;
        while (j > 0 && sorted[j - 1] > value) {
            sorted[j] = sorted[j - 1];
            --j;
        }
        sorted[j] = value;
    }
    if ((count & 1u) != 0u) {
        return sorted[count / 2];
    }
    int16_t sum = (int16_t)sorted[count / 2 - 1] + (int16_t)sorted[count / 2];
    return (int8_t)(sum / 2);
}

uint8_t bm_neighbor_results(const bm_neighbor_collector_t *collector,
                            bm_neighbor_result_t *results,
                            uint8_t max_results) {
    if (collector == NULL || results == NULL || max_results == 0) {
        return 0;
    }
    bm_neighbor_result_t all[BM_NEIGHBOR_MAX_TRACKED];
    uint8_t count = 0;
    for (uint8_t i = 0; i < collector->count; ++i) {
        const bm_neighbor_observation_t *item = &collector->items[i];
        if (item->sample_count == 0) {
            continue;
        }
        all[count].mesh_addr = item->mesh_addr;
        all[count].rssi_dbm = bm_median_i8(item->samples, item->sample_count);
        ++count;
    }

    /* Insertion sort: strongest RSSI first, then ascending address. */
    for (uint8_t i = 1; i < count; ++i) {
        bm_neighbor_result_t value = all[i];
        uint8_t j = i;
        while (j > 0 &&
               (all[j - 1].rssi_dbm < value.rssi_dbm ||
                (all[j - 1].rssi_dbm == value.rssi_dbm &&
                 all[j - 1].mesh_addr > value.mesh_addr))) {
            all[j] = all[j - 1];
            --j;
        }
        all[j] = value;
    }

    uint8_t output = count < max_results ? count : max_results;
    memcpy(results, all, (size_t)output * sizeof(results[0]));
    return output;
}
