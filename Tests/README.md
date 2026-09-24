# ユニットテスト(純ロジック)

Unityを起動せず `dotnet test` で回る、Unity非依存の純ロジック検証。
E2E(Unityバッチで数分)を補完し、境界値を秒速で網羅する。PRではCI(`.github/workflows/unit-tests.yml`)が自動で実行する。
件数はここに書かない(すぐ古くなる) — 実行結果の「合計」を見る。

## 実行

```bash
dotnet test Tests/UnitTests      # ユニットテストだけ
tools/verify.sh --fast           # コンパイル検証 + ユニットテスト + Swift構文
tools/verify.sh                  # 上記 + E2E(変更の完了判定はこれ。AGENTS.md §6)
```

初回は NuGet 復元が走る。以降は数秒で終わる。

## テストの足し方

1. 純ロジックを `Assets/_Project/Scripts/` の**Unity非依存の静的クラス**に書く(`UnityEngine` を `using` しない)。
   MonoBehaviour は計算をそのクラスへ委譲するだけにする。
2. そのファイルを [ARVision.UnitTests.csproj](UnitTests/ARVision.UnitTests.csproj) の `<Compile Include>` に1行足す
   (テストプロジェクトはUnityのDLLを参照せず、ソースを直接コンパイルする)。
3. `Tests/UnitTests/<クラス名>Tests.cs` に NUnit 3 のテストを書く。テスト名は日本語で「何が保証されるか」を書く慣習
   (例: `どの位相から計測を始めても結果は同じ`)。

Unityの型(`Vector3` 等)が要る処理はここではテストできない — その部分は E2E(`E2EScenarioBehaviour.cs`)で縛る。

## 対象

| テスト | 対象 | 仕様 |
|---|---|---|
| `PaceParsingTests` | `PaceMath.TryParsePace` | ペース入力の解析・範囲(3.5〜7.0分/km)。F-01 |
| `GhostPaceTests` | `PaceMath.SampleGhostPace` | ゴーストの区間速度→ペース(第1期スコープ外) |
| `PaceSynchronicityMathTests` | `PaceSynchronicityMath` | 同期率・累積距離偏差(AGENTS.md §4.3) |
| `PaceHudDisplayTests` | `PaceHudDisplay` | HUD右上「現在ペース」の表示と色。F-07 |
| `AvatarPaceColorTests` | `AvatarPaceColor` | ペースシンクロ・カラー(緑/橙→赤/青)。§7.1 |
| `AuraFeedbackTests` | `AuraFeedback` | 5m以上遅れたときのオーラ。§7.2 |
| `AvatarScaleTests` | `AvatarScale` | 身長スケール |
| `AvatarVisibilityReasonTests` | `AvatarVisibilityReason` | 「アバターが見えない理由」の判定 |
| `RunnerTrackingMathTests` | `RunnerTrackingMath` | GPS座標の距離・方位・妥当性(F-03) |
| `HeadingGateMathTests` | `HeadingGateMath` | 測位ノイズで進行方向を振り回さないゲート。F-04/F-08 |
| `StartHeadingPolicyTests` | `StartHeadingPolicy` | 開始時にアバターを走者の正面へ出す |
| `TrackingLagMathTests` | `TrackingLagMath` | 追従の定常遅れ補正。F-03 / §10 位置誤差 |
| `SpatialKalmanFilterTests` | `SpatialKalmanFilter` | カルマンフィルタ(時間基準)。AGENTS.md §4.4 |
| `FrameSmoothingTests` | `FrameSmoothing` | 長いフレームで追従が瞬間移動しない平滑化 |
| `GroundFloorTrackerTests` | `GroundFloorTracker` | 床の確定・天井の棄却。F-05 |
| `CliffMathTests` | `CliffMath` | 断崖判定(天井を地面にしない)。AGENTS.md §4.2 |
| `GroundContactMathTests` | `GroundContactMath` | 接地誤差の判定と足のめり込み補正。§10 ±5cm |
| `SurfaceSemanticMathTests` | `SurfaceSemanticMath` | ARKitの面分類から地面・障害物を決める |
| `OutdoorSemanticMathTests` | `SurfaceSemanticMath`(屋外の画像分類との統合) | 3D面分類を画像分類で上書きしない・安全側に倒れる(ARCore、既定は休眠) |
| `GoalLineMathTests` | `GoalLineMath` | ARゴールラインの表示・固定のタイミング |
| `MotionToPhotonMathTests` | `MotionToPhotonMath` | M2P遅延の算出。§10 20ms |
| `NonFunctionalStatsTests` | `NonFunctionalStats` | 位置誤差の集計。§10 1.0m |
| `GlassOpticsMathTests` | `GlassOpticsMath` | グラスの画角換算・全身が視野に入る距離 |
| `GlassPoseAndProfileTests` | `GlassDisplayProfile` / `HeadPoseMath` | 表示プロファイルと頭部姿勢の供給元 |
| `VrmAvatarPolicyTests` | `VrmAvatarPolicy` | 差し替えアバター(VRM)の受け入れ基準 |
