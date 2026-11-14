/* Compile-time deployment settings for the prototype firmware. */
#pragma once

#include "esp_err.h"

#define BM_FIRMWARE_VERSION "0.1.0"

/* Normal balloon firmware includes Protocol A / FTM positioning.  The
 * BLE-Mesh-only bring-up profile overrides this at build time. */
#ifndef BM_ENABLE_POSITIONING
#define BM_ENABLE_POSITIONING 1
#endif

/* Bluetooth Mesh addressing.  Production deployments should provision these
 * values rather than compiling them in. */
#define BM_COMPANY_ID_LOCAL 0x02E5u
#define BM_NET_IDX 0u
#define BM_APP_IDX 0u
#define BM_MODEL_ID_CONTROL 0x0001u
#define BM_MODEL_ID_TELEMETRY 0x0002u

/* Two broadcast groups by receiver role; individual nodes use unicast addresses.
 * Each model subscribes to only one of them (well under the library's
 * CONFIG_BLE_MESH_MODEL_GROUP_COUNT limit of 3). */
#define BM_GROUP_BALLOON 0xC010u /* balloon-facing: LED, time, neighbour probe */
#define BM_GROUP_GATEWAY 0xC011u /* gateway-facing: FTM/neighbour/queue reports */

#define BM_GROUP_FTM_REPORT BM_GROUP_GATEWAY
#define BM_GROUP_NEIGHBOR_REPORT BM_GROUP_GATEWAY
#define BM_GROUP_QUEUE_STATUS BM_GROUP_GATEWAY
#define BM_GROUP_NEIGHBOR_PROBE BM_GROUP_BALLOON
#define BM_GROUP_TIME BM_GROUP_BALLOON
#define BM_GROUP_LED_ALL BM_GROUP_BALLOON

/* Provisioning identity (PB-ADV/PB-GATT) is generated per device from the
 * Bluetooth MAC by bm_device_uuid(); do not hard-code a shared UUID here. */

/* Anchor SoftAP and Protocol A. */
#define BM_ANCHOR_SSID_PREFIX "BM-ANCHOR-"
#define BM_ANCHOR_WIFI_PASSWORD "balloonmesh"
#define BM_ANCHOR_AP_IP "192.168.4.1"
#define BM_ANCHOR_UDP_PORT 3232
#define BM_ANCHOR_TOKEN_BYTES { 'B', 'A', 'L', 'L', 'O', 'O', 'N', '1' }
#define BM_ANCHOR_MAX 8u
#define BM_ANCHOR_WIFI_CHANNEL 6u
/* Anchors on one channel share the air with every balloon associating to them,
 * so spread them over the three non-overlapping 2.4 GHz channels. */
#define BM_ANCHOR_CHANNEL_FOR_ID(id) \
    ((uint8_t)(((id) % 3u) == 1u ? 1u : (((id) % 3u) == 2u ? 6u : 11u)))
/* SoftAP association slots.  The default of 4 makes the fifth balloon of a
 * cycle wait out the full connect timeout, so FTM coverage collapses as the
 * fleet grows. */
#define BM_ANCHOR_MAX_CLIENTS 8u
/* The anchor stops advertising TIME_VALID / POSITION_VALID once the last fix
 * is older than this; a GPS that loses its fix otherwise serves a frozen
 * clock that drags every balloon's LED schedule backwards. */
#define BM_ANCHOR_TIME_MAX_AGE_MS 3000u
#define BM_ANCHOR_FIX_MAX_AGE_MS 10000u
// Association can take several seconds when the balloon is far from an
// anchor; keep enough time for the first scan before declaring it unreachable.
#define BM_ANCHOR_CONNECT_TIMEOUT_MS 12000u

typedef struct {
    uint8_t id;
    char ssid[32];
} bm_anchor_entry_t;

static const bm_anchor_entry_t BM_ANCHORS[] = {
    { 1, "BM-ANCHOR-1" },
    { 2, "BM-ANCHOR-2" },
    { 3, "BM-ANCHOR-3" },
    { 4, "BM-ANCHOR-4" },
};

/* LED PWM pins (Seeed XIAO ESP32C3: D0=GPIO2, D1=GPIO3, D2=GPIO4). */
#define BM_LED_R_GPIO 2
#define BM_LED_G_GPIO 3
#define BM_LED_B_GPIO 4
#define BM_LED_PWM_FREQ_HZ 5000
/* 10 bit at 5 kHz is well within the LEDC limit (80 MHz / 5 kHz = 2^13.9) and
 * gives the gamma curve enough room at the bottom: with 8 bit duty the dimmest
 * commanded colours rounded down to "off". */
#define BM_LED_PWM_RESOLUTION 10

