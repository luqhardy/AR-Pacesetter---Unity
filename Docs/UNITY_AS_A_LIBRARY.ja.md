# SwiftUIアプリへ Unity as a Library で組み込む

*English version: [UNITY_AS_A_LIBRARY.md](UNITY_AS_A_LIBRARY.md)*

SwiftUIアプリの中にUnityを埋め込んで1本のアプリとして出す(Unity as a Library、以下UaaL)ための実践ガイド。
実際に動かして得た知見をまとめたもので、ここに挙げた落とし穴はすべて**実際に踏んだもの**です。

確認環境: **Unity 6000.3.17f1** / **Xcode 26** / iOS実機ビルド。

---

## 1. 考え方

最初につまずくのはここです。

> **Unityはアプリ本体ではなく、アプリが読み込むフレームワークである。**

- **SwiftUIアプリがホスト**。アプリのライフサイクル・ウィンドウ・画面遷移・権限はすべてホストが持つ
- **Unityは `UnityFramework.framework` になり**、任意の場所に置いた `UIView` へ描画する
- Unity側だけをビルドしても「Unityだけのアプリ」にしかならず、SwiftUI画面は一切入らない。
  最終アプリは常に**ホストアプリのスキーム**からビルドする

つまり流れは *Unityが Xcodeプロジェクトを書き出す → ホストがその成果物のフレームワークをリンクする → ホストをビルドする* です。

## 2. 構成

```
repo/
├── Assets/ ProjectSettings/        Unityプロジェクト(このリポジトリではルート)
│   └── Editor/IOSBuildExporter.cs  エクスポートを実行するメニュー項目
├── ios/
│   ├── ARRunner.xcworkspace        ← 開くのはこれ(どちらの .xcodeproj でもない)
│   ├── AR_Runner_UI/               SwiftUIホストアプリ
│   └── UnityExport/                Unityのエクスポート産物(生成物・gitignore)
```

ワークスペースは「1つのXcodeウィンドウから両プロジェクトを見えるようにする」ためだけに存在します。
ホストがUnity側のビルド成果物を参照できるようにするのが目的です。
エクスポート産物は**バージョン管理に入れない**こと(生成物であり、約1.5GBあります)。

## 3. 手順1 — Unityから Xcodeプロジェクトを書き出す

この工程はUnityが動く環境ならどこでも実行でき、**Windowsでも可能**です。Macが要るのは手順2だけ。

```bash
Unity.exe -batchmode -quit -projectPath <repo> \
  -executeMethod IOSBuildExporter.ExportIOS -logFile export.log
```

エクスポータの実体は `BuildPipeline.BuildPlayer` を `BuildTarget.iOS` で呼ぶ数行で、
出力先はワークスペースが期待する固定パスにします。

**失敗時に終了コードを返すようにしてください。** Unityの `-quit` はビルドが失敗しても0を返すため、
CIもシェルも壊れたエクスポートを成功として扱ってしまいます。

```csharp
if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode)
    EditorApplication.Exit(1);
```

### エクスポートが失敗する前提条件

| 必要なもの | 欠けたときの症状 |
|---|---|
| 対象Unityバージョンの **iOS Build Support** モジュール | 即座にエクスポート失敗 |
| 使用するAPI分の **Player Settings 使用目的文**(マイク・位置情報・Bluetooth等) | `BuildFailedException: Microphone class is used but Microphone Usage Description is empty` |
| 数GBの空きディスク | Burstが容量不足のIOエラーで落ち、プロジェクトが中途半端な状態で残る |

Player Settingsの使用目的文は、ホストアプリ側の `INFOPLIST_KEY_*` とは**別物**です。
エクスポートを通すのにUnity側、実行時の権限ダイアログにホスト側と、**両方**必要になります。

### エクスポート後に `ProjectSettings.asset` を確認する

ビルド処理が **`preloadedAssets` を空にする**ことがあります。ここに `XRGeneralSettings.asset` が
含まれている場合、空のままコミットするとプレイヤービルドでXR(ARKit)ローダーが初期化されません。
**実機でしか露見しない**種類の不具合なので、毎回差分を見て必要なら戻してください。

## 4. 手順2 — Xcodeでフレームワークを繋ぐ(macOS・エクスポートごとに1回)

`ios/ARRunner.xcworkspace` を開きます。ナビゲータに両方のプロジェクトが見えている必要があります。

