# AR Vision (AR Pacesetter) — プロジェクト指針

ARランニング支援システム。3.0m前方を走る半透明アバターとの「距離感」でペーシングする。
Unityプロジェクト(本リポジトリ)+ SwiftUIホスト(`ios/`)のUaaLモノレポ。

## 仕様の優先順位

1. **Aチーム基本設計書 v1**(2025/06/09 梁・PDF、要点は本ファイルに転記)— 第1期の正式仕様
2. [AGENTS.md](AGENTS.md) — 数式・FSM・レイテンシ予算・検証ワークフロー・実装不変条件
3. [HANDOVER.md](HANDOVER.md) — 機能→実装→検証の対応表

## いつ何を読むか

- コードを変更する前 → AGENTS.md §6(検証ワークフローと不変条件)
- アバター位置・接地・カルマンフィルタ・同期率に触れる → AGENTS.md §4
- GPSロスト/FSM(`GameStateController` / `GpsSignalMonitor`)に触れる → AGENTS.md §5
- Swift⇄Unity のメッセージを追加・変更する → [SWIFT_INTEGRATION.md](SWIFT_INTEGRATION.md)
- グラス表示・画角・頭部姿勢 → [Docs/XREAL_ONE_INTEGRATION.md](Docs/XREAL_ONE_INTEGRATION.md)
- 屋外路面の分類(ARCore) → [Docs/ARCORE_SCENE_SEMANTICS.md](Docs/ARCORE_SCENE_SEMANTICS.md)
- 差し替えアバター(VRM) → [Docs/VRM_AVATARS.md](Docs/VRM_AVATARS.md)
- 実地テストの計画 → [Docs/FIELD_TEST_PLAN.md](Docs/FIELD_TEST_PLAN.md)
- 過去の経緯(なぜそうなっているか) → [CHANGELOG.md](CHANGELOG.md) を検索する(全文は長いので読み通さない)

## 第1期検証フェーズのスコープ(基本設計書 §1.2)

**iPhone単体 + ARグラス(USB-C有線)の2台構成**。Apple Watch・外部サーバー・バイタルセンサーは
意図的にスコープ外 — 通信ボトルネックを排し M2P 20ms を守るため。
ゴールは「陸上トラックにおけるARペーシング技術の完全確立」。技術限界データのCSV蓄積がソラド社への譲渡基盤になる。

Watch/HealthKit/ゴースト等の企画書由来機能も実装済み。削除はせず、触るときは影響を最小に留める。
検証・デモ・実装の優先度は F-01〜F-11 が先。

## 機能一覧(F-01〜F-11)

| ID | 機能 | 要点 |
|---|---|---|
| F-01 | 走行条件設定 | 目標ペース・トラック距離(400m等)の入力・保持 |
| F-02 | レディチェック | グラス接続+GPS測位精度の確認、全緑で走行開始活性化 |
| F-03 | 3.0m前方維持 | 進行方向3.0m前方へ配置・追従 |
| F-04 | 曲線部接線維持 | コーナーで向きを接線方向へ100%一致(前後フレーム座標から逆算) |
| F-05 | グラウンドスナップ | LiDAR/空間認識で接地。異常値(断崖等)は前フレーム高さを維持 |
| F-06 | アニメーション制御 | Idle(0)/Walk(0.1〜5.0km/h・再生速度同期)/Run(5.0km/h+)のブレンド |
| F-07 | 周辺視野レイアウト | 左上=時間・距離 / 右上=現在ペース(遅れ=赤・維持=緑) / 中央=完全透過(アバターのみ) / 下部=警告時のみ |
| F-08 | 視覚的安定化 | 1.5秒移動平均(IMU 150フレーム)で首振りブレを排除 |
| F-09 | GPSロスト慣性移動 | 判定: 更新1.5秒途絶 or 水平精度10m超。直前1秒の平均速度・方向で最大5秒慣性。復帰時は1秒かけて同期 |
| F-10 | フェードアウト | ロスト5秒継続で1秒(60f)かけてα100→0%。HUD下部に赤字警告「GPS信号を探索中：安全のため減速してください」 |
| F-11 | リアルタイムログ保存 | 位置・IMU・描画遅延をローカルCSVへ100Hz出力(下記) |

## 主要仕様値

