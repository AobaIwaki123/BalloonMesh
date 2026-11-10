#include "gateway_mesh.h"

#include <string.h>

#include "esp_bt.h"
#include "esp_bt_main.h"
#include "esp_log.h"
#include "esp_timer.h"

#include "esp_ble_mesh_common_api.h"
#include "esp_ble_mesh_config_model_api.h"
#include "esp_ble_mesh_defs.h"
#include "esp_ble_mesh_health_model_api.h"
#include "esp_ble_mesh_local_data_operation_api.h"
#include "esp_ble_mesh_networking_api.h"
#include "esp_ble_mesh_provisioning_api.h"

#include "balloon_protocol.h"
#include "bm_config.h"
#include "bm_device_id.h"
#include "gateway_spool.h"

static const char *TAG = "gateway-mesh";

static uint8_t s_prov_uuid[16];
static bool s_provisioned;
static uint16_t s_primary_addr;

static esp_ble_mesh_cfg_srv_t s_config_server = {
    .net_transmit = ESP_BLE_MESH_TRANSMIT(2, 20),
    .relay = ESP_BLE_MESH_RELAY_ENABLED,
    .relay_retransmit = ESP_BLE_MESH_TRANSMIT(2, 20),
    .beacon = ESP_BLE_MESH_BEACON_ENABLED,
    .gatt_proxy = ESP_BLE_MESH_GATT_PROXY_ENABLED,
    .friend_state = ESP_BLE_MESH_FRIEND_NOT_SUPPORTED,
    .default_ttl = 7,
};

ESP_BLE_MESH_HEALTH_PUB_DEFINE(s_health_pub, 0, ROLE_NODE);
static esp_ble_mesh_health_srv_t s_health_server;

static esp_ble_mesh_model_op_t s_telemetry_ops[] = {
    ESP_BLE_MESH_MODEL_OP(BM_VENDOR_OPCODE(BM_OP_FTM_REPORT), 8),
    ESP_BLE_MESH_MODEL_OP(BM_VENDOR_OPCODE(BM_OP_NEIGHBOR_REPORT), 8),
    ESP_BLE_MESH_MODEL_OP(BM_VENDOR_OPCODE(BM_OP_QUEUE_STATUS), 3),
    ESP_BLE_MESH_MODEL_OP_END,
};

static esp_ble_mesh_model_t s_root_models[] = {
    ESP_BLE_MESH_MODEL_CFG_SRV(&s_config_server),
    ESP_BLE_MESH_MODEL_HEALTH_SRV(&s_health_server, &s_health_pub),
};

static esp_ble_mesh_model_t s_vendor_models[] = {
    ESP_BLE_MESH_VENDOR_MODEL(BM_COMPANY_ID_LOCAL, BM_MODEL_ID_CONTROL, NULL, NULL, NULL),
    ESP_BLE_MESH_VENDOR_MODEL(BM_COMPANY_ID_LOCAL, BM_MODEL_ID_TELEMETRY,
                              s_telemetry_ops, NULL, NULL),
};

static esp_ble_mesh_elem_t s_elements[] = {
    ESP_BLE_MESH_ELEMENT(0, s_root_models, s_vendor_models),
};

static esp_ble_mesh_comp_t s_composition = {
    .cid = BM_COMPANY_ID_LOCAL,
    .pid = 0x0002,
    .vid = 0x0001,
    .element_count = sizeof(s_elements) / sizeof(s_elements[0]),
    .elements = s_elements,
};

static esp_ble_mesh_prov_t s_prov = {
    .uuid = s_prov_uuid,
};

static void subscribe_telemetry_groups(void) {
    uint16_t addr = esp_ble_mesh_get_primary_element_address();
    if (addr == ESP_BLE_MESH_ADDR_UNASSIGNED) {
        return;
    }
    /* Free slots occupied by subscriptions from older firmware. */
    const uint16_t legacy_groups[] = { 0xC001u, 0xC002u, 0xC003u };
    for (size_t i = 0; i < sizeof(legacy_groups) / sizeof(legacy_groups[0]); ++i) {
        (void)esp_ble_mesh_model_unsubscribe_group_addr(
            addr, BM_COMPANY_ID_LOCAL, BM_MODEL_ID_TELEMETRY, legacy_groups[i]);
    }
    esp_err_t err = esp_ble_mesh_model_subscribe_group_addr(
        addr, BM_COMPANY_ID_LOCAL, BM_MODEL_ID_TELEMETRY, BM_GROUP_GATEWAY);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "subscribe 0x%04x: %s", BM_GROUP_GATEWAY, esp_err_to_name(err));
    }
    ESP_LOGI(TAG, "gateway mesh primary address 0x%04x", addr);
}

