#include "bm_device_id.h"

#include <string.h>

#include "esp_mac.h"

void bm_device_uuid(uint8_t uuid[16]) {
    static const uint8_t prefix[10] = {
        0xB1, 0xA1, 0x10, 0x00, 0x00,
        0x01, 0x11, 0x22, 0x33, 0x44
    };
    uint8_t mac[6] = {0};
    if (esp_read_mac(mac, ESP_MAC_BT) != ESP_OK) {
        esp_read_mac(mac, ESP_MAC_WIFI_STA);
    }
    memcpy(uuid, prefix, sizeof(prefix));
    memcpy(uuid + sizeof(prefix), mac, sizeof(mac));
}
