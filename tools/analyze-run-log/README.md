# analyze-run-log — 走行ログCSVの解析

iPhoneから回収した走行ログ(`Log_YYYYMMDD_HHMMSS.csv`、F-11)を読み、実地テスト
([Docs/FIELD_TEST_PLAN.md](../../Docs/FIELD_TEST_PLAN.md))の数値を出す。Unity不要・Windows/macOS共通(.NET 8)。

```bash
dotnet run --project tools/analyze-run-log -- Docs/field-tests/20261001/            # フォルダ内の Log_*.csv すべて
dotnet run --project tools/analyze-run-log -- Log_20261001_101500.csv               # 1ファイル
dotnet run --project tools/analyze-run-log -- Docs/field-tests/20261001/ --json     # 機械可読
```

## 出るもの

| 項目 | 内容 | 使い道 |
|---|---|---|
| Timeline | 行数・時間・実レート(100Hz目標、95Hz以上でOK)・50ms超の欠落・時刻の逆行 | F-11 の記録品質 |
| M2P | 実測行の割合、p50 / p95 / p99 / 最大、20ms・30ms超過率 → **T2 PASS/FAIL**(p95 ≤ 20ms) | T2 / §10 |
| GPS | 測位のある行の割合、位置更新の回数、最長の途絶、1.5秒以上の途絶の数、GPS距離 | T4(既知距離と比較)・T7 |
| IMU | 非ゼロ行の割合・平均/最大加速度 | 実機のCoreMotionが入っているか |
| Avatar | アバターの移動距離・平均速度とペース | 目標ペースとの比較 |

複数ファイルを渡すと、最後に全体のM2P(T2は走行全体の95%で判定)と合計時間・GPS距離を出し、
記録テンプレート(FIELD_TEST_PLAN §3)の `T2 p95レイテンシ` 行をそのまま貼れる形で出す。

## 読み方の注意

- **エディタ(E2E)のログはM2PとGPSが「未計測」になる** — `latency_m2p` は実機でのみ実測され、GPSは0,0。
- パーセンタイルは補間しない最近順位法(「95%の行がこの値以下」)。`-1`(未計測)と1秒超の異常値は除外する。
- `latency_m2p` はフレーム単位の値で、100Hzの行には同じ値が2行ほど続く。T2 は計画どおり行単位で集計している。
- GPSの「位置更新」は緯度経度が変わった回数。立ち止まっていると更新が無くても途絶と数えることがある。
- 終了コード: 0 = 読めた / 1 = 走行ログでないファイルがあった / 2 = 使い方の誤り。

解析ロジックは [RunLogAnalysis.cs](RunLogAnalysis.cs)(`Tests/UnitTests/RunLogAnalysisTests.cs` でテスト)。
