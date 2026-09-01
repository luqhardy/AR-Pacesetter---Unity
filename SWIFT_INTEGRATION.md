# Swift UI 連携ガイド (モノレポ構成 / Unity as a Library)

SwiftUIアプリとUnityを**1つのリポジトリ・1つのXcodeアプリ**として管理する。
SwiftUI側のオリジナルは [kyainna/AR-runner](https://github.com/kyainna/AR-runner)(`ios/AR_Runner_UI/` に取り込み済み)。

## リポジトリ構成

```
AR Pacesetter/                        ← リポジトリルート = Unityプロジェクト
├── Assets/ ProjectSettings/ ...      ← Unity本体
│   └── Editor/IOSBuildExporter.cs    ← メニュー Build → Export iOS
├── ios/
│   ├── ARRunner.xcworkspace          ← ★ Macで開くのはこれ
│   ├── AR_Runner_UI/                 ← SwiftUIアプリ (ホスト側)
│   │   ├── AR_Runner_UI.xcodeproj
│   │   └── AR_Runner_UI/
│   │       ├── UnityBridge.swift     ← 双方向メッセージ (本番配線済み)
│   │       ├── UnityLauncher.swift   ← UnityFramework起動 + UnityContainerView
│   │       └── (各View).swift
│   └── UnityExport/                  ← Unityエクスポート産物 (gitignore・生成物)
│       └── Unity-iPhone.xcodeproj    ← Build → Export iOS で生成される
└── SWIFT_INTEGRATION.md              ← このファイル
```

**考え方**: 最終アプリは常に `AR_Runner_UI` スキームでビルドする。
Unityは「エクスポートして `ios/UnityExport/` に置かれる部品(UnityFramework)」であり、
Unity単体をビルドしてもSwiftUI画面は含まれない。

## ビルド手順

### ① Unityエクスポート (Windows可)

Unityメニュー **Build → Export iOS (ios/UnityExport)** を実行。
`ios/UnityExport/Unity-iPhone.xcodeproj` が生成される。

- **Unityバージョン: 6000.3.17f1**(`ProjectSettings/ProjectVersion.txt` と一致必須。
  CIも `unityci/editor:ubuntu-6000.3.17f1-base-3` を使用)
- **iOS Build Support モジュール必須**(Unity Hub → インストール → 対象バージョン → モジュールを加える)
- **Player Settings の使用目的文が空だとエクスポートは失敗する**: `Microphone`/`Location`/`Bluetooth` を
  スクリプトが使う場合、Unity側(`ProjectSettings`)の Usage Description が空のままだと
  `BuildFailedException` になる(Swiftアプリ側の`INFOPLIST_KEY_*`とは別物で、両方必要)。設定済み
- バッチ実行: `Unity.exe -batchmode -quit -projectPath <repo> -executeMethod IOSBuildExporter.ExportIOS`
  — 失敗時は終了コード1を返す
- **エクスポート後は `ProjectSettings/ProjectSettings.asset` の差分を必ず確認すること**:
  ビルド処理が `preloadedAssets` を空にすることがある。ここには
  `Assets/XR/XRGeneralSettings.asset` が含まれており、**空のままコミットするとプレイヤービルドで
  XR(ARKit)ローダーが初期化されなくなる**。空になっていたら復元してからコミットする
- エクスポート対象シーンは `EditorBuildSettings` の有効シーン。空の場合は
  `Assets/Scenes/SampleScene.unity` にフォールバックする([IOSBuildExporter.cs](Assets/Editor/IOSBuildExporter.cs))

### ② Xcodeで統合ビルド (Mac)

1. `ios/ARRunner.xcworkspace` を開く(両プロジェクトが並んで表示される)
2. **初回のみ**: `AR_Runner_UI` ターゲット → General → *Frameworks, Libraries, and Embedded Content* →
   `Unity-iPhone` プロジェクト内の **UnityFramework.framework** を追加し **Embed & Sign** に設定
3. Unity-iPhone側: `Data` フォルダの Target Membership を **UnityFramework** に変更(UaaL公式手順)
4. Signing: Automatically manage signing + Team設定
5. スキーム `AR_Runner_UI` を選択 → **iPhone実機**でRun(ARKit/GPSはシミュレータ不可)

公式リファレンス: https://docs.unity3d.com/Manual/UnityasaLibrary-iOS.html

Unityを再エクスポートしても手順2-3の設定は`Unity-iPhone.xcodeproj`側に保持される
(まっさらに消して再生成した場合のみ再設定)。

### ③' ARグラスへの出力(外部ディスプレイ・SDK不要)

XREAL One は iPhone(USB-C)に対して**外部ディスプレイ**として振る舞うため、NRSDKなしで
「グラスにARビュー・iPhoneに操作パネル」の構成が動作する(`ExternalDisplayManager.swift`):

- グラス接続 → iOSが外部ディスプレイシーンを生成 → UnityのARビューを自動でグラス側ウィンドウへ移設
- 切断 → 走行画面の`UnityContainerView`が自動でiPhone側へ回収
- 接続検知は DeviceConnectView のARグラス行に自動反映され、Unityの`ConnectXREAL`も送信される
- グラス出力中、iPhoneの走行画面は「ARビューはグラスに出力中」表示+HUD操作に切替

実機確認: iPhone 15以降(USB-C) + XREAL One を接続し、走行画面でグラス側に
Unityの映像が出ること。3DoF頭部追従はグラス側X1チップのネイティブ機能で行われる。

### ③ SwiftUIからUnityを表示

```swift
// 走行画面に遷移する前に
UnityLauncher.shared.launch()

// SwiftUI内でARビューを表示
UnityContainerView()
    .ignoresSafeArea()
```

`UnityFramework`未リンクのビルド(シミュレータ・UI単体開発)では自動でプレースホルダー表示と
シミュレーションモードにフォールバックするため、SwiftUI開発は従来通り継続できる。

### ④ 走行ログCSVの取り出し(F-11 — PoC成果物の回収手順)

基本設計書が「実証実験の技術限界データ蓄積 = ソラド社への技術資産譲渡の基盤」と位置づける
F-11のCSVは**実機のアプリコンテナ内**に出力されるため、実地テスト後に取り出す手順が要る。

- **出力先**: `Application.persistentDataPath/RunLogs/Log_YYYYMMDD_HHMMSS.csv`
  (UnityのiOSにおける`persistentDataPath`はアプリコンテナの `Documents/`。
  実体は `/var/mobile/Containers/Data/Application/<GUID>/Documents/RunLogs/`)
- **取り出し(現状の手順・追加設定なしで可能)**:
  1. iPhoneをMacへ接続 → Xcode **Window → Devices and Simulators**
  2. 対象デバイス → *Installed Apps* → `AR_Runner_UI` を選択 → 歯車 → **Download Container…**
  3. 保存された `.xcappdata` を右クリック → **パッケージの内容を表示** →
     `AppData/Documents/RunLogs/` に `Log_*.csv` が入っている
  4. `Docs/field-tests/YYYYMMDD/` へ格納(FIELD_TEST_PLAN.md §5)
- **推奨(未適用・要チーム判断)**: `ios/AR_Runner_UI/Info.plist` に `UIFileSharingEnabled` と
  `LSSupportsOpeningDocumentsInPlace` を **true** で追加すると、`RunLogs/` が iOS「ファイル」アプリの
  *このiPhone内* に現れ、**Mac無しでその場で共有できる**(AirDrop/メール送信可)。
  コード変更は不要で、`GENERATE_INFOPLIST_FILE=YES` 環境でも既存キーとマージされる。
  実地テストの回転を上げたい場合は適用を検討すること。

> `imu_accel_x/y/z` 列は実機では **CoreMotion の実測値**(`Input.gyro.userAcceleration` を
> 100Hzで取得し m/s² へ換算)。エディタはジャイロが無いためカメラ速度差分の近似になる。
> 供給元は `RunTelemetryLogger.ImuSource`(`device` / `external` / `approximated`)で判別できる。
> GPS列(`gps_latitude`/`gps_longitude`)は`UpdateMetrics`経由で実測値が入る。

## アーキテクチャ

```
┌─ SwiftUI (ios/AR_Runner_UI) ──────────┐      ┌─ Unity (UnityFramework) ───────────┐
│ ConnectOnboardingView                 │      │ ARSessionManagerBridge.cs          │
│ CourseRunningView + UnityContainerView│      │   (GameObject "ARSessionManager")  │
│ StatsHistoryView                      │      │ DeviceManagerBridge.cs             │
│ UnityLauncher.swift (runEmbedded)     │      │   (GameObject "DeviceManager")     │
│                                       │      │                                    │
│ UnityBridge.swift ──sendMessageToGO──▶│──────▶ OnSwiftCommand(json)               │
│ onUnityMessage(_:) ◀─NSNotification──│◀──────  SwiftMessageSender.cs             │
│                     "UnityToSwiftMessage"     │   + Plugins/iOS/UnitySwiftBridge.mm│
└───────────────────────────────────────┘      └────────────────────────────────────┘
```

## メッセージ契約

### Swift → Unity (`OnSwiftCommand` にJSON文字列)

| ターゲットGameObject | command | フィールド | Unity側の動作 |
|---|---|---|---|
| `ARSessionManager` | `StartSession` | `targetPaceKmH`, `distanceKm`, `avatarHeightCm`, `forwardOffsetM`, `mode`(任意: "ghost"), `ghostDateIso`(任意) | ペース換算(60/kmh→分/km)・先行距離・身長スケール適用 → `StartPacing()`。`mode:"ghost"`なら過去セッションの速度プロファイルでアバターを駆動(ゴースト競走)。UnityのセットアップUIは非表示 |
| `ARSessionManager` | `UpdateMetrics` | `paceKmH`, `heartRate`, `distanceKm`, `gpsLatitude`, `gpsLongitude`, `gpsAccuracy` | 心拍→発光/HUD/バイタル警告、距離→1km/5kmスプリット判定。測位3値は`GpsSignalMonitor`(F-09 §8.1のロスト判定)とF-11 CSVログのGPS列へ供給される。**`gpsAccuracy > 0` が有効サンプルの目印**(未送信/負値は無効として完全に無視され、エディタ単体走行を阻害しない)<br>**測位3値は「前回と異なる新しいfixのとき」だけ添付すること。** 同じfixを毎秒再送するとUnity側の更新途絶タイマーが際限なくリセットされ、F-09の「1.5秒途絶」判定が永久に成立しなくなる。`distanceKm` は**実測のみ**を送る(推定距離を送るとゴール判定とHealthKitへ混入する) |
| `ARSessionManager` | `EndSession` | — | 走行終了・セッション保存 → `SessionEnded` イベント返信 |
| `ARSessionManager` | `RequestHistory` | — | 保存済みセッション(新しい順・最大20件)を `HistoryData` で返信 |
| `ARSessionManager` | `ResumeSession` | — | §8.3: グラス再接続後、**準備画面からの再スタート操作**でスタンバイ中の表示のみ復帰(新規セッションは開始せず記録は継続) |
| `DeviceManager` | `ConnectXREAL` | — | ReadyチェックのARグラスをConnectedへ |
| `DeviceManager` | `DisconnectXREAL` | — | §8.3: スタンバイ移行でアバターを消去。**走行セッションは終了させない**ためF-11のCSVログはBG継続。再接続だけではアバターを復帰させない(安全のため`ResumeSession`が必要) |

`StartSession`は前セッションが終了済みの場合、全コンポーネント(エンジン・集計・HUD・
セーフティログ・音響)を自動リセットしてから開始する — **同一起動内での再走行に対応**。

### Unity → Swift (NSNotification `UnityToSwiftMessage` → `onUnityMessage`)

| event | フィールド | 送信タイミング |
|---|---|---|
| `SyncRateUpdated` | `value` (int 0-100) | 走行中 1Hz |
| `AvatarStateChanged` | `state` = Idle/Run/Slow/Fast/Goal/Lost | 状態変化時 |
| `GPSLost` / `GPSRecovered` | — | GPS FSM遷移時 |
| `LatencyReport` | `ms` (double) | 走行中 1Hz(平滑化フレーム時間) |
| `SessionEnded` | `grade`, `rank`, `averageSync`, `distanceKm`, `elapsedSeconds`, `calories` | EndSession応答 |
| `HistoryData` | `sessions`: [{`dateIso`, `distanceKm`, `elapsedSeconds`, `averageSync`, `grade`}] | RequestHistory応答 |
| `LowBattery` | — | **現在発火しない** — 唯一の送出元 `SafetyAndSystemController` が実行時に生成されないため(HANDOVER.md §5)。Swift側の購読は将来の有効化に備えて残置。※HUDのバッテリー黄色点滅(`PeripheralHUDManager`)は別実装で正常動作 |
| `VoiceAlert` | `kind`("Signal"/"Intersection"), `ttc` | 音声警告要求 → Swift側`VoiceAlertSpeaker`がAVSpeechSynthesizerで発話。重複時はTTCが短い方が割込(企画書4.3)。信号は長め振動併用 |

Unity側の受信オブジェクト(`ARSessionManager`/`DeviceManager` GameObject)は
[ARVisionSystemsBootstrap.cs](Assets/_Project/Scripts/ARVisionSystemsBootstrap.cs) が起動時に自動生成する。シーン配線は不要。

## エディタでの連携テスト (Unity単体・Windows可)

1. `SampleScene` を再生
2. Hierarchyで `ARSessionManager` を選択 → Inspector右上の「⋮」→
   - **Simulate StartSession (12km/h = 5:00/km)** — Swiftからの開始をシミュレート
   - **Simulate UpdateMetrics (HR 150)** — 心拍・距離の注入
   - **Simulate EndSession** — 終了+結果送信
3. Consoleに `[Unity → Swift] {"event":...}` が1Hzで出力されれば送信側もOK

## AR-runnerリポジトリとの関係

`ios/AR_Runner_UI/` は kyainna/AR-runner のスナップショット取り込み+以下の変更:
- `UnityBridge.swift` — 本番配線版に置き換え(NSNotification購読・sendMessageToGO・SessionEnded受信)
- `UnityLauncher.swift` — 新規(UnityFramework起動・UnityContainerView)
- `AR_Runner_UIApp.swift.swift` → `AR_Runner_UIApp.swift` にリネーム
- `PROJECT_SETUP.md` 削除(本ファイルに統合)

以後の UI開発はこのリポジトリの `ios/` で行い、AR-runner側には必要に応じて還元する。
Xcode 16形式(FileSystemSynchronizedRootGroup)のため、`ios/AR_Runner_UI/AR_Runner_UI/` に
.swiftファイルを置くだけでビルド対象になる。

## ゴール判定(実装済み)

`StartSession`の`distanceKm`を目標距離としてUnity側([ARSessionManagerBridge.cs](Assets/_Project/Scripts/ARSessionManagerBridge.cs))が監視:
- 距離ソースはSwift報告値(CoreLocation・UpdateMetrics)とUnity内計測の**大きい方**
- 到達すると自動で`EndSession`相当の終了処理 → `AvatarStateChanged: Goal` + `SessionEnded`をSwiftへ送信
- Swift側: RunningViewが`avatarState == .goal`を検知 → GOALオーバーレイ表示(2.5秒) → 統計画面へ自動遷移
- エディタ検証: `ARSessionManager`のコンテキストメニュー「Simulate Goal Reached」

## ゴースト機能(実装済み・企画書§3)

過去の自分と競走する。走行中は5秒毎に累積距離をサンプリングして`paceTimeline`として保存し、
履歴画面の「この記録と競走（ゴースト）」でその速度プロファイルを再生する:
- Unity側: `GhostPaceDriver.cs`が1秒毎にタイムラインの区間速度からペースを算出し`UpdateTargetPace`
- タイムラインが無い旧データは平均ペースで代替。タイムライン終端以降も平均ペースで巡航
- Swift側: `RunSettings.ghostDateIso`に対象セッションを設定 → StartSessionに`mode:"ghost"`が付与される(1走行で自動クリア)
- エディタ検証: `ARSessionManager`コンテキストメニュー「Simulate Ghost Run (latest saved session)」(事前に1回走行完了が必要)

## 統計画面(実装済み)

StatsViewは`UnityBridge.lastResult`(SessionEnded)を表示: シンクロ率リング(実測%)・GRADE/ランクバッジ・距離・タイム・平均ペース・推定カロリー。結果未着時はモック値(UI単体開発用)。

## 実測センサー(実装済み)

走行中の距離・ペース・心拍は実測値を優先し、取得できない環境(シミュレータ等)では自動で推定値にフォールバックする:

| 計測値 | 実測ソース | フォールバック |
|---|---|---|
| 距離・ペース | `LocationTracker.swift`(CoreLocation、精度20m以下のサンプルのみ採用・GPS飛び棄却) | 設定ペースからの推定 |
| 心拍 | `HeartRateMonitor.swift`(HealthKit・Apple Watch。HKAnchoredObjectQueryでリアルタイム購読) | ランダム仮値 |
| LatencyReport | Unity `LatencyBenchmarkRunner` のローリング平均M2P(走行中バックグラウンド計測) | 平滑化フレーム時間 |

権限まわり(設定済み): カメラ・位置情報・モーション・Bluetooth・ヘルスケアの使用目的文をビルド設定(INFOPLIST_KEY)に、HealthKit entitlementを `AR_Runner_UI.entitlements` に追加済み。
**初回のみXcodeで**: Signing & Capabilities → + Capability → **HealthKit** を追加(entitlementsファイルは同梱済みなので追加するだけ)。
シミュレータでの位置情報テスト: Features → Location → **City Run**。

## バックグラウンド走行(実装済み)

画面ロック中もCoreLocationの距離計測が継続する:
- `ios/AR_Runner_UI/Info.plist`(部分plist)に `UIBackgroundModes: location`。`GENERATE_INFOPLIST_FILE=YES`と併用時は生成キーとマージされる
- `LocationTracker`が走行中のみ `allowsBackgroundLocationUpdates` を有効化(青いインジケーター表示で明示、停止時に解除)

## 既知の制約 / TODO

- 初回のみ UnityFramework の Embed & Sign と Data フォルダの Target Membership 変更が手動(上記②)
- `ConnectXREAL` は実際のXREAL SDK初期化ではなくReadyチェック状態の更新のみ(SDK導入後にDeviceManagerBridgeへ実装)。DeviceConnectViewのARグラス行タップで送信される
- バックグラウンド中はUnity(AR描画)は停止する — 計測のみ継続し、復帰時にHUD/アバターが追いつく

## 走行画面の配線(実装済み)

`RunningView`([CourseRunningView.swift](ios/AR_Runner_UI/AR_Runner_UI/CourseRunningView.swift))に統合済み:
- 背景: UnityFrameworkリンク時は `UnityContainerView`(ARカメラ+アバター)、未リンク時は従来のモック背景に自動フォールバック
- `onAppear`: `UnityLauncher.launch()` → `ARSessionManager.start(paceKmH:distanceKm:)`(設定値は`RunSettings.shared`経由でRunningSettingsViewから受領)
- HUD: 距離/経過時間=`ARSessionManager`、シンクロ率=`UnityBridge.avatarSyncRate`(Unityから1Hz)、ペース=設定値の分'秒"換算
- GPS喪失バナー: `UnityBridge.gpsStatus == .lost` で表示(Unityの`GPSLost`/`GPSRecovered`イベント連動)
- 終了: `session.end()`(→Unityへ`EndSession`)→ `UnityLauncher.pause()` → 統計画面へ遷移
- ナビゲーション: マップ画面「開始」→ `.running`(RunningView)→ 終了 → `.stats`
