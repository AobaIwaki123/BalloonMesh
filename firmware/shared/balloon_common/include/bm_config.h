/* Compile-time deployment settings for the prototype firmware. */
#pragma once

#include "esp_err.h"

#define BM_FIRMWARE_VERSION "0.1.0"

/* Bluetooth Mesh addressing.  Production deployments should provision these
 * values rather than compiling them in. */
#define BM_COMPANY_ID_LOCAL 0x02E5u
#define BM_NET_IDX 0u
#define BM_APP_IDX 0u
#define BM_MODEL_ID_CONTROL 0x0001u
#define BM_MODEL_ID_TELEMETRY 0x0002u

#define BM_GROUP_FTM_REPORT 0xC001u
#define BM_GROUP_NEIGHBOR_REPORT 0xC002u
#define BM_GROUP_QUEUE_STATUS 0xC003u
#define BM_GROUP_NEIGHBOR_PROBE 0xC004u
#define BM_GROUP_LED_ALL 0xC010u
#define BM_GROUP_LED_ZONE_BASE 0xC100u
#define BM_GROUP_LED_ZONE_COUNT 16u

/* Provisioning identity (PB-ADV/PB-GATT).  Replace per device. */
#define BM_PROV_UUID { 0xB1, 0xA1, 0x10, 0x00, 0x00, 0x01, 0x11, 0x22, \
                       0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA }

/* Anchor SoftAP and Protocol A. */
#define BM_ANCHOR_SSID_PREFIX "BM-ANCHOR-"
#define BM_ANCHOR_WIFI_PASSWORD "balloonmesh"
#define BM_ANCHOR_AP_IP "192.168.4.1"
#define BM_ANCHOR_UDP_PORT 3232
#define BM_ANCHOR_TOKEN_BYTES { 'B', 'A', 'L', 'L', 'O', 'O', 'N', '1' }
#define BM_ANCHOR_MAX 8u
#define BM_ANCHOR_WIFI_CHANNEL 6u

typedef struct {
    uint8_t id;
    char ssid[32];
} bm_anchor_entry_t;

static const bm_anchor_entry_t BM_ANCHORS[] = {
    { 1, "BM-ANCHOR-1" },
    { 2, "BM-ANCHOR-2" },
    { 3, "BM-ANCHOR-3" },
};

/* LED PWM pins (ESP32-C3 DevKitM-1). */
#define BM_LED_R_GPIO 3
#define BM_LED_G_GPIO 4
#define BM_LED_B_GPIO 5
#define BM_LED_PWM_FREQ_HZ 5000
#define BM_LED_PWM_RESOLUTION 8

/* Anchor UART (NMEA). */
#define BM_GPS_UART_PORT 1
#define BM_GPS_TX_GPIO 6
#define BM_GPS_RX_GPIO 7
#define BM_GPS_BAUD 9600

/* Gateway configuration (override with -D in platformio.ini). */
#ifndef BM_GATEWAY_ID
#define BM_GATEWAY_ID "gw-01"
#endif
#ifndef BM_SERVER_URL
#define BM_SERVER_URL "http://192.168.1.10:5080"
#endif
/* Optional PEM CA bundle for HTTPS gateways. Define through build_flags. */
#ifndef BM_TLS_CA_CERT
/* #define BM_TLS_CA_CERT "-----BEGIN CERTIFICATE-----\n..." */
#endif
#ifndef BM_GATEWAY_WIFI_SSID
#define BM_GATEWAY_WIFI_SSID "balloon-mesh"
#endif
#ifndef BM_GATEWAY_WIFI_PASSWORD
#define BM_GATEWAY_WIFI_PASSWORD "balloonmesh"
#endif

/* Neighbour probe timing. */
#define BM_NEIGHBOR_PROBE_WINDOW_MS 20000u
#define BM_PROBE_RESPONSE_SPAN_MS 19000u
