# ユニットテスト(純ロジック)

Unityを起動せず `dotnet test` で回る、Unity非依存の純ロジック検証。
E2E(37+項目・Unityバッチで数分)を補完し、境界値を秒速で網羅する。

## 実行

```bash
cd Tests/UnitTests
dotnet test
```

初回は NuGet 復元が走る。以降は 1 秒未満で 23 ケースが完了する。

## 対象

| テスト | 対象 | 由来 |
|---|---|---|
| `PaceParsingTests` | `PaceMath.TryParsePace` | ペース入力の解析・範囲検証(3.5〜7.0分/km) |
| `GhostPaceTests` | `PaceMath.SampleGhostPace` | ゴーストの区間速度→ペース算出・フォールバック・クランプ |

## 設計方針

テスト対象の純ロジックは `Assets/_Project/Scripts/PaceMath.cs`(Unity非依存の
静的クラス)へ抽出済み。MonoBehaviour(`PaceCalibrationController` /
`GhostPaceDriver`)は薄いラッパーとして `PaceMath` へ委譲する。この分離により
テストプロジェクトは Unity DLL を一切参照せず、CI(Linux含む)でもそのまま動く。

新しい純ロジックを足すときは、まず `PaceMath` のような依存ゼロの静的クラスに
書き、MonoBehaviour からは委譲する — そうすればここでテストできる
(AGENTS.md の検証ワークフロー参照)。
