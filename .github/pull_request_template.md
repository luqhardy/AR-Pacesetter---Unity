## 概要

<!-- 何を変えたか・なぜか。タスク表の行や不具合の再現手順があれば書く -->

## 検証

<!-- tools/verify.sh の結果(AGENTS.md §6)。例: dotnet test 421/421、Swift 45ファイル構文OK、E2E 282/282 -->

- [ ] `tools/verify.sh` が `ALL CHECKS PASSED`(Unity エディタは閉じて実行する。E2E を省いた `--fast` は途中確認用)
- [ ] `CHANGELOG.md` の先頭に5行以内のエントリ(変更・理由・検証結果)
- [ ] コミットメッセージは日本語で、検証結果を含む

### 変えた場所に応じて

- [ ] Swift の画面の文言: `Core/Localization/AppStrings.swift` のキー + `Resources/Localizable.xcstrings` に日本語・英語(スタッフ画面・開発者モードは日本語のみ)
- [ ] UI に依存しない判断: `AppLogic/` に置き、`ios/AppLogicTests` にテスト(Windows では実行できない — CI の swift test で確認)
- [ ] Swift ⇄ Unity のメッセージ: `SWIFT_INTEGRATION.md` の表を更新
- [ ] Unity 側(Assets / ProjectSettings / Packages)を変えた: マージ後に `tools/publish-unity-export.sh` で再エクスポート
- [ ] 署名の一時変更(`tools/prepare-free-signing.sh` の結果・個人の Team ID / Bundle ID)を含めていない

## 実機で要確認

<!-- シミュレータ・エディタでは確かめられないこと。無ければ「なし」 -->

- [ ]