/* Perceptual gamma applied to the physical LED output only.  Linear duty
 * makes low colour values look far brighter than intended (the eye is
 * non-linear and 1W-class chips stay bright even at small duty), so the
 * commanded 0-255 value is mapped through (value/255)^gamma before it
 * reaches LEDC.  Colours stored in the queue/DB/NVS stay linear 0-255;
 * only bm_led_output_set() applies the curve.  Override per deployment
 * with -DBM_LED_GAMMA=0 (linear) or e.g. -DBM_LED_GAMMA=2.8. */
#ifndef BM_LED_GAMMA
#define BM_LED_GAMMA 2.2f
#endif

/* Anchor UART (NMEA). */
#define BM_GPS_UART_PORT 1
#define BM_GPS_TX_GPIO 6
#define BM_GPS_RX_GPIO 7
#define BM_GPS_BAUD 9600

/* Gateway deployment configuration.  The gateway PlatformIO project injects
 * these values from the build environment; do not store deployment endpoints
 * or credentials in this shared header. */
#ifndef BM_GATEWAY_TOKEN
#define BM_GATEWAY_TOKEN ""
#endif
#ifndef BM_GATEWAY_ID
#define BM_GATEWAY_ID ""
#endif
#ifndef BM_SERVER_URL
#define BM_SERVER_URL ""
#endif
/* Optional PEM CA bundle for HTTPS gateways. Define through build_flags. */
#ifndef BM_TLS_CA_CERT
/* #define BM_TLS_CA_CERT "-----BEGIN CERTIFICATE-----\n..." */
#endif
#ifndef BM_GATEWAY_WIFI_SSID
#define BM_GATEWAY_WIFI_SSID ""
#endif
#ifndef BM_GATEWAY_WIFI_PASSWORD
#define BM_GATEWAY_WIFI_PASSWORD ""
#endif

#if defined(BM_ROLE_GATEWAY)
/* A gateway image must be built with deployment-specific identity, endpoint,
 * authentication token, and Wi-Fi credentials.  Keep the diagnostics generic
 * so a missing value cannot echo a secret into build logs. */
#if defined(__cplusplus)
static_assert(sizeof(BM_GATEWAY_ID) > 1,
              "BM_GATEWAY_ID must be supplied for gateway deployment");
static_assert(sizeof(BM_SERVER_URL) > 1,
              "BM_SERVER_URL must be supplied for gateway deployment");
static_assert(sizeof(BM_GATEWAY_TOKEN) > 1,
              "BM_GATEWAY_TOKEN must be supplied for gateway deployment");
static_assert(sizeof(BM_GATEWAY_WIFI_SSID) > 1,
              "BM_GATEWAY_WIFI_SSID must be supplied for gateway deployment");
static_assert(sizeof(BM_GATEWAY_WIFI_PASSWORD) > 1,
              "BM_GATEWAY_WIFI_PASSWORD must be supplied for gateway deployment");
#else
_Static_assert(sizeof(BM_GATEWAY_ID) > 1,
               "BM_GATEWAY_ID must be supplied for gateway deployment");
_Static_assert(sizeof(BM_SERVER_URL) > 1,
               "BM_SERVER_URL must be supplied for gateway deployment");
_Static_assert(sizeof(BM_GATEWAY_TOKEN) > 1,
               "BM_GATEWAY_TOKEN must be supplied for gateway deployment");
_Static_assert(sizeof(BM_GATEWAY_WIFI_SSID) > 1,
               "BM_GATEWAY_WIFI_SSID must be supplied for gateway deployment");
_Static_assert(sizeof(BM_GATEWAY_WIFI_PASSWORD) > 1,
               "BM_GATEWAY_WIFI_PASSWORD must be supplied for gateway deployment");
#endif
#endif

/* Neighbour probe timing. */
#define BM_NEIGHBOR_PROBE_WINDOW_MS 20000u
#define BM_PROBE_RESPONSE_SPAN_MS 19000u
/* Spread of the first PROBE after the group-wide REQUEST, and the gap between
 * the following ones.  first jitter + 2 * max gap stays inside
 * BM_PROBE_RESPONSE_SPAN_MS for BM_NEIGHBOR_PROBE_TX_COUNT = 3. */
#define BM_PROBE_FIRST_JITTER_MS 2500u
#define BM_PROBE_GAP_MIN_MS 4000u
#define BM_PROBE_GAP_JITTER_MS 4000u

/* Mesh time fallback.  A TIME_REQUEST is a group broadcast, so without jitter
 * and suppression every synced balloon answers it in the same millisecond. */
#define BM_TIME_REPLY_JITTER_MS 2000u
#define BM_TIME_REPLY_SUPPRESS_MS 5000u
#define BM_TIME_REPLY_MIN_INTERVAL_MS 10000u
