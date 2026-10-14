# TestFlight 配布の計画(未導入 — チーム判断待ち)

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

## CI でやること(Secrets が揃ったら実装する)

`ios-build.yml` の「Build host app」の後に、master への push でだけ動くステップを足す:

1. ビルド番号をワークフローの実行番号にする(`CURRENT_PROJECT_VERSION=${{ github.run_number }}`)
2. `xcodebuild archive -workspace ios/ARRunner.xcworkspace -scheme AR_Runner_UI -destination 'generic/platform=iOS'`
   `-allowProvisioningUpdates -authenticationKeyPath … -authenticationKeyID … -authenticationKeyIssuerID …`
   (API キーでの自動署名。証明書・プロファイルを手で管理しない)
3. `xcodebuild -exportArchive` に `ExportOptions.plist`(`method = app-store-connect`, `destination = upload`)を渡して
   TestFlight へアップロード
4. 内部テスター(最大100人・審査なし)に自動配信

Unity エクスポートは今と同じく Releases の最新(`tools/publish-unity-export.sh` で公開)を使う。

## 決めること

- 個人アカウントか組織アカウントか(組織は D-U-N-S 番号が要る)
- Bundle ID(上記の理由で、最初に決めて固定する)
- 誰が API キーと Secrets を管理するか