static void prov_cb(esp_ble_mesh_prov_cb_event_t event,
                    esp_ble_mesh_prov_cb_param_t *param) {
    (void)param;
    if (event == ESP_BLE_MESH_NODE_PROV_COMPLETE_EVT) {
        s_provisioned = true;
        s_primary_addr = esp_ble_mesh_get_primary_element_address();
        subscribe_telemetry_groups();
    } else if (event == ESP_BLE_MESH_NODE_PROV_RESET_EVT) {
        s_provisioned = false;
        s_primary_addr = ESP_BLE_MESH_ADDR_UNASSIGNED;
        esp_err_t err = esp_ble_mesh_node_prov_enable(
            (esp_ble_mesh_prov_bearer_t)(ESP_BLE_MESH_PROV_ADV | ESP_BLE_MESH_PROV_GATT));
        if (err != ESP_OK) {
            ESP_LOGE(TAG, "restart provisioning failed: %s", esp_err_to_name(err));
        }
    }
}

static void config_server_cb(esp_ble_mesh_cfg_server_cb_event_t event,
                             esp_ble_mesh_cfg_server_cb_param_t *param) {
    (void)event;
    (void)param;
}

static void health_server_cb(esp_ble_mesh_health_server_cb_event_t event,
                             esp_ble_mesh_health_server_cb_param_t *param) {
    (void)event;
    (void)param;
}

static void custom_model_cb(esp_ble_mesh_model_cb_event_t event,
                            esp_ble_mesh_model_cb_param_t *param) {
    if (event != ESP_BLE_MESH_MODEL_OPERATION_EVT) {
        return;
    }
    bm_gateway_event_t mesh_event = {0};
    mesh_event.src = param->model_operation.ctx->addr;
    mesh_event.dst = param->model_operation.ctx->recv_dst;
    mesh_event.opcode = param->model_operation.opcode;
    mesh_event.rssi = param->model_operation.ctx->recv_rssi;
    mesh_event.ttl = param->model_operation.ctx->recv_ttl;
    mesh_event.received_ms = (uint32_t)(esp_timer_get_time() / 1000);
    mesh_event.length = param->model_operation.length > sizeof(mesh_event.payload)
                            ? sizeof(mesh_event.payload)
                            : (uint8_t)param->model_operation.length;
    if (mesh_event.length > 0) {
        memcpy(mesh_event.payload, param->model_operation.msg, mesh_event.length);
    }
    bm_gateway_spool_add(&mesh_event);
}

void bm_gateway_mesh_init(void) {
    esp_bt_controller_config_t bt_cfg = BT_CONTROLLER_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_bt_controller_init(&bt_cfg));
    ESP_ERROR_CHECK(esp_bt_controller_enable(ESP_BT_MODE_BLE));
    ESP_ERROR_CHECK(esp_bluedroid_init());
    ESP_ERROR_CHECK(esp_bluedroid_enable());
    bm_device_uuid(s_prov_uuid);

    ESP_ERROR_CHECK(esp_ble_mesh_register_prov_callback(prov_cb));
    ESP_ERROR_CHECK(esp_ble_mesh_register_config_server_callback(config_server_cb));
    ESP_ERROR_CHECK(esp_ble_mesh_register_health_server_callback(health_server_cb));
    ESP_ERROR_CHECK(esp_ble_mesh_register_custom_model_callback(custom_model_cb));
    ESP_ERROR_CHECK(esp_ble_mesh_init(&s_prov, &s_composition));

    if (esp_ble_mesh_node_is_provisioned()) {
        s_provisioned = true;
        s_primary_addr = esp_ble_mesh_get_primary_element_address();
        subscribe_telemetry_groups();
    } else {
        ESP_ERROR_CHECK(esp_ble_mesh_node_prov_enable(
            (esp_ble_mesh_prov_bearer_t)(ESP_BLE_MESH_PROV_ADV | ESP_BLE_MESH_PROV_GATT)));
    }
    ESP_LOGI(TAG, "gateway mesh initialized (provisioned=%d)", (int)s_provisioned);
}

bool bm_gateway_mesh_is_provisioned(void) {
    return s_provisioned;
}

uint16_t bm_gateway_mesh_primary_addr(void) {
    return s_primary_addr;
}

bool bm_gateway_mesh_send(uint16_t dst,
                          uint32_t opcode,
                          uint8_t ttl,
                          const uint8_t *payload,
                          uint16_t length) {
    if (!s_provisioned) {
        return false;
    }
    esp_ble_mesh_msg_ctx_t ctx = {
        .net_idx = BM_NET_IDX,
        .app_idx = BM_APP_IDX,
        .addr = dst,
        .send_ttl = ttl,
    };
    esp_err_t err = esp_ble_mesh_client_model_send_msg(
        &s_vendor_models[0], &ctx, opcode, length, (uint8_t *)payload, 0, false,
        ROLE_NODE);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "mesh send 0x%06lx: %s", (unsigned long)opcode,
                 esp_err_to_name(err));
        return false;
    }
    return true;
}
