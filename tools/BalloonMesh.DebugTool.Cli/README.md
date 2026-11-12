# BalloonMesh Debug CLI

BalloonMesh サーバと BLE Mesh Gateway を、GUI なしで確認するための
コマンドラインツールです。ターゲットは **.NET 10** で、Windows / macOS / Linux
の同じコマンドで動作します。通信と Protocol B のコーデックは
`BalloonMesh.DebugTool.Core` を GUI 版と共有しています。

## できること

- サーバのヘルスチェック、Fleet 状態、予約済み Show step の表示 / キャンセル
- LED の即時反映 / 予約 / キャンセル / RESET / RESET_AND_SET
- WebSocket または HTTP ポーリングの擬似 Gateway を起動し、下りコマンドを
  Protocol B としてデコード表示
- 擬似 Gateway からの FTM / QUEUE_STATUS / NEIGHBOR_REPORT / TIME_REPORT などの注入
- Protocol B のオフライン encode / decode（サーバ不要）
- `--json` による機械可読出力（`gateway run` は JSON Lines）

## 前提

- .NET 10 SDK / Runtime
- サーバ API を使う場合は起動済みの BalloonMesh サーバ（PostgreSQL を含む）
- Mock ログインを使う場合は `Authentication:Mock:Enabled=true`
  （`appsettings.Development.json` では既定で有効）

## 起動

```bash
dotnet run --project tools/BalloonMesh.DebugTool.Cli/BalloonMesh.DebugTool.Cli.csproj -- --help
```

何度も使う場合は build 済みの実行ファイルを直接呼ぶと高速です。

```powershell
dotnet build tools/BalloonMesh.DebugTool.Cli/BalloonMesh.DebugTool.Cli.csproj
tools/BalloonMesh.DebugTool.Cli/bin/Debug/net10.0/BalloonMesh.DebugTool.Cli.exe --help
```

以降の例では `balloonmesh-debug` を実行ファイル（または
`dotnet run --project ... --`）の読み替えとして記載します。

## グローバルオプション

| オプション | 説明 |
|---|---|
| `--server <url>` | サーバのベース URL。既定は `http://localhost:5080` |
| `--role <role>` | Development の Mock ログイン。`Admin` / `Operator` / `Viewer` |
| `--cookie <value>` | Mock ログインが無い環境用の生の Cookie ヘッダ |
| `--gateway-token <token>` | Gateway エンドポイント用の `X-Gateway-Token` |
| `--json` | テキストではなく JSON を出力 |
| `--verbose` | `gateway run` のテキスト出力に raw JSON を追加 |
| `-h`, `--help` | ヘルプを表示 |

環境変数 `BALLOONMESH_SERVER`、`BALLOONMESH_ROLE`、`BALLOONMESH_COOKIE`、
`BALLOONMESH_GATEWAY_TOKEN` でも指定できます。トークンと Cookie は保存しません。

`--role` と `--cookie` は同時に指定できません。

## コマンド

### health / state / show

```bash
balloonmesh-debug --role Admin health
balloonmesh-debug --role Admin state
balloonmesh-debug --role Admin show --limit 20
balloonmesh-debug --role Admin show --cancel 42
```

`show --cancel` はキャンセル結果を表示し、拒否された場合は終了コード 1 を返します。

### led

```bash
# 即時反映
balloonmesh-debug --role Admin led now --target-kind balloon --target 0x0101 --color "#FF8000"

# 10 秒後に予約（--at を省略した場合の既定値）
balloonmesh-debug --role Admin led set --target-kind all --target all --color 255,128,0 --at +10s

# 予約のキャンセル（予約時と同じ execute tick を指定）
balloonmesh-debug --role Admin led cancel --target-kind balloon --target 0x0101 --at 2026-01-01T00:00:10Z

# RESET / RESET_AND_SET
balloonmesh-debug --role Admin led reset --target-kind all --target all --cutoff now
balloonmesh-debug --role Admin led reset --target-kind all --target all --set-now --color "#FFFFFF"
```

