# Swift UI 連携ガイド (AR-runner ⇄ AR Pacesetter)

[kyainna/AR-runner](https://github.com/kyainna/AR-runner) のSwiftUIアプリと、本Unityプロジェクトを
**Unity as a Library (UaaL)** で連携するための手順とメッセージ契約。

## アーキテクチャ

```
┌─ SwiftUI (AR_Runner_UI) ──────────────┐      ┌─ Unity (AR Pacesetter) ────────────┐
│ ConnectOnboardingView                 │      │ ARSessionManagerBridge.cs          │
│ CourseRunningView                     │      │   (GameObject "ARSessionManager")  │
│ StatsHistoryView                      │      │ DeviceManagerBridge.cs             │
│                                       │      │   (GameObject "DeviceManager")     │
│ UnityBridge.swift ──sendMessageToGO──▶│──────▶ OnSwiftCommand(json)               │
│ onUnityMessage(_:) ◀─NSNotification──│◀──────  SwiftMessageSender.cs             │
│                     "UnityToSwiftMessage"     │   + Plugins/iOS/UnitySwiftBridge.mm│
└───────────────────────────────────────┘      └────────────────────────────────────┘
```

## メッセージ契約

### Swift → Unity (`OnSwiftCommand` にJSON文字列)

| ターゲットGameObject | command | フィールド | Unity側の動作 |
|---|---|---|---|
| `ARSessionManager` | `StartSession` | `targetPaceKmH`, `distanceKm`, `avatarHeightCm`, `forwardOffsetM` | ペース換算(60/kmh→分/km)・先行距離・身長スケール適用 → `StartPacing()`。UnityのセットアップUIは非表示 |
| `ARSessionManager` | `UpdateMetrics` | `paceKmH`, `heartRate`, `distanceKm` | 心拍→発光/HUD/バイタル警告、距離→1km/5kmスプリット判定 |
| `ARSessionManager` | `EndSession` | — | 走行終了・セッション保存 → `SessionEnded` イベント返信 |
| `DeviceManager` | `ConnectXREAL` | — | ReadyチェックのARグラスをConnectedへ |

### Unity → Swift (NSNotification `UnityToSwiftMessage` → `onUnityMessage`)

| event | フィールド | 送信タイミング |
|---|---|---|
| `SyncRateUpdated` | `value` (int 0-100) | 走行中 1Hz |
| `AvatarStateChanged` | `state` = Idle/Run/Slow/Fast/Goal/Lost | 状態変化時 |
| `GPSLost` / `GPSRecovered` | — | GPS FSM遷移時 |
| `LatencyReport` | `ms` (double) | 走行中 1Hz(平滑化フレーム時間) |
| `SessionEnded` | `grade`, `rank`, `averageSync`, `distanceKm`, `elapsedSeconds` | EndSession応答 |

## セットアップ手順

### 1. Unity側 (このリポジトリ — 対応済み)

追加作業なし。以下が起動時に自動生成・動作します:
- `ARSessionManager` / `DeviceManager` GameObject([ARVisionSystemsBootstrap.cs](Assets/_Project/Scripts/ARVisionSystemsBootstrap.cs))
- 送信ブリッジ [SwiftMessageSender.cs](Assets/_Project/Scripts/SwiftMessageSender.cs) + [UnitySwiftBridge.mm](Assets/Plugins/iOS/UnitySwiftBridge.mm)

iOSビルド: **File → Build Profiles → iOS** で Export。生成された `Unity-iPhone.xcodeproj` の
`UnityFramework` をSwiftアプリのワークスペースへ組み込む(UaaL公式手順:
https://docs.unity3d.com/Manual/UnityasaLibrary-iOS.html)。

### 2. Swift側 (AR-runner リポジトリ)

`AR_Runner_UI/UnityBridge.swift` を [Docs/Swift/UnityBridge.swift](Docs/Swift/UnityBridge.swift) で**置き換える**。
変更点は3つだけ:
1. `init()` で NSNotification `UnityToSwiftMessage` を購読 → `onUnityMessage` へ転送
2. `sendToUnity` が `#if canImport(UnityFramework)` で本番は `sendMessageToGO`、未リンク時は従来のシミュレータにフォールバック
3. `SessionEnded` イベント受信(`lastResult: SessionResult?` published追加)

UnityFrameworkを組み込むまでは従来どおりシミュレーションで動くので、UI開発は今まで通り継続できます。

## エディタでの連携テスト (Unity単体)

1. `SampleScene` を再生
2. Hierarchyで `ARSessionManager` を選択 → Inspector右上の「⋮」→
   - **Simulate StartSession (12km/h = 5:00/km)** — Swiftからの開始をシミュレート
   - **Simulate UpdateMetrics (HR 150)** — 心拍・距離の注入
   - **Simulate EndSession** — 終了+結果送信
3. Consoleに `[Unity → Swift] {"event":...}` が1Hzで出力されれば送信側もOK

## 既知の制約 / TODO

- `distanceKm`(目標距離)は現在ゴール判定に未使用(Swift側がendSessionを送る設計)
- `LatencyReport` は実測Motion-to-Photonではなく平滑化フレーム時間(実機ではLatencyBenchmarkRunnerと統合予定)
- `ConnectXREAL` は実際のXREAL SDK初期化ではなくReadyチェック状態の更新のみ(SDK導入後にDeviceManagerBridgeへ実装)
