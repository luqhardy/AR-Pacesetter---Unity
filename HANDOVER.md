# AR Vision — 技術資産引き継ぎドキュメント

要件定義書 9.3「GitHubでのドキュメント整備を完了定義(DoD)とする」に基づく、
ソラド株式会社への成果物譲渡用ドキュメント。本リポジトリを「再現・拡張可能な状態」で
引き継ぐために必要な情報の索引と、企画書要件との対応表を提供する。

> 譲渡条件: 現状有姿(As-Is)。詳細は要件定義書 9.3 参照。

---

## 1. リポジトリ構成と主要ドキュメント

| ドキュメント | 内容 |
|---|---|
| [README.md](README.md) | 開発手順・数式・フローチャート・スクリプト一覧・**更新履歴**・E2E検証手順 |
| [AGENTS.md](AGENTS.md) | 技術仕様の一次情報(数式・GPS FSM・レイテンシ予算 ≤20ms) |
| [SWIFT_INTEGRATION.md](SWIFT_INTEGRATION.md) | SwiftUI⇄Unity連携(モノレポ構成・メッセージ契約・ビルド手順) |
| [Docs/BUILD_ON_BORROWED_MAC.md](Docs/BUILD_ON_BORROWED_MAC.md) | 借りたMac+無料Apple IDでの実機ビルド当日手順 |
| [Docs/UNITY_AS_A_LIBRARY.md](Docs/UNITY_AS_A_LIBRARY.md) / [.ja](Docs/UNITY_AS_A_LIBRARY.ja.md) | **UaaL汎用ガイド**(英/日)。SwiftUIアプリへUnityを組み込む手順と落とし穴 |
| [Assets/AvatarStateTransitions.md](Assets/AvatarStateTransitions.md) | Animatorパラメータ定義(Speed/IsHalted/Beckon/Goodbye/Nod等) |

```
AR Pacesetter/          ← Unityプロジェクト(プロトタイプ/検証レイヤー)
├── Assets/_Project/Scripts/   ← 全ゲームプレイロジック(下記対応表)
├── Assets/Editor/              ← iOSエクスポート・E2Eランナー
├── Assets/Plugins/iOS/         ← ネイティブブリッジ(.mm)
└── ios/                        ← SwiftUIアプリ + Xcodeワークスペース(UaaL)
```

## 2. 企画書要件 → 実装 → 検証状態

### アバター・エンジン (企画書 4.1)

| 要件 | 実装 | 検証 |
|---|---|---|
| 3.0m先行追従・Vector Forward純化(GPS+ARKit/IMU、1.5s移動平均) | `RunnerTrackingState.cs` + `AvatarEngine.cs` | Unit(GPS換算)+E2E(直線・コーナー) |
| 速度連動 Walk/Jog/Run ブレンド | `OvertakeBehaviourController.cs` + Animator | エディタ目視 |
| 自動接地(±15cm/0.3sイージング・断崖判定) | `GroundSnap.cs` + `GroundFloorTracker.cs` | **E2E自動**(障害物停止/再開・**床の固定**) |
| 離隔待機(10mで座標固定+手招き、7mで再開) | `AvatarEngine.cs` | **E2E自動** |
| バイタル警告(HR185+で深青+CalmDownSign) | `AvatarVisualsAndActions.cs` | **E2E自動** |
| VFX(起動粒子集積・終了挨拶消滅・接地パルス) | `AvatarVFXController.cs` | エディタ目視 |
| 追い抜き/追い抜かれ動作 | `AvatarEngine.cs` + `OvertakeBehaviourController.cs` | エディタ(O/Pキー) |

### AR HUD (企画書 4.2)

| 要件 | 実装 | 検証 |
|---|---|---|
| 周辺視野レイアウト(HR/Time/Dist/Pace) | `PeripheralHUDManager.cs` | エディタ目視 |
| 横向き時の表示自動抑制 | 同上(ヨー角速度120°/s検知) | エディタ(マウス首振り) |
| 1pxアウトライン高コントラスト | 同上 | エディタ目視 |
| バッテリー10%以下の黄色点滅 | 同上 | エディタ(Yキー) |
| 目標達成時の拡大演出 | 同上(スプリット通知) | エディタ |

### セーフティ&サウンド (企画書 4.3)

| 要件 | 実装 | 検証 |
|---|---|---|
| TTC優先の危険警告(赤点滅+音+振動) | `SafetyAndSystemController.cs` | **未配線 — 実行時に存在しない**(§5参照) |
| 特定音声＆優先度制御(赤信号/交差点のみ・TTC短優先で割込) | `VoiceAlertSpeaker.swift` + `VoiceAlert`イベント | 実機要(検知ソースの地図連携は未 — エディタはContextMenuで送信確認) |
| 足音(路面連動)・心拍連動呼吸音 | `RunAudioEngine.cs`(全クリップ手続き生成) | エディタ試聴 |
| 環境適応音響(45dB→自動音量、75dB上限) | 同上(実機マイク/エディタMキー) | エディタ |
| サイレントルート復帰 | `SilentRouteRecoverer.cs` | **E2E自動**(逸脱→復帰+ログ) |
| GPS喪失FSM(慣性5s→フェード→復帰粒子+頷き) | `GameStateController.cs` | **E2E自動** |

