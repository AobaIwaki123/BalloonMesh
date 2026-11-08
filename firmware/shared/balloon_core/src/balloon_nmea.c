#include "balloon_nmea.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define BM_NMEA_MAX_FIELDS 24
#define BM_NMEA_LINE_MAX 160

static int64_t days_from_civil(int year, unsigned month, unsigned day) {
    year -= month <= 2;
    const int era = (year >= 0 ? year : year - 399) / 400;
    const unsigned yoe = (unsigned)(year - era * 400);
    const unsigned doy = (153u * (month + (month > 2 ? (unsigned)-3 : 9u)) + 2u) / 5u + day - 1u;
    const unsigned doe = yoe * 365u + yoe / 4u - yoe / 100u + doy;
    return (int64_t)era * 146097 + (int64_t)doe - 719468;
}

static bool checksum_ok(const char *line) {
    if (line[0] != '$') {
        return false;
    }
    const char *star = strchr(line, '*');
    if (star == NULL || star[1] == '\0' || star[2] == '\0') {
        return false;
    }
    uint8_t checksum = 0;
    for (const char *p = line + 1; p < star; ++p) {
        checksum ^= (uint8_t)*p;
    }
    char expected[3] = {star[1], star[2], 0};
    return checksum == (uint8_t)strtoul(expected, NULL, 16);
}

static bool parse_decimal_coordinate(const char *value, const char *hemisphere,
                                     int32_t *out_e7) {
    if (value == NULL || hemisphere == NULL || value[0] == '\0') {
        return false;
    }
    double raw = strtod(value, NULL);
    int degrees = (int)(raw / 100.0);
    double minutes = raw - (double)degrees * 100.0;
    double decimal = (double)degrees + minutes / 60.0;
    if (hemisphere[0] == 'S' || hemisphere[0] == 'W') {
        decimal = -decimal;
    }
    if (decimal < -180.0 || decimal > 180.0) {
        return false;
    }
    *out_e7 = (int32_t)llround(decimal * 10000000.0);
    return true;
}

static bool parse_hms(const char *value, int *hour, int *minute, int *second,
                      int *millisecond) {
    if (value == NULL || value[0] == '\0') {
        return false;
    }
    double seconds = strtod(value, NULL);
    int whole = (int)seconds;
    *hour = whole / 10000;
    *minute = (whole / 100) % 100;
    *second = whole % 100;
    double fraction = seconds - (double)whole;
    *millisecond = (int)(fraction * 1000.0 + 0.5);
    if (*millisecond >= 1000) {
        *millisecond = 999;
    }
    return *hour < 24 && *minute < 60 && *second <= 60;
}

static void handle_gga(bm_nmea_state_t *state, char *fields[], int count) {
    if (count < 10) {
        return;
    }
    int fix = atoi(fields[6]);
    if (fix <= 0) {
        state->position_valid = false;
        return;
    }
    int32_t lat_e7;
    int32_t lon_e7;
    if (!parse_decimal_coordinate(fields[2], fields[3], &lat_e7) ||
        !parse_decimal_coordinate(fields[4], fields[5], &lon_e7)) {
        return;
    }
    int hour, minute, second, millisecond;
    if (!parse_hms(fields[1], &hour, &minute, &second, &millisecond)) {
        return;
    }
    int32_t alt_cm = (int32_t)lround(strtod(fields[9], NULL) * 100.0);

    state->position_valid = true;
    state->lat_e7 = lat_e7;
    state->lon_e7 = lon_e7;
    state->alt_cm = alt_cm;
    state->h_acc_cm = 500;
    if (state->year != 0) {
        int64_t days = days_from_civil(state->year, (unsigned)state->month,
                                       (unsigned)state->day);
        state->utc_ms = (days * 86400 + hour * 3600 + minute * 60 + second) * 1000 +
                        millisecond;
        state->time_valid = true;
    }
}

static void handle_rmc(bm_nmea_state_t *state, char *fields[], int count) {
    if (count < 10) {
        return;
    }
    if (fields[2][0] != 'A') {
        return;
    }
    int hour, minute, second, millisecond;
    if (!parse_hms(fields[1], &hour, &minute, &second, &millisecond)) {
        return;
    }
    if (strlen(fields[9]) < 6) {
        return;
    }
    int day = 0, month = 0, year = 0;
    if (sscanf(fields[9], "%2d%2d%2d", &day, &month, &year) != 3) {
        return;
    }
    if (year < 70) {
        year += 2000;
    } else {
        year += 1900;
    }
    int64_t days = days_from_civil(year, (unsigned)month, (unsigned)day);
    state->utc_ms = (days * 86400 + hour * 3600 + minute * 60 + second) * 1000 +
                    millisecond;
    state->year = year;
    state->month = month;
    state->day = day;
    state->time_valid = true;

    int32_t lat_e7 = 0;
    int32_t lon_e7 = 0;
    bool have_position =
        parse_decimal_coordinate(fields[3], fields[4], &lat_e7) &&
        parse_decimal_coordinate(fields[5], fields[6], &lon_e7);
    if (have_position) {
        state->position_valid = true;
        state->lat_e7 = lat_e7;
        state->lon_e7 = lon_e7;
    }
}

void bm_nmea_init(bm_nmea_state_t *state) {
    if (state != NULL) {
        memset(state, 0, sizeof(*state));
    }
}

bool bm_nmea_feed_line(bm_nmea_state_t *state, const char *line) {
    if (state == NULL || line == NULL || line[0] != '$') {
        return false;
    }

    char buffer[BM_NMEA_LINE_MAX];
    size_t length = strlen(line);
    if (length >= sizeof(buffer)) {
        return false;
    }
    memcpy(buffer, line, length + 1);
    while (length > 0 && (buffer[length - 1] == '\r' || buffer[length - 1] == '\n')) {
        buffer[--length] = '\0';
    }
    if (length < 7 || !checksum_ok(buffer)) {
        return false;
    }
    char *star = strchr(buffer, '*');
    if (star != NULL) {
        *star = '\0';
    }

    char *fields[BM_NMEA_MAX_FIELDS] = {0};
    int count = 0;
    char *cursor = buffer;
    while (cursor != NULL && count < BM_NMEA_MAX_FIELDS) {
        fields[count++] = cursor;
        cursor = strchr(cursor, ',');
        if (cursor != NULL) {
            *cursor++ = '\0';
        }
    }
    if (count < 2 || fields[0][0] != '$' || strlen(fields[0]) < 6) {
        return false;
    }

    if (strcmp(fields[0] + 3, "GGA") == 0) {
        handle_gga(state, fields, count);
        return true;
    }
    if (strcmp(fields[0] + 3, "RMC") == 0) {
        handle_rmc(state, fields, count);
        return true;
    }
    return false;
}
