#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    bool time_valid;
    bool position_valid;
    int64_t utc_ms;
    int32_t lat_e7;
    int32_t lon_e7;
    int32_t alt_cm;
    uint16_t h_acc_cm;
    int year;
    int month;
    int day;
} bm_nmea_state_t;

void bm_nmea_init(bm_nmea_state_t *state);

/* Feeds one NMEA 0183 line. Returns true when a recognised sentence was
 * accepted (checksum valid).  RMC supplies date/time, GGA supplies altitude. */
bool bm_nmea_feed_line(bm_nmea_state_t *state, const char *line);

/* Also reports whether this sentence supplied a valid position, including
 * when the coordinates are unchanged from the previous fix. */
bool bm_nmea_feed_line_with_position_update(bm_nmea_state_t *state, const char *line,
                                             bool *position_updated);

#ifdef __cplusplus
}
#endif
