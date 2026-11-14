#include "bm_device_id.h"

#include <string.h>

#include "esp_mac.h"

static void read_device_mac(uint8_t mac[6]) {
    memset(mac, 0, 6);
    if (esp_read_mac(mac, ESP_MAC_BT) != ESP_OK) {
        esp_read_mac(mac, ESP_MAC_WIFI_STA);
    }
}

void bm_device_uuid(uint8_t uuid[16]) {
    static const uint8_t prefix[10] = {
        0xB1, 0xA1, 0x10, 0x00, 0x00,
        0x01, 0x11, 0x22, 0x33, 0x44
    };
    uint8_t mac[6];
    read_device_mac(mac);
    memcpy(uuid, prefix, sizeof(prefix));
    memcpy(uuid + sizeof(prefix), mac, sizeof(mac));
}

uint16_t bm_device_dev_unicast(uint16_t base, uint16_t span) {
    if (span == 0u) {
        return base;
    }
    uint8_t mac[6];
    read_device_mac(mac);
    /* FNV-1a over the MAC.  A Mesh unicast address is 15 bit and must not be
     * 0x0000, which the caller's base/span keeps it clear of. */
    uint32_t hash = 2166136261u;
    for (int i = 0; i < 6; ++i) {
        hash ^= mac[i];
        hash *= 16777619u;
    }
    return (uint16_t)(base + (uint16_t)(hash % span));
}
