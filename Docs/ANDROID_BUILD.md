# Android ビルド(動作確認用 — 第1期のスコープ外)

第1期の正式構成は **iPhone + ARグラス**(AGENTS.md §2)。Android は「Unity 部分が Android でも動くか」を
確かめるためのテスト APK だけを用意している。2026-10-13 に初めてビルドが通った(実機での動作は未確認)。

## 作り方

Unity エディタでプロジェクトを開いていない状態で:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe" -batchmode -quit -buildTarget Android \
  -projectPath "<repo>" -executeMethod AndroidBuildExporter.BuildTestApk -logFile android-build.log
```

(エディタからは Build → Android test APK)。出力は `Builds/Android/ARPacesetter-test.apk`(git 管理外)。
初回は IL2CPP のネイティブコンパイルで 10〜20 分、空きディスクが数GB要る。

**Unity のアクティブなプラットフォームが Android に切り替わる。** 次の iOS エクスポートは `-buildTarget iOS`
で戻る(アセットの再インポートに時間がかかる)。ビルド後に `git status` で URP / XR シミュレーションの
設定に差分が出ていたら、プラットフォーム切替の副作用なので `git checkout --` で戻す。

## 入れ方

ARCore 対応・Android 11 以降・Google Play Services for AR がある端末(arm64)。

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe" install -r Builds/Android/ARPacesetter-test.apk
```

ログは `adb logcat -s Unity`。マニフェスト上 ARCore の **Depth API が必須**(`com.google.ar.core.depth`)。

## iPhone 版との違い

| | iPhone | Android(テストAPK) |
|---|---|---|
| ホーム・設定・体験モードのスタッフ画面・開発者モード | SwiftUI | **無い**(Unity の簡易セットアップ画面のみ) |
| M2P 実測・100Hz ネイティブ IMU | iOS ネイティブ(`.mm`) | **無い**(CSV の `latency_m2p` は -1) |
| LiDAR の面分類 | ARKit | 無い(検出平面へ自動フォールバック) |
| ARグラス出力 | 外部ディスプレイのシーン | 未検証 |
| グラスの頭部姿勢 | iOS からは取得不可 | XREAL SDK(Android専用)なら取得できる可能性 — 未着手 |

## よくある失敗

| 症状 | 原因と対処 |
|---|---|
| `CS0234: 'ARKit' does not exist in the namespace 'UnityEngine.XR'` | ARKit のアセンブリは iOS ビルドにしか無い。ARKit を使うコードは `#if UNITY_IOS && !UNITY_EDITOR` の中だけに置く(#4 で `EnvironmentSceneScanner` を修正) |
| `Namespace 'com.google.ar.core' is used in multiple modules` | Unity 6000.3 の AGP 9.0 が、ARCore パッケージ 6.4.2 同梱の `arcore_client` と `unityandroidpermissions` の namespace 重複をエラーにする。`AndroidBuildExporter.cs` の `ArCoreNamespaceGradleFix` が AGP の暫定フラグ `android.uniquePackageNames=false` を足して回避している(ARCore パッケージ側で直れば削除) |
| IL2CPP で `ディスクに十分な空き領域がありません` | 空きディスク不足。数GB空ける |
