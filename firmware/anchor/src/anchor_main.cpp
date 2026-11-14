#include <Arduino.h>
#include <HardwareSerial.h>
#include <WiFi.h>
#include <WiFiUdp.h>

#include <string.h>

#include "esp_log.h"
#include "esp_wifi.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nvs_flash.h"

#include "balloon_nmea.h"
#include "bm_anchor_protocol.h"
#include "bm_config.h"

#ifndef BM_ANCHOR_ID
#define BM_ANCHOR_ID 1
#endif

static const char *TAG = "anchor";
static const uint8_t s_token[8] = BM_ANCHOR_TOKEN_BYTES;
static HardwareSerial s_gps(BM_GPS_UART_PORT);
static portMUX_TYPE s_state_lock = portMUX_INITIALIZER_UNLOCKED;
static bm_nmea_state_t s_state;

static void gps_task(void *argument) {
    (void)argument;
    char line[160];
    size_t length = 0;
    uint32_t report_at = millis() + 5000u;
    uint32_t rx_bytes = 0;
    uint32_t valid_nmea = 0;
    for (;;) {
        while (s_gps.available() > 0) {
            char c = (char)s_gps.read();
            rx_bytes++;
            if (c == '\n') {
                line[length] = '\0';
                if (length > 0) {
                    bm_nmea_state_t parsed;
                    portENTER_CRITICAL(&s_state_lock);
                    parsed = s_state;
                    portEXIT_CRITICAL(&s_state_lock);

                    if (bm_nmea_feed_line(&parsed, line)) {
                        portENTER_CRITICAL(&s_state_lock);
                        s_state = parsed;
                        portEXIT_CRITICAL(&s_state_lock);
                        valid_nmea++;
                    }
                }
                length = 0;
            } else if (length + 1 < sizeof(line)) {
                line[length++] = c;
            } else {
                length = 0;
            }
        }
        uint32_t now = millis();
        if ((int32_t)(now - report_at) >= 0) {
            bm_nmea_state_t state;
            portENTER_CRITICAL(&s_state_lock);
            state = s_state;
            portEXIT_CRITICAL(&s_state_lock);
            ESP_LOGI(TAG,
                     "GPS rx_bytes=%lu valid_nmea=%lu time_valid=%d position_valid=%d "
                     "utc_ms=%llu lat_e7=%ld lon_e7=%ld alt_cm=%ld h_acc_cm=%u",
                     (unsigned long)rx_bytes, (unsigned long)valid_nmea,
                     (int)state.time_valid, (int)state.position_valid,
                     (unsigned long long)state.utc_ms, (long)state.lat_e7,
                     (long)state.lon_e7, (long)state.alt_cm,
                     (unsigned)state.h_acc_cm);
            rx_bytes = 0;
            valid_nmea = 0;
            report_at = now + 5000u;
        }
        vTaskDelay(pdMS_TO_TICKS(20));
    }
}

static void udp_task(void *argument) {
    (void)argument;

    char id_text[8];
    snprintf(id_text, sizeof(id_text), "%d", BM_ANCHOR_ID);
    char ssid[32];
    snprintf(ssid, sizeof(ssid), "%s%s", BM_ANCHOR_SSID_PREFIX, id_text);

    // Wi-Fi must be initialised before WiFiUDP::begin() creates and binds the
    // lwIP socket; otherwise the bind fails and the anchor silently dies.
    WiFi.mode(WIFI_AP);
    // Configure the AP netif explicitly so the built-in DHCP server is
    // started reliably on every boot.  The balloon uses this address for the
    // UDP anchor-data request.
    const IPAddress ap_ip(192, 168, 4, 1);
    const IPAddress ap_subnet(255, 255, 255, 0);
    if (!WiFi.softAPConfig(ap_ip, ap_ip, ap_subnet)) {
        ESP_LOGE(TAG, "SoftAP IP/DHCP configuration failed");
    }
    // The Arduino AP helper supports FTM responder directly.  It also
    // performs the AP-side initialization (including the WPA2 cipher and
    // DHCP/netif setup) that a hand-built wifi_config_t would otherwise omit.
    if (!WiFi.softAP(ssid, BM_ANCHOR_WIFI_PASSWORD, BM_ANCHOR_WIFI_CHANNEL,
                     0, 4, true)) {
        ESP_LOGE(TAG, "SoftAP start failed");
    }
    // FTM measurements are invalid ("No valid FTM Measurements") on a 40 MHz
    // link, so keep the AP at 20 MHz; the balloon station does the same.
    esp_wifi_set_bandwidth(WIFI_IF_AP, WIFI_BW_HT20);
    esp_wifi_ftm_resp_set_offset(0);

    WiFiUDP udp;
    while (!udp.begin(BM_ANCHOR_UDP_PORT)) {
        ESP_LOGE(TAG, "UDP bind failed; retrying");
        vTaskDelay(pdMS_TO_TICKS(1000));
    }
    ESP_LOGI(TAG, "UDP anchor data server on port %u", BM_ANCHOR_UDP_PORT);
    ESP_LOGI(TAG, "SoftAP '%s' started, IP=%s, channel=%u, FTM responder",
             ssid, WiFi.softAPIP().toString().c_str(), BM_ANCHOR_WIFI_CHANNEL);

    uint8_t request[BM_ANCHOR_REQUEST_SIZE];
    uint8_t response[BM_ANCHOR_RESPONSE_SIZE];
    for (;;) {
        int size = udp.parsePacket();
        if (size > 0) {
            int read = udp.read(request, sizeof(request));
            if (read == (int)sizeof(request) &&
                bm_anchor_request_validate(request, sizeof(request), s_token)) {
                bm_nmea_state_t state;
                portENTER_CRITICAL(&s_state_lock);
                state = s_state;
                portEXIT_CRITICAL(&s_state_lock);

                uint8_t flags = BM_ANCHOR_FLAG_AUTHENTICATED;
                if (state.time_valid) {
                    flags |= BM_ANCHOR_FLAG_TIME_VALID;
                }
                if (state.position_valid) {
                    flags |= BM_ANCHOR_FLAG_POSITION_VALID;
                }
                size_t length = bm_anchor_response_encode(
                    response, sizeof(response), BM_ANCHOR_ID, flags,
                    state.time_valid ? state.utc_ms : 0,
                    state.lat_e7, state.lon_e7, state.alt_cm,
                    state.h_acc_cm, s_token);
                if (length > 0) {
                    udp.beginPacket(udp.remoteIP(), udp.remotePort());
                    udp.write(response, length);
                    udp.endPacket();
                }
            }
        }
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

void setup() {
    Serial.begin(115200);
    delay(200);
    ESP_LOGI(TAG, "BalloonMesh anchor %d firmware %s", BM_ANCHOR_ID,
             BM_FIRMWARE_VERSION);

    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(err);

    bm_nmea_init(&s_state);
    s_gps.begin(BM_GPS_BAUD, SERIAL_8N1, BM_GPS_RX_GPIO, BM_GPS_TX_GPIO);
    xTaskCreate(gps_task, "gps", 4096, NULL, 5, NULL);
    xTaskCreate(udp_task, "anchor_udp", 6144, NULL, 6, NULL);
}

void loop() {
    delay(1000);
}