### 走行分析&データ (企画書 4.4)

| 要件 | 実装 | 検証 |
|---|---|---|
| シンクロ率(目標対実測ペース+累積距離偏差、絶対偏差10m以上で0%)・1km/5kmスプリット | `PaceSynchronicityMath.cs` + `AnalyticsManager.cs` | **Unit + E2E自動** |
| リザルト4段階ランク+アバターコメント生成 | `RunSessionController.cs` | **E2E自動** |
| 疲労推定(28℃×1.5/31℃×2.0) | `AnalyticsManager.cs` | **E2E自動**(3閾値の係数) |
| セーフティ・ロギング(急停止/速度超過/逸脱) | `SafetyEventLogger.cs` | **E2E自動**(逸脱ログ) |
| デュアル保存(JSON DB+HealthKit同期キュー) | `SessionDataStore.cs` | **E2E自動**(JSON) |
| **ゴースト機能(過去の自分と競走)** | `GhostPaceDriver.cs` + paceTimeline | **E2E自動** |

### スマホUI&コネクティビティ (企画書 4.5) — SwiftUI側 `ios/`

| 要件 | 実装 | 検証 |
|---|---|---|
| オンボーディング(身長/体重/性別) | Unity: `UserProfile.cs` / Swift: 各View | エディタ・シミュレータ |
| ハイブリッド入力(±ボタン/直接入力) | `RunningSettingsView.swift` ほか | シミュレータ |
| Readyチェック(4色インジケーター・出走ゲート) | `ReadyCheckController.cs` / `Deviceconnectview.swift` | エディタ(F1-F3) |
| ガードレイヤー・スリープ制御 | `RunSessionController.cs` | エディタ |
| 実測センサー(CoreLocation/HealthKit/バックグラウンド) | `LocationTracker.swift` / `HeartRateMonitor.swift` | 実機要 |

## 3. 検証手段(再現手順)

1. **コンパイル検証(Unity起動不要)**: README「更新履歴」参照のdotnetビルド手法
2. **E2E自動検証(現在86項目)**: `Unity.exe -batchmode -projectPath <repo> -executeMethod E2EScenarioRunner.Run -logFile e2e.log`
   — 開始→走行→バイタル警告→追い抜き→障害物停止→ルート逸脱復帰→離隔待機→**コーナー追従(半径36.5m)**→ゴール(お辞儀)→記録→ゴースト再走→GPS喪失/復帰→履歴→HUD抑制→ジェスチャー3種→フェイクシャドウ→60fps設定を自動判定(終了コード0=全PASS)
3. **エディタ手動検証**: README「エディタ検証用ショートカットキー一覧」
   - POV一括デモ: `Tools → AR Pacesetter → POV Demo → Start Automatic 60m Run`
4. **統合ビルド(Mac)**: SWIFT_INTEGRATION.md の手順②(UaaL)

## 4. 企画書§6 成功基準との対応

| 成功基準 | 状態 |
|---|---|
| ① コーナー追従の安定性(400mトラック曲線部) | ロジックはE2Eで自動検証(実地検証は未) |
| ① 低遅延描画 ≤20ms 95% | 計測基盤あり(`LatencyBenchmarkRunner`・実測は実機要) |
| ① 接地精度(LiDAR/空間認識) | エディタRaycast実装済(実機LiDAR検証は未) |
| ② 1.5s移動平均・視覚的安定性 | 実装済(`RunnerTrackingState` GPS+ARKit融合 → `AvatarEngine`) |
| ② 直感的ペーシング | 実装済(シンクロ率・E2E検証) |
| ③ 実証実験の完遂 | **未**(陸上競技場での実走が必要) |
| ③ 技術資産の譲渡(再現・拡張可能な状態) | 本ドキュメント+各種ドキュメントで整備 |

## 5. 未完了事項(引き継ぎ時の注意)

- **XREAL head-pose/IMU入力は未統合**: `ExternalDisplayManager.swift`はUnity画面をUSB-C外部ディスプレイへ移すところまで。`ConnectXREAL`もReady状態更新であり、グラス固有の姿勢/IMU値はUnityへ届かない。現状の`RunnerTrackingState`はiPhoneのARKit/XR Camera+CoreLocationを使うためiPhone POVデモは可能だが、グラスを自由に装着した状態の真のworld-lockにはXREAL SDKまたは対応するpose bridgeが必要
- **Mac統合ビルドの初回手順**: UnityFramework の Embed & Sign 等(SWIFT_INTEGRATION.md ②)。Swiftコードは未コンパイル検証(Windows開発のため)
  - **未実施であることの確認方法と症状(重要)**: `ios/AR_Runner_UI/AR_Runner_UI.xcodeproj/project.pbxproj` に
    `UnityFramework` の文字列が1つも無い場合、リンクは未実施。このとき Swift 側は
    `#if canImport(UnityFramework)` が偽になり **UnityLauncher / UnityBridge がダミー実装へ落ちる** —
    `launch()` はフラグを立てるだけ、`sendToUnity` は送信されず、`simulateUnityResponse` が
    偽のイベントを返す。**アプリはビルドも起動も成功し、統計もそれらしく表示されるため
    「動いているが実際には何も繋がっていない」状態になる**。走行画面に
    「Unity AR View (UnityFramework 未リンク)」のプレースホルダーが出ていたら未リンク。
    併せて `ios/UnityExport/` が空なら手順① (Build → Export iOS) も未実施
