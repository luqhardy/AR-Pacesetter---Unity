# 借りたMacで実機ビルドする手順(無料Apple ID)

Windows開発機 + 一時的に借りるMac、という前提の実機ビルド手順。
Mac作業を最小化するため、**Windowsでできることは全て事前に済ませてから行く**。

対象は「SwiftUIアプリ + Unity埋め込み」の最終アプリ。Unity単体をビルドしても
SwiftUI画面は入らない(詳細: [SWIFT_INTEGRATION.md](../SWIFT_INTEGRATION.md))。

---

## 0. 行く前に確認する(最重要)

- [ ] **友人のMacのXcodeバージョン**を聞く → `xcodebuild -version`
      本アプリの Deployment Target は **iOS 26.0**(基本設計書 §11)なので **Xcode 26 が必須**。
      無い場合は約15GBのダウンロードになるため、**当日ではなく事前に入れてもらう**
- [ ] Xcodeが対応するmacOSかどうか(Xcode 26は新しいmacOSを要求する)
- [ ] 自分のApple IDでMacのXcodeにサインインできること
      (Xcode → Settings → Accounts。無料のApple IDでよい)
- [ ] **iPhone実機**とUSBケーブル。**ARKitはシミュレータで動かないので実機必須**
- [ ] ARグラス(XREAL)とUSB-Cケーブル ※グラス表示まで確認する場合

## 1. Windows側で事前に済ませる

- [ ] 最新の状態をpush済みにする(Macでは浅いcloneを使う)
- [ ] **Unityエクスポートを実行する**

      ```
      "C:\Program Files\Unity\Hub\Editor\6000.3.17f1\Editor\Unity.exe" -batchmode -quit ^
        -projectPath "C:\Users\luqma\AR Pacesetter" ^
        -executeMethod IOSBuildExporter.ExportIOS -logFile export.log
      ```

      終了コード0で成功。`ios/UnityExport/` が生成される
- [ ] エクスポート後に `git diff ProjectSettings/ProjectSettings.asset` を確認。
      `preloadedAssets` が空になっていたら**元に戻す**(空のままだとXR(ARKit)ローダーが初期化されない)
- [ ] **`ios/UnityExport/` をUSBメモリにコピーする(約1.5GB)**
      このフォルダは生成物なのでgit管理外 = cloneしても付いてこない

## 2. Mac側: 準備(15分)

- [ ] リポジトリを浅くcloneする(履歴1.3GBを避ける)

      ```bash
      git clone --depth 1 https://github.com/luqhardy/AR-Pacesetter---Unity.git
      cd AR-Pacesetter---Unity
      ```

- [ ] USBメモリから `UnityExport` を `ios/` の下に置く

      ```bash
      cp -R /Volumes/<USB名>/UnityExport ios/
      ls ios/UnityExport/Unity-iPhone.xcodeproj    # 存在すればOK
      ```

- [ ] **無料Apple ID用の下準備スクリプトを実行する**(Bundle IDは自分専用のものにする)

      ```bash
      chmod +x tools/prepare-free-signing.sh
      ./tools/prepare-free-signing.sh com.yourname.pacesetter
      ```

      HealthKitケイパビリティの除去・チームIDの空化・Bundle IDの差し替えを行う。
      **なぜHealthKitを外すのか**: 無料のPersonal TeamではHealthKitをプロビジョニングできず
      署名が通らない。HealthKit(心拍・ワークアウト保存)はApple Watch連携で
      **第1期スコープ(F-01〜F-11)の対象外**なので、外しても検証内容に影響しない。
      元に戻す場合は `git checkout -- ios/AR_Runner_UI`

## 3. Mac側: Xcode設定(初回のみ・5分)

- [ ] `ios/ARRunner.xcworkspace` を開く(`.xcodeproj` ではなく **workspace**)
- [ ] 左のプロジェクト一覧に `AR_Runner_UI` と `Unity-iPhone` の**両方**が見えることを確認
      (`Unity-iPhone` が赤字/不在なら手順2のコピーが失敗している)
