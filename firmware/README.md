# Firmware projects

デバイスごとに独立した PlatformIO プロジェクトです。共通ロジックは `shared/`、native テストは `tests/` に分離しています。

```text
firmware/
  balloon/    ESP32-C3 バルーン
  anchor/     GPS 基準局
  gateway/    Mesh Gateway
  shared/
    balloon_core/    機種非依存 C (protocol / LED queue / NMEA / neighbor)
    balloon_common/  共有 ESP32 設定と Protocol A codec
  tests/      native Unity テスト
```

## Build all projects

```powershell
.\build-all.ps1
```

## Build individually
```powershell
cd balloon
pio run
pio run -t upload

cd ../anchor
pio run
pio run -t upload

cd ../gateway
pio run
pio run -t upload
```

## Native tests

```powershell
$env:PATH = "C:\msys64\mingw64\bin;$env:PATH"   # Windows / MSYS2 gcc
cd tests
pio test -e native
```

## 設定

### balloon
- `firmware/shared/balloon_common/include/bm_config.h`
  - 基準局 SSID / UDP ポート / トークン
  - LED GPIO
  - Group Address / Model ID / Company ID

### anchor
- `platformio.ini` の `-DBM_ANCHOR_ID=N`
- GPS UART ピンと SoftAP 設定は `bm_config.h`

### gateway
- `platformio.ini` の `BM_GATEWAY_USE_WEBSOCKET=1`（既定）
  - `0` にすると HTTP ポーリングへ切り替え
- `bm_config.h` の `BM_GATEWAY_ID` / `BM_SERVER_URL` / Wi-Fi 設定
- WSS/HTTPS の CA は `-DBM_TLS_CA_CERT=\"...\"` で指定

## Arduino BLE Mesh の注意

Arduino-ESP32 のプリビルド `libbt.a` は `CONFIG_BLE_MESH_NODE` が無効な sdkconfig でビルドされています。`balloon` / `gateway` はヘッダ構造体を有効化するため `-DCONFIG_BLE_MESH_NODE=1` を付けていますが、実機 Provisioning はプリビルド構成に依存します。詳細は `../docs/decisions.md` を参照してください。
