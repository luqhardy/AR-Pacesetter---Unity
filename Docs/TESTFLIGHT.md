# TestFlight 配布(ワークフローは用意済み — 有料アカウントの判断待ち)

> **今すぐ使える方**: Mac を持っている人は `tools/build-to-iphone.sh` で、挿した iPhone へ1コマンドで入る
> (無料の Apple ID・7日ごとに再実行)。[BUILD_ON_BORROWED_MAC.md](BUILD_ON_BORROWED_MAC.md) の冒頭。

**目的**: 実機テストのたびに Mac を借りる手間をなくす。今は Windows → Unity エクスポート → Mac へ運ぶ →
無料署名でビルド(7日で起動不能になる)の流れで、2026-10 にはエクスポートのコピー漏れ・衝突コピー・
実行権限・署名で何度も止まった。CI(`ios-build.yml`)はすでに macOS ランナーでアプリをコンパイル・
リンクできているので、**署名して TestFlight へ上げる段だけ足せば**、チームの iPhone は TestFlight アプリから
入れられるようになる。

## 必要なもの(チームが用意する)

| もの | 備考 |
|---|---|
| Apple Developer Program | 年額 $99(個人または組織)。無料の Apple ID では TestFlight に出せない |
| Bundle ID | 例 `com.<組織>.arvision`。**一度決めたら変えない** — 変えると別アプリになり、端末に溜まった `RunLogs/*.csv` へ新しいアプリから届かない |
| App Store Connect のアプリ登録 | 上の Bundle ID で作成。HealthKit ケイパビリティは有料アカウントならそのまま使える(`prepare-free-signing.sh` は不要になる) |
| App Store Connect API キー | Users and Access → Integrations → App Store Connect API。役割 App Manager。`.p8`・Key ID・Issuer ID |
| GitHub Secrets | `ASC_KEY_ID` / `ASC_ISSUER_ID` / `ASC_KEY_P8`(.p8 の中身)/ `APPLE_TEAM_ID` |

## CI(`.github/workflows/ios-testflight.yml` — 実装済み・未実行)

**リポジトリ変数 `TESTFLIGHT_BUNDLE_ID` を設定した時点で動き出す**。それまでジョブは skipped で何もしない。

- いつ: master へ `ios/**` の変更が入ったとき・`tools/publish-unity-export.sh` が新しいエクスポートを公開したとき・手動(Actions → TestFlight → Run workflow)
- 何を: 最新の `unity-export-*` を取り、ランナーの中でだけチームIDと Bundle ID を配布用へ替え、
  `xcodebuild archive`(Release・ビルド番号 = 実行番号)→ `-exportArchive`(`app-store-connect` / `upload`)。
  署名は API キーでの自動署名(クラウド管理の証明書)で、証明書・プロファイルを手で管理しない
- チームの受け取り: App Store Connect → TestFlight → 内部テスト のグループに全員を入れ、自動配信を ON。
  以後は iPhone の TestFlight アプリに新しいビルドが届く(処理に10〜30分)

### 有効にする手順(アカウント保有者が1回)

1. App Store Connect でアプリを作る(Bundle ID を決めて固定)
2. Users and Access → Integrations → App Store Connect API でキーを作る。**役割は Admin**
   (クラウド管理の配布用証明書を作れるのは Admin 以上。App Manager では署名で止まる)
3. GitHub → Settings → Secrets and variables → Actions
   - Secrets: `APPLE_TEAM_ID` / `ASC_KEY_ID` / `ASC_ISSUER_ID` / `ASC_KEY_P8`(.p8 の中身をそのまま)
   - Variables: `TESTFLIGHT_BUNDLE_ID`
4. Actions → TestFlight → Run workflow で1回流し、TestFlight にビルドが出るのを確かめる

**一度も実行していない**ので、最初の1回で署名まわりの調整が要る可能性がある。失敗したら Artifacts の
`testflight-logs`(archive.log / export.log)を見る。

### 毎回の手作業が残るところ

- **輸出コンプライアンス**: Info.plist に `ITSAppUsesNonExemptEncryption` が無いので、ビルドごとに
  App Store Connect で暗号化の質問に答えるまで配信されない。標準の HTTPS だけなら `NO` を足せば
  毎回の質問が消える — 申告にあたるのでチームで決めてから `INFOPLIST_KEY_ITSAppUsesNonExemptEncryption = NO` を足す

## 決めること

- 個人アカウントか組織アカウントか(組織は D-U-N-S 番号が要る)
- Bundle ID(上記の理由で、最初に決めて固定する)
- 誰が API キーと Secrets を管理するか
- 輸出コンプライアンス(上)の申告
