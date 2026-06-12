# PulseRelay

English version: [README.md](README.md)

PulseRelay は、Bluetooth LE 心拍数デバイスと OSC 対応アプリをローカルでつなぐブリッジです。
Bluetooth 標準の **Heart Rate Service (0x180D)** を実装したトラッカーに直接接続し、
BPM を UDP 経由でローカルの OSC エンドポイント（例: VRChat）へ転送します。
リアルタイム経路にスマホ・クラウド・ベンダー API は一切使いません。

現在の状態: **デスクトップアプリ + CLI プローブ。** Avalonia 製デスクトップアプリが
Windows 11 上で実機トラッカーからライブ BPM を受信し、OSC へ転送できることを
Fitbit Charge 6 の実機で確認済みです。macOS/Linux ではシミュレーションソースで動作します
（Bluetooth LE 対応は現時点で Windows のみ）。

## クイックスタート（Windows 11）

```sh
dotnet run --project src/PulseRelay.Desktop -f net10.0-windows10.0.19041.0
```

1. **先にデバイス側で**心拍共有を開始します（下記の Charge 6 の手順を参照）。
2. PulseRelay の **Start** を押します。ダッシュボードにデバイス名・ライブ BPM・OSC の状態が表示されます。
3. OSC 出力は**初期状態でオン**です。送信先は `127.0.0.1:9000`、アドレスは
   `/avatar/parameters/VRCOSC/Heartrate/Value` です。

## Fitbit Charge 6 での使い方（例）

トラッカー側の操作は自動化できません。PulseRelay が代わりに Share を押すことは
できないため、毎回次の手順が必要です。

1. Charge 6 で「**HR on equipment**」（装置で心拍数の共有）タイルを開き、画面を点けたままにします。
2. 共有の確認が表示されたら **Share** を押し、続けて **Start** を押します。
3. PulseRelay 側の **Start** を押します。
4. 共有中はトラッカーをその状態のまま維持してください。Share 画面から離れると配信が
   止まります（共有を再開すれば PulseRelay が自動で再接続します）。

注意: Charge 6 が同時に接続できるのは **1 つ**のアプリ・機器だけです。他の PC や
ジム機器、CLI プローブなどの接続は先に終了してください。

標準の Heart Rate Service を公開している他のトラッカーでも、同様に各機器の
「心拍ブロードキャスト / 共有」機能を使えば動作するはずです。周囲に BLE デバイスが
複数ある場合は、設定でデバイス名フィルター（例: `Charge 6`）を指定してください。

## OSC 出力

| 設定 | 既定値 |
|---|---|
| ホスト | `127.0.0.1` |
| ポート | `9000` |
| アドレス | `/avatar/parameters/VRCOSC/Heartrate/Value`（int） |

いずれもアプリの設定（プローブは CLI フラグ）で変更できます。
詳細は [docs/vrchat-osc.md](docs/vrchat-osc.md) を参照してください。

## トラブルシューティング

- **デバイスが見つからない** — デバイス側で本当に共有が始まっているか確認してください
  （Charge 6 では「HR on equipment」画面を開いて Share を開始した状態）。PC の
  Bluetooth がオンであること、デバイスが PC の近くにあることも確認します。周囲に
  BLE デバイスが多い場合は、設定でデバイス名フィルターを指定してください。
- **デバイス名が一般的な表示になる** — 接続後はデバイスのアドバタイズ名
  （例: `BLE Charge 6`）が表示され、取得できない場合はデバイス名フィルター、
  それも無ければ「Bluetooth LE デバイス」と表示されます。接続中に
  `BLE <unknown>` と表示されることはなくなりました。もし表示された場合は報告してください。
- **OSC が届かない** — 受信側アプリが設定どおりのホスト/ポートで待ち受けているか
  確認してください（VRChat はアクションメニューで OSC を有効化。既定ポートは 9000）。
  OSC アドレスが受信側の期待と一致しているかも確認します。送信エラーは Output カードに
  表示されます。
- **接続直後に「デバイスとの接続が切れました」** — デバイスが別のアプリ・機器に
  接続されたままの可能性が高いです。他の接続をすべて切り、共有をやり直してから
  もう一度 Start してください。
- **10 秒ほどデータが来ない** — ダッシュボードがデータ途絶を表示し、30 秒続くと
  自動で再接続します。トラッカーによっては最初の計測値まで 20 秒ほどかかることが
  ありますが、正常です。

## プロジェクト構成

| プロジェクト | ターゲット | 役割 |
|---|---|---|
| `src/PulseRelay.Core` | `net10.0` | プラットフォーム非依存のモデル、0x2A37 パーサー、ソースインターフェース、モックソース |
| `src/PulseRelay.Osc` | `net10.0` | 最小限の OSC 1.0 エンコーダー + UDP 送信 |
| `src/PulseRelay.WindowsBle` | `net10.0-windows10.0.19041.0` | WinRT GATT クライアント（スキャン・接続・購読） |
| `src/PulseRelay.App` | `net10.0` | ブリッジのセッション/スーパーバイザー、設定、ローカライズ |
| `src/PulseRelay.Desktop` | 両方 | Avalonia デスクトップアプリ（ダッシュボード） |
| `src/PulseRelay.Probe` | 両方 | CLI: `scan` / `connect` / `mock` |
| `tests/PulseRelay.Tests` | `net10.0` | xunit ユニットテスト + ヘッドレス UI テスト |

設計は [docs/architecture.md](docs/architecture.md) を参照してください。

## ビルド

どのプラットフォームでも可（macOS/Linux/Windows）。.NET 10 SDK が必要です
（`global.json` でバージョンを固定）。

```sh
dotnet build PulseRelay.CrossPlatform.slnf   # クロスプラットフォームのプロジェクト + テスト
dotnet test  PulseRelay.CrossPlatform.slnf
dotnet build PulseRelay.sln                  # Windows BLE を含む全体（ビルドはどこでも可、BLE の実行は Windows のみ）
```

## CLI プローブの実行

任意のプラットフォーム — 合成心拍を生成し、必要なら OSC へ転送:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0 -- mock --osc
```

Windows 11 — 実機 BLE（詳細な手順は
[docs/charge6-verification.md](docs/charge6-verification.md)）:

```sh
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- scan --service 180D --verbose
dotnet run --project src/PulseRelay.Probe -f net10.0-windows10.0.19041.0 -- connect --name "Charge 6" --verbose
```

PulseRelay はリアルタイム BPM の取得に Fitbit Web API・Google Health API・クラウド
サービスを使いません。また Bluetooth アドレスを保存することもありません
（トラッカーのアドレスはローテーションする RPA であり、恒久的な ID ではないため）。

## ライセンス

MIT — [LICENSE](LICENSE) を参照してください。