1. **ホストのターゲット → General → Frameworks, Libraries, and Embedded Content → `+` → `UnityFramework.framework`**
   (パスを辿るのではなくUnityプロジェクトの成果物から選ぶ)。追加後 **Embed & Sign** にする。
   "Do Not Embed" のままだとビルドは通り、起動時に `dyld: Library not loaded` で落ちます
2. **Unity-iPhone プロジェクト → `Data` フォルダ → File Inspector → Target Membership → `UnityFramework`**。
   これを忘れると undefined symbols のリンクエラーになります
3. **ホストアプリのスキーム**を選び、**実機**へビルド

エクスポート産物を作り直したら1と2はやり直しです。設定は*エクスポート側*のプロジェクトにあり、
それを丸ごと置き換えたためです。

### `ld: framework not found UnityFramework`

`UnityFramework.framework` は `sourceTree = BUILT_PRODUCTS_DIR` で宣言されています。
ディスク上のファイルではなく**ビルド成果物**なので、先に誰もビルドしなければリンカは空の
ディレクトリを探しにいきます。**Build Phases → Target Dependencies** に追加するか、
スキームの **Build** 一覧でホストより前に置いて解決します。

### そもそも候補一覧にターゲットが出てこない

Unityは `SUPPORTED_PLATFORMS = iphoneos`、つまり**実機専用**で書き出します。
Destinationがシミュレータの間、XcodeはTarget Dependenciesにもスキームの一覧にも
そのターゲットを**表示しません**。先に実機を選んでください(ARKitはどのみち実機必須です)。

## 5. 手順3 — ブリッジ

方向ごとに仕組みが違います。

### Swift → Unity

```swift
UnityFramework.getInstance()?.sendMessageToGO(
    withName: "ARSessionManager",       // GameObject名 — 完全一致が必要
    functionName: "OnSwiftCommand",     // そこに付いたMonoBehaviourのpublicメソッド
    message: jsonString)
```

Unity側は `public void OnSwiftCommand(string json)` を持つ `MonoBehaviour`。
受信用のGameObjectは**実行時に生成**するとシーン配線が壊れる心配がありません。

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Bootstrap() { /* new GameObject("ARSessionManager").AddComponent<Bridge>(); */ }
```

メソッドを増やすのではなく、`command` フィールドを持つ**JSON文字列1本**に集約するのがおすすめです。
入口が1つなら、バージョン管理もログ出力もはるかに楽になります。

### Unity → Swift

UnityからSwiftは直接呼べません。`Assets/Plugins/iOS/` に置いたObjective-C++を経由します。

```objc
extern "C" void UnitySendMessageToSwift(const char *json) {
    NSString *m = [NSString stringWithUTF8String:json];
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:@"UnityToSwiftMessage"
                                                           object:nil
                                                         userInfo:@{@"json": m}];
    });
}
```

```csharp
[DllImport("__Internal")] static extern void UnitySendMessageToSwift(string json);
```

Swift側はこの通知名を購読します。Unityは自前のスレッドから呼ぶことがあるため、
**メインキューへ渡す**のを忘れないでください。

**重複シンボルに注意。** `Assets/Plugins/iOS/` 配下の `.mm` はすべてフレームワークへコンパイルされます。
2つのファイルが同じ `extern "C"` 関数を定義しているとリンクエラーになりますが、これは
**C#のコンパイルにもユニットテストにもエディタのPlay Modeにも現れず、実機リンクでしか検出できません**。

## 6. 手順4 — Unityを起動しビューを載せる

```swift
let bundle = Bundle(path: Bundle.main.bundlePath + "/Frameworks/UnityFramework.framework")
bundle?.load()
let ufw = (bundle?.principalClass as? UnityFramework.Type)?.getInstance()
ufw?.setExecuteHeader(...)                  // 下記参照
ufw?.runEmbedded(withArgc: CommandLine.argc, argv: CommandLine.unsafeArgv, appLaunchOpts: nil)
```

あとは `ufw.appController()?.rootView` を `UIViewRepresentable` で包み、SwiftUI階層へ置くだけです。

### `Undefined symbol: __mh_execute_header`

Unityのサンプルは `&_mh_execute_header` を渡しますが、この記号はリンカが**実行ファイルにのみ**
供給するものです。Swiftから参照すると最近のツールチェーンでは解決できません。
dyld から取得してください(インデックス0は常にメイン実行ファイル)。

```swift
if let header = _dyld_get_image_header(0) {
    ufw.setExecuteHeader(UnsafeRawPointer(header).assumingMemoryBound(to: MachHeader.self))
}
```

**コピーではなく実体のポインタを渡すこと。** Unityはヘッダ直後に続くロードコマンドを走査するため、
構造体だけ複製したポインタを渡すと、先頭32バイトの先は無関係なヒープを読むことになります。

### 「動いているように見えるが繋がっていない」罠

`#if canImport(UnityFramework)` で囲って `#else` にスタブを置くのはシミュレータ開発に便利ですが、
**危険**でもあります。フレームワークが未リンクだと、**アプリはビルドも起動もでき、見た目も正常**なまま
何も繋がっていない状態になります。フォールバックは**明らかに偽物と分かる**ものにしてください。
「未リンク」と表示するプレースホルダーは可、それらしい乱数は不可。
私たちは目標値の範囲内のレイテンシを返すスタブを置いており、実測値として記録されかねない状態でした。