- `--color` は `#RRGGBB`、`RRGGBB`、`r,g,b` を受け付けます。`--r` / `--g` / `--b` でも指定できます。
- `--at` / `--cutoff` は ISO-8601、`now`、`+500ms` / `+10s` / `+2m` / `+1h` を受け付けます。

### gateway run

擬似 Gateway を起動し、サーバからの下りコマンドをデコード表示します。

```bash
# WebSocket で接続し、Ctrl+C まで待機
balloonmesh-debug --gateway-token secret gateway run --id cli-gw

# 60 秒だけ動かし、FTM レポートとキュー状態を注入
balloonmesh-debug --gateway-token secret gateway run --id cli-gw --duration 60 \
  --event ftm:anchor=1,distance=3000,batch=1 \
  --event queue:kind=nearfull,count=401

# HTTP ポーリング
balloonmesh-debug --gateway-token secret gateway run --id cli-gw --transport http --poll-ms 5000
```

| オプション | 説明 |
|---|---|
| `--id <id>` | Gateway ID。既定は `debug-gateway` |
| `--transport <ws\|http>` | WebSocket（既定）または HTTP ポーリング |
| `--poll-ms <n>` | HTTP の long-poll 待機時間（既定 10000） |
| `--firmware <text>` | 報告するファームウェア文字列 |
| `--spool <n>` | 報告する spool バックログ |
| `--no-provisioned` | 未プロビジョニングとして報告 |
| `--event <spec>` | 接続後に注入するイベント。繰り返し指定可 |
| `--event-delay <ms>` | 注入間隔（既定 500） |
| `--duration <seconds>` | 指定秒数で終了。省略時は Ctrl+C まで待機 |

`--event` の書式:

```text
raw:opcode=0xC102E5,payload=...,src=0x0101,dst=0xC011,rssi=-45,ttl=3
ledset:r=255,g=128,b=0,at=+10s
lednow:r=255,g=255,b=255
ledcancel:at=2026-01-01T00:00:10Z
ledreset:cutoff=now
ftm:anchor=1,distance=3000,batch=1[,timestamp=0x12345678]
queue:kind=nearfull|recovered,count=401
neighbor:batch=1,index=0,neighbor=0x0102@-45[,neighbor=0x0103@-60]
time:at=now
neighbor_request | neighbor_probe | time_request
```

共通キー `src` / `dst` / `rssi` / `ttl` は全イベントで上書きできます
（既定 `src=0x0101`、`dst=0xC011`、`rssi=-45`、`ttl=3`）。

### protocol

サーバ不要で Protocol B を扱えます。

```bash
balloonmesh-debug protocol ops
balloonmesh-debug protocol decode --opcode 0xC102E5 --payload E821F8BB00102030
balloonmesh-debug protocol encode --op LED_SET --at +10s --color "#00FF00"
balloonmesh-debug protocol encode --op FTM_REPORT --anchor 1 --distance 3000 --batch 2
balloonmesh-debug protocol encode --op QUEUE_STATUS --kind recovered --count 0
balloonmesh-debug protocol encode --op NEIGHBOR_REPORT \
  --neighbor 0x0102@-45 --neighbor 0x0103@-60
balloonmesh-debug protocol encode --op 0x0A --payload 0102030405060708
```

`--op` は `protocol ops` に表示される名前、または生の vendor opcode
（`0x00`〜`0x3F`）です。生 opcode の場合は `--payload` をそのまま使います。

## 出力と終了コード

- テキスト出力は人間向け、`--json` は機械可読です。
- `gateway run --json` は 1 行 1 オブジェクトの JSON Lines を出力します。
- 終了コード: `0` 成功、`1` API / 通信エラーまたは拒否、`2` 引数エラー、
  `130` Ctrl+C。

## テスト

```bash
dotnet test tools/BalloonMesh.DebugTool.Cli.Tests/BalloonMesh.DebugTool.Cli.Tests.csproj
```

引数パーサ、値のパース、`--event` の展開、Protocol B の encode、終了コードを
サーバなしでテストします。
