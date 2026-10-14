# 実地テストの記録(Docs/field-tests)

[FIELD_TEST_PLAN.md](../FIELD_TEST_PLAN.md) の T1〜T9 を実施したら、**1回のセッションを1フォルダ**にまとめてここへ置く。
第1期の成果物は「技術限界データ」そのもの(AGENTS.md §2)なので、走行ログCSVと解析結果を必ず残す。

```
Docs/field-tests/
  20261020/                   ← 実施日(YYYYMMDD)。同日に2回なら 20261020-2
    RESULTS.md                ← TEMPLATE.md をコピーして記入
    Log_20261020_101500.csv   ← 走行ログ(F-11・100Hz)。1走行1ファイル
    analysis.txt              ← tools/analyze-run-log の出力
```

## 当日の流れ

1. 走る前に [FIELD_TEST_PLAN.md §0 / §0-A](../FIELD_TEST_PLAN.md) を済ませる
2. 走行後、iPhone の **ホーム → メニュー → 開発者モード**
   - **走行ログ CSV** → 各ファイルをタップ → 共有(AirDrop / ファイル)で取り出す
   - **アバター非表示の履歴** → 消えた時刻・秒数・理由をスクリーンショット(RESULTS.md に転記)
   - **状態**(`m2p.lastMs` / `csv.imuSource` / `glass.fit`)をスクリーンショット
3. CSV をこのフォルダへ置き、解析する:

   ```bash
   dotnet run --project tools/analyze-run-log -- Docs/field-tests/20261020/ > Docs/field-tests/20261020/analysis.txt
   ```

   出力の末尾は FIELD_TEST_PLAN §3 の記入行(T2 p95・T4 距離など)になっているので、そのまま RESULTS.md へ写す
4. RESULTS.md を記入してコミット(CHANGELOG にも1行 — AGENTS.md §6)

## 注意

- CSV は 100Hz なので 20分で約10MB。数本なら git にそのまま置いてよいが、増えてきたら Git LFS を検討する
- **T2(M2P ≤20ms)は実機のCSVでしか測れない**。エディタ・E2E の `latency_m2p` は常に `-1`(未計測)
- 撮影した動画はリポジトリに入れず、RESULTS.md にリンク(共有ドライブ等)を書く