- **床(接地)の基準はARプレーン頼み**: 実測フロアはコライダーまたはARプレーンからのみ得られる。
  エディタのシーンにはコライダーが1つも無いため常に暫定床で動作する。実機でも起動直後は
  ARKitが平面を検出するまで暫定床のままなので、**実フロアへのキャリブレーションは未実装**。
  暫定床は`GroundFloorTracker`が**1回だけ確定して固定**するのでアバターが浮き上がることはないが、
  絶対高さが正しい保証は無い(端末をどの高さで起動したかに依存する)。
  `GroundSnap.hideUntilMeasuredFloor` を有効化すると実測フロアを掴むまでアバター描画を抑止できる
  (既定OFF — エディタ/E2Eでは実測フロアが存在せず常時非表示になってしまうため)
- **Mac側の残作業(コード修正では閉じられない)**: UnityFrameworkのEmbed & Sign と
  `Data`フォルダのTarget Membership変更(SWIFT_INTEGRATION.md ②-2/②-3)。
  これが済むまで Swift 側は `#if canImport(UnityFramework)` の偽実装で動き続ける
- **実地フィールドテスト**: `Docs/FIELD_TEST_PLAN.md` の T1〜T9 を実施(GPS不安定域・実機レイテンシ・XREAL表示の定量評価)
- **ルート同期**: MapRouteView(Swift)で表示するコースがUnityの逸脱判定(`SilentRouteRecoverer.routeWaypoints`)へ未接続。実ルート運用時はStartSessionへポリライン(緯度経度→開始点基準のローカル座標変換)を追加する必要がある。現状の逸脱検知はシミュレーション(D キー/E2E)のみ
- **`SafetyAndSystemController` が未配線(TTC危険警告・低バッテリー退避が実行時に不在)**: スクリプトは存在するが、
  シーン(`SampleScene.unity`)にもプレハブにも配置されておらず、`ARVisionSystemsBootstrap` の `Ensure<>` にも無い。
  `AddComponent` する箇所も皆無なので**実行時には一度も生成されない**。結果として TTC赤フラッシュ+警告音+振動、
  最小HUDパネル、低バッテリー退避(アバター退避+Standby遷移)は動作せず、`UnityBridge.swift` が購読する
  `LowBattery` イベントも唯一の送出元がここなので発火し得ない。READMEの「Tキー=TTC警告シミュレーション」も同じ理由で無効。
  ※HUDのバッテリー10%黄色点滅(`PeripheralHUDManager`・Yキー)は別実装で、こちらは正常に動作する。

  **有効化する前に併せて直すべき2点**(そのまま配線すると走行中に誤警報が鳴る):
  1. **障害物の検知ソースが無い** — `Physics.SphereCast` の対象コライダーがシーンに1つも無く(実機ではARFoundationの
     検出平面が対象になる)、地図/LiDARの危険源連携も未接続(音声警告の行と同じ制約)
  2. **非検出時のTTC計算が誤り** — 障害物ヒット無しの場合に距離を`ttcScanRange`(8m)として計算するため、
     前方に何も無くても閉速度が約5.33m/s(19.2km/h)を超えると`TTC≤1.5s`が成立し、
     ループ再生の警告音と`Handheld.Vibrate()`が鳴り続ける。「非検出=安全」として扱う分岐が必要
  第1期スコープ(F-01〜F-11)外の企画書4.3機能のため、上記を解消し実機検証できる段階まで**意図的に休眠のまま**としている

解決済み(参考):
- ~~HealthKit書き込み~~ → `HealthKitWorkoutSaver.swift` がSessionEnded受信時にHKWorkoutを保存
- ~~C++カルマンフィルタの実体~~ → `Assets/Plugins/iOS/KalmanFilterNative.mm`(3軸スカラーKF+線形トレンド)。これが無いとiOSビルドはリンクエラーになるため必須部品。
  **`InitKalmanFilter`/`UpdateKalmanFilter` の実体はこのファイルのみ**とすること — `HeartRatePlugin.mm` にも
  同名実装が残っており、実機リンク時に duplicate symbol 2件でビルドが落ちていた(2026-09-01に削除)。
  ネイティブプラグインの重複はC#コンパイルにもE2Eにも現れず、**実機リンクでしか検出できない**