- **アバター色(§7.1)**: ジャスト(目標リード±1.5m)=緑 / 遅延(3.0m以上離れ)=橙→赤グラデ / 超過(追い抜き)=青。`AvatarPaceColor`
- **オーラ(§7.2)**: 5.0m以上遅れで足元からランナー側へ光のライン。`AvatarAuraEffect`
- **非機能(§10)**: M2P 20ms以内(最大許容30ms) / 位置誤差1.0m以内・接地誤差上下5cm以内 / 連続稼働60分(バッテリー30%以上)
- **描画**: 60fps。映像はDisplayPort Alt Mode(USB-C有線・無圧縮1080p)、グラスはバスパワー給電
- **グラス切断時(§8.3)**: CSVログはBGで継続しスタンバイへ。再接続だけではアバターを出さず、準備画面からの再スタート(`ResumeSession`)で復帰
- **設定データ(§5.1)**: `targetPaceMinutesPerKm`(**秒**で保持、例270=4分30秒)・`trackLength`(m)・`avatarDistance`(3.0)・`isGlassConnected`・`gpsAccuracyStatus`(0=圏外/1=低/2=高)
- **走行ログCSV(§5.2)**: `<persistentDataPath>/RunLogs/Log_YYYYMMDD_HHMMSS.csv`、100Hz、列=`timestamp, gps_latitude, gps_longitude, imu_accel_x/y/z, avatar_pos_x, avatar_pos_z, latency_m2p`。`RunTelemetryLogger`
- **開発環境(§11)**: Unity 6000.3.17f1 / C#+Swift / iOS 26+ / iPhone 12 Pro以上
- **テスト3フェーズ(§11.2)**: ①室内ベンチ(100Hz+CSV確認) → ②歩行・低速(3m追従・ワープなし) → ③400mトラック実証(旋回・HUD・フェードアウト+CSVで20ms評価)

## 守るべき実装上の約束

- アバターの色は発光で出すので、アバターのmaterialは **Emission 対応**にする
- GPSロスト判定を止めたいときは実行時コマンド `SetGpsLostHandling {enabled}` を使う。既定値(`true`)は仕様どおりに保つ。
  屋内で消えないのは `GpsSignalMonitor.RequireInitialFixBeforeLost` のおかげ(良好な初回測位前はロスト判定しない)
- 実測GPSサンプルが来ない間 `GpsSignalMonitor` は介入しない — エディタの G/R/A キー検証はこれに依存する
- IMU加速度の供給元は `RunTelemetryLogger.ImuSource` で判別する(実機=`Input.gyro.userAcceleration`、エディタ=カメラ差分近似)。`SetImuAcceleration` はC#専用で、Swiftからは呼べない
- アニメーションの閾値は km/h 基準を m/s に換算した値(Walk 0.0278 / Run 1.3889 / Sprint 4.1667)。AnimatorController はジェネレータで再生成する
- グラスの画面モードは **Follow(固定)**。Anchor だと頭部補正が二重にかかる。iOSからグラスの頭部姿勢は取得できない
- 屋外路面分類(ARCore)は `#if ARCORE_EXTENSIONS` の中だけで触る。**3D面分類を画像分類で上書きしない**
- VRMアバターの受け入れ基準(三角形70,000・マテリアル8等)は 60fps と M2P 20ms のための値。`.vrca`(VRChat)は使えない

## 未決事項(チーム判断待ち — 独断で解決しない)

1. **3.0m前方と画角の両立(F-03 × グラス)**: 身長1.75mのアバターは3.0mで垂直31.1°を占め、グラスの垂直25.7°に全身が入らない
   (全身には3.7m必要)。§7.2のオーラ(足元)も視野外。距離か見え方か、どちらを譲るかはチームが決める
2. **`SafetyAndSystemController`(TTC警告・低バッテリー退避)は休眠中**: 実行時に生成されない。有効化には
   (a)障害物検知ソースの接続 (b)非検出時にTTCを `ttcScanRange` で計算する誤りの修正が要る —
   そのまま配線すると19.2km/h超で誤警報が出る。詳細は HANDOVER.md §5

## 作業規約

- 検証は AGENTS.md §6 の手順どおり、コンパイル → ユニットテスト → (Swift構文) → E2E(終了コード0)まで通して完了とする
- 純ロジックは `PaceMath` のような依存ゼロの静的クラスへ書き、MonoBehaviour は委譲する
- コミット前に [CHANGELOG.md](CHANGELOG.md) の先頭へ5行以内のエントリを足す。コミットメッセージは日本語で、検証結果を書く
