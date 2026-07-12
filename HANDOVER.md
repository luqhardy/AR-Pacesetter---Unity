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
| 3.0m先行追従・Vector Forward純化(1.5s移動平均) | `AvatarEngine.cs` | E2E(直線・コーナー) |
| 速度連動 Walk/Jog/Run ブレンド | `OvertakeBehaviourController.cs` + Animator | エディタ目視 |
| 自動接地(±15cm/0.3sイージング・断崖判定) | `GroundSnap.cs` | **E2E自動**(障害物停止/再開) |
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
| TTC優先の危険警告(赤点滅+音+振動) | `SafetyAndSystemController.cs` | エディタ(Tキー) |
| 足音(路面連動)・心拍連動呼吸音 | `RunAudioEngine.cs`(全クリップ手続き生成) | エディタ試聴 |
| 環境適応音響(45dB→自動音量、75dB上限) | 同上(実機マイク/エディタMキー) | エディタ |
| サイレントルート復帰 | `SilentRouteRecoverer.cs` | **E2E自動**(逸脱→復帰+ログ) |
| GPS喪失FSM(慣性5s→フェード→復帰粒子+頷き) | `GameStateController.cs` | **E2E自動** |

### 走行分析&データ (企画書 4.4)

| 要件 | 実装 | 検証 |
|---|---|---|
| シンクロ率(S=100×(1−d/10))・1km/5kmスプリット | `AnalyticsManager.cs` | **E2E自動** |
| リザルト4段階ランク+アバターコメント生成 | `RunSessionController.cs` | **E2E自動** |
| 疲労推定(28℃×1.5/31℃×2.0) | `AnalyticsManager.cs` | 単体ロジック |
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
2. **E2E自動検証**: `Unity.exe -batchmode -projectPath <repo> -executeMethod E2EScenarioRunner.Run -logFile e2e.log`
   — 開始→走行→ゴール→記録→ゴースト再走→**コーナー追従(半径36.5m)**→GPS喪失/復帰→履歴を自動判定(終了コード0=全PASS)
3. **エディタ手動検証**: README「エディタ検証用ショートカットキー一覧」
4. **統合ビルド(Mac)**: SWIFT_INTEGRATION.md の手順②(UaaL)

## 4. 企画書§6 成功基準との対応

| 成功基準 | 状態 |
|---|---|
| ① コーナー追従の安定性(400mトラック曲線部) | ロジックはE2Eで自動検証(実地検証は未) |
| ① 低遅延描画 ≤20ms 95% | 計測基盤あり(`LatencyBenchmarkRunner`・実測は実機要) |
| ① 接地精度(LiDAR/空間認識) | エディタRaycast実装済(実機LiDAR検証は未) |
| ② 1.5s移動平均・視覚的安定性 | 実装済(`AvatarEngine` Vector Forward純化) |
| ② 直感的ペーシング | 実装済(シンクロ率・E2E検証) |
| ③ 実証実験の完遂 | **未**(陸上競技場での実走が必要) |
| ③ 技術資産の譲渡(再現・拡張可能な状態) | 本ドキュメント+各種ドキュメントで整備 |

## 5. 未完了事項(引き継ぎ時の注意)

- **XREAL SDK統合**: グラス表示は外部ディスプレイ経由で実装済み(`ExternalDisplayManager.swift`・SDK不要)。SDK固有機能(6DoF・空間メッシュ等)が必要になった場合のみNRSDK導入を検討
- **Mac統合ビルドの初回手順**: UnityFramework の Embed & Sign 等(SWIFT_INTEGRATION.md ②)。Swiftコードは未コンパイル検証(Windows開発のため)
- **実地フィールドテスト**: `Docs/FIELD_TEST_PLAN.md` の T1〜T9 を実施(GPS不安定域・実機レイテンシ・XREAL表示の定量評価)

解決済み(参考):
- ~~HealthKit書き込み~~ → `HealthKitWorkoutSaver.swift` がSessionEnded受信時にHKWorkoutを保存
- ~~C++カルマンフィルタの実体~~ → `Assets/Plugins/iOS/KalmanFilterNative.mm`(3軸スカラーKF+線形トレンド)。これが無いとiOSビルドはリンクエラーになるため必須部品
