# BalloonMesh Debug Tool

BalloonMesh サーバと BLE Mesh Gateway の動作を、実機なしで確認するための
クロスプラットフォーム・デスクトップツールです。UI は **Avalonia UI 12**、
ターゲットは **.NET 10** で、Windows 10/11 と macOS (Intel / Apple Silicon)
で同じコードが動作します。

## 機能

- **接続**: サーバ URL、Gateway 共有トークン、Session Cookie を指定。
  Development 環境では Mock ログイン（Admin / Operator / Viewer）が使えます。
- **Fleet**: バルーン一覧（位置・色・キュー・Online）、Gateway 一覧、
  予約済み Show step の表示とキャンセル。バルーンを選ぶと Commands タブの
  ターゲットに反映されます。
- **Commands**: LED 即時反映、予約、キャンセル、RESET / RESET_AND_SET。
  送信結果の opcode / payload / 受信 Gateway 数を表示します。
- **Gateway simulator**: WebSocket または HTTP ポーリングで擬似 Gateway を
  起動し、サーバからの下りコマンドを Protocol B としてデコード表示します。
  受信一覧の **Open** で payload を Protocol タブへ送れます。
  FTM_REPORT、QUEUE_STATUS、NEIGHBOR_REPORT を手動またはバッチで注入できます。
  HTTP ポーリングの TX/RX raw JSON は、実際の HTTP バッチではなく
  メッシュメッセージ単位に合成した `type=mesh` フレームです。
- **Protocol**: 既知の Vendor Opcode をプリセットから生成し、任意の
  opcode / payload をデコードします。長さ不一致や非 Vendor Opcode は警告として
  表示し、デバッグを止めません。
- **Decoded log**: 送信 (TX)、受信 (RX)、手動デコード (DECODE) を時系列で
  記録します。opcode / opcode 名 / 全フィールド / raw JSON を表示し、
  direction・transport・検索で絞り込めます。表示中のログはファイルへ保存できます。
- **Log**: 最大 500 行の操作ログ。設定は秘密情報を除いて保存されます。

## 前提

- .NET 10 SDK / Runtime
- 起動済みの BalloonMesh サーバ（PostgreSQL を含む）
- Mock ログインを使う場合は `Authentication:Mock:Enabled=true`
  （`appsettings.Development.json` では既定で有効）

## 起動

Windows / macOS のどちらでも同じコマンドです。

```bash
dotnet run --project tools/BalloonMesh.DebugTool/BalloonMesh.DebugTool.csproj
```

1. Server URL（既定 `http://localhost:5080`）を確認して **Connect**。
2. Development サーバなら **Mock login** を押すか、
   別環境なら Session Cookie を貼り付けてから **Connect / Refresh**。
3. Gateway 共有トークンが必要な環境では **Gateway token** を入力してください。

## 配布用ビルド

フレームワーク依存（.NET 10 ランタイムが必要）:

```powershell
# Windows (PowerShell)
dotnet publish tools/BalloonMesh.DebugTool/BalloonMesh.DebugTool.csproj -c Release -r win-x64 --self-contained false
```

```bash
# macOS (Intel / Apple Silicon)
dotnet publish tools/BalloonMesh.DebugTool/BalloonMesh.DebugTool.csproj -c Release -r osx-x64 --self-contained false
dotnet publish tools/BalloonMesh.DebugTool/BalloonMesh.DebugTool.csproj -c Release -r osx-arm64 --self-contained false
```

自己完結（.NET ランタイムを同梱）にする場合は `--self-contained true` を
指定します。出力は `bin/Release/net10.0/<RID>/publish` です。

## テスト

```bash
dotnet test tools/BalloonMesh.DebugTool.Tests/BalloonMesh.DebugTool.Tests.csproj
```

Protocol のエンコード / デコード、HTTP クライアント、Gateway JSON エンベロープ、
設定ファイルの読み書きをテストします。サーバや PostgreSQL は不要です。

## 設定ファイル

秘密情報以外の入力値は OS の ApplicationData 配下に保存されます。

- Windows: `%APPDATA%\BalloonMesh\debugtool.json`
- macOS / Linux: `~/.config/BalloonMesh/debugtool.json`

Gateway トークンと Session Cookie は保存しません。起動のたびに入力してください。

## 実装メモ

- Protocol B のワイヤフォーマットは `server/BalloonMesh.Protocol` の
  `BalloonProtocol` をサーバと共有しています。デバッグツール側で
  コーデックを複製していないため、サーバの変更に追従します。
- Gateway simulator の WebSocket は実機と同じ `hello` / `type=mesh`
  エンベロープを使用します。HTTP モードは `/api/gateway/{id}/events` と
  `/api/gateway/{id}/commands` を使用します。