- [ ] `AR_Runner_UI` ターゲット → **Signing & Capabilities**
      - [ ] Team: 自分のApple ID(Personal Team)を選択
      - [ ] "Automatically manage signing" がON
      - [ ] エラーが出る場合はBundle IDを更にユニークなものへ変更
- [ ] `AR_Runner_UI` ターゲット → **General** → *Frameworks, Libraries, and Embedded Content*
      - [ ] `Unity-iPhone` プロジェクト内の **UnityFramework.framework** を `+` で追加
      - [ ] 追加後、右の選択を **Embed & Sign** にする ← ここを忘れると起動時にクラッシュする
- [ ] `Unity-iPhone` プロジェクト → `Data` フォルダを選択 → 右ペインの **Target Membership** を
      **UnityFramework** に変更(既定は Unity-iPhone のためリンクが通らない)

## 4. Mac側: ビルドと実行

- [ ] iPhoneをUSB接続 → iPhone側で「このコンピュータを信頼」
- [ ] Xcode上部のデバイス選択で**実機**を選ぶ(Simulatorは不可)
- [ ] スキームが **AR_Runner_UI** であることを確認(Unity-iPhoneではない)
- [ ] ⌘R で実行
- [ ] 初回起動時、iPhone側で **設定 → 一般 → VPNとデバイス管理** から
      開発者App(自分のApple ID)を**信頼**する ※無料アカウント特有の手順
- [ ] 権限ダイアログを全て許可(カメラ・位置情報・モーション)

## 5. 動作確認(ここが本番)

- [ ] 走行画面の背景が**カメラ映像**になっている
      → 「Unity AR View (UnityFramework 未リンク)」と出ていたら手順3のEmbed & Signが未完了
- [ ] 3m前方にアバターが表示され、**接地している**(浮いていない・頭を動かしても上下しない)
- [ ] 短く1本走って**最後まで完走する**
      → 終了時にクラッシュしないこと(修正済みのC1がここで効く)
- [ ] 統計画面に結果が出る
- [ ] **CSVログを回収する**(PoCの成果物)
      Xcode → Window → Devices and Simulators → 対象デバイス → Installed Apps →
      AR_Runner_UI → 歯車 → **Download Container…** →
      `.xcappdata` を右クリック → パッケージの内容を表示 →
      `AppData/Documents/RunLogs/Log_*.csv`
- [ ] CSVを開き、`imu_accel_x/y/z` が **0以外の実測値**で埋まっていることを確認
      (実機ではCoreMotionから100Hzで供給される)

## 6. よくある失敗

| 症状 | 原因と対処 |
|---|---|
| 起動直後にクラッシュ(dyld: Library not loaded) | UnityFrameworkが **Embed & Sign** になっていない(手順3) |
| リンクエラー(undefined symbols) | `Data` フォルダの Target Membership が未変更(手順3) |
| 走行画面が暗く「未リンク」と出る | 同上。Unityが実際には繋がっていない状態 |
| 署名エラー(HealthKit) | 手順2のスクリプトを実行していない |
| 署名エラー(Bundle IDが使用中) | Bundle IDを更にユニークなものへ |
| `Unity-iPhone` がworkspaceで赤い | `ios/UnityExport/` のコピー漏れ(手順2) |
| アプリが7日で起動しなくなる | 無料アカウントの仕様。再ビルドで延長される |

## 7. 持ち帰るもの

- [ ] `RunLogs/Log_*.csv`(1本につき1ファイル) → `Docs/field-tests/YYYYMMDD/` へ格納
- [ ] 走行画面の録画(あれば)
- [ ] 発生した問題のメモ(Xcodeのエラーは全文をコピーしておく)

> 補足: 実機で継続的にビルドしたくなったら、リポジトリが公開のため
> GitHub Actions の macOS ランナーが無料で使える。Windowsでエクスポート →
> CIで `xcodebuild` のみ実行、という構成にすればMacを借りずに済む。