## 7. エクスポート産物を別マシンへ運ぶ

サイズは大きいものの圧縮はよく効きます(`tar czf` で 1.5GB → 約334MB)。
ただし **Windowsで固めた場合**、拡張子の無いMach-Oバイナリの実行ビットが失われるため、macOS側で:

```bash
chmod +x ios/UnityExport/usymtool ios/UnityExport/usymtoolarm64 ios/UnityExport/*.sh
xattr -dr com.apple.quarantine ios/UnityExport   # ダウンロード由来の場合
```

1行目が無いと `Command PhaseScriptExecution failed with a nonzero exit code` で落ちます
(`process_symbols.sh` が `usymtool` を起動できないため)。UnityのIL2CPPフェーズは自前で `chmod` するので
影響を受けず、そのせいで**原因が分かりにくい**失敗になります。

## 8. 落とし穴一覧

| 症状 | 原因 |
|---|---|
| 起動直後に `dyld: Library not loaded` | UnityFrameworkが **Embed & Sign** になっていない |
| リンク時に undefined symbols | `Data` フォルダの Target Membership が UnityFramework でない |
| `ld: framework not found UnityFramework` | UnityFrameworkが未ビルド。ターゲット依存を追加する |
| 候補一覧にターゲットが出ない | Destinationがシミュレータ。Unityは実機専用で書き出す |
| `Undefined symbol: __mh_execute_header` | `_dyld_get_image_header(0)` を使う |
| リンク時に duplicate symbol | 2つの `.mm` が同じ関数を定義している |
| `PhaseScriptExecution failed` | 転送で `usymtool` の実行ビットが落ちた |
| マイク・位置情報でエクスポート失敗 | Unity **Player Settings** の使用目的文が空 |
| 動くが何も繋がっていない | `#if canImport(UnityFramework)` がスタブへ落ちている |
| 実機でARKitが初期化されない | エクスポートが `preloadedAssets` を空にした |
| `does not conform to 'ObservableObject'` | Xcode 26はSwiftUI経由でCombineを再エクスポートしない。`import Combine` |

## 9. 自動化する価値があるもの

上の失敗のほとんどは、macOSランナー上で数分で捕まる**コンパイル/リンクエラー**です。
実機も署名も要りません(`CODE_SIGNING_ALLOWED=NO`)。
エクスポートと実機ビルドを別マシンで行う構成なら、このCIジョブはすぐに元が取れます。

本リポジトリの実装は [`.github/workflows/ios-build.yml`](../.github/workflows/ios-build.yml)。
GitHub Releaseからエクスポート産物を取得し、2つをビルドします:

1. **`UnityFramework`** — `.mm` プラグインの重複シンボルとIL2CPPのリンクエラーを検出する。
   C#のコンパイルにもユニットテストにもエディタのPlay Modeにも現れない種類の失敗
2. **ホストアプリ** — Swift側のコンパイルエラーを検出する

注意点: フレームワークのリンク設定(Embed & Sign / `Data` の Target Membership)を
Xcodeで手作業する運用のままだと、CIでは `canImport(UnityFramework)` が **false** で
コンパイルされるため、**フレームワーク有りの分岐は検証されません**。
リンク設定を `pbxproj` へコミットすればこの穴は塞がります。

---

関連: このプロジェクト固有のメッセージ契約は [SWIFT_INTEGRATION.md](../SWIFT_INTEGRATION.md)、
他人のMacを借りて1回だけビルドする手順は [BUILD_ON_BORROWED_MAC.md](BUILD_ON_BORROWED_MAC.md) を参照。
