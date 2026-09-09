import SwiftUI
import MachO
import Combine   // ObservableObject / @Published (Xcode 26 は SwiftUI 経由の暗黙再エクスポートをしない)
#if canImport(UnityFramework)
import UnityFramework
#endif

// MARK: - Unity Launcher
// Unity as a Library の起動を担当。
// UnityFramework がリンクされていない構成（SwiftUI単体開発・シミュレータ）では
// プレースホルダー表示にフォールバックし、既存のUI開発フローを妨げない。
//
// 使い方:
//   1. 走行画面に遷移する前に UnityLauncher.shared.launch()
//   2. SwiftUI内で UnityContainerView() を配置（Unityのカメラ映像+アバターが表示される）

final class UnityLauncher: ObservableObject {

    static let shared = UnityLauncher()

    @Published private(set) var isRunning = false

    /// Unityの初期化中（メインスレッドが塞がる区間）。
    ///
    /// **重要**: この区間はメインスレッドがブロックされるため、UI は一切更新されない。
    /// ローディング表示を出すなら `prepare(then:)` を使い、**表示を1フレーム描かせてから**
    /// 初期化に入ること。同期的に `launch()` を呼ぶと SwiftUI が描く隙が無く、
    /// ローディング画面は一度も画面に出ないまま終わる。
    /// またこの区間はアニメーションが止まるので、**スピナーは使わない**
    /// （止まったスピナーは「読み込み中」ではなく「ハングした」に見える）。
    @Published private(set) var isPreparing = false

    /// 初回起動の実測値（ms）。0 は未計測。内訳は起動ログに出る。
    @Published private(set) var lastLaunchMs: Double = 0

    /// 一度でも Unity を起動したか。2回目以降は初期化コストが掛からない。
    private(set) var hasLaunchedOnce = false

    /// 走行画面より前で Unity を先に温めるか。**既定 OFF（実機未検証のため）**。
    ///
    /// ON にすると初回の待ち時間が走行設定画面へ前倒しされ、走行開始時の待ちは消える。
    /// 初期化そのものは速くならないが、ユーザーが既に操作している画面へ移るので
    /// 体感の待ち時間は無くなる — 準備画面を綺麗にするより効果が大きい。
    ///
    /// **有効化する前に実機で確認すること**: 構成によっては `runEmbedded` が
    /// Unityのウィンドウを前面に出し、SwiftUIの画面に一瞬ちらつきが出る可能性がある。
    /// 発表前に入れるなら、必ず実機で設定画面→走行画面の遷移を通しで見ること。
    static var prewarmEnabled = false

    /// ローディング表示を確実に描かせてから Unity を初期化する。
    ///
    /// 初期化は同期でメインスレッドを塞ぐので、先に `isPreparing = true` を publish し、
    /// **RunLoop を1回まわして SwiftUI に描かせてから**ブロックする処理へ入る。
    /// - Parameter completion: 初期化完了後にメインスレッドで呼ばれる
    func prepare(then completion: (() -> Void)? = nil) {
        if isRunning && hasLaunchedOnce {
            launch()                 // 2回目以降は再開のみ（速い）
            completion?()
            return
        }

        isPreparing = true
        // async にすることで、ここで一度 SwiftUI に描画の機会を渡す。
        // これが無いとローディング画面は表示されないまま初期化が始まる
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.launch()
            self.isPreparing = false
            completion?()
        }
    }

#if canImport(UnityFramework)
    private var ufw: UnityFramework?

    /// Unityランタイムを埋め込みモードで起動する（初回のみ実体化、以降は表示再開）
    func launch() {
        if let ufw, isRunning {
            // 再走行: 前回終了時に pause(true) しているため必ず再開させる
            // (これが無いと2本目のARビューが停止画のまま固まる)
            ufw.pause(false)
            ufw.showUnityWindow()
            return
        }

        // 初回起動が遅い件の切り分け用。どこに時間が消えているかを内訳で残す。
        // 「初回だけ遅い」の主因は通常この2つで、対処法が違う:
        //   bundle.load()  — iOSによる巨大フレームワークの署名検証とページイン。
        //                    インストール後1回だけ。アプリ側で減らす余地は小さい
        //   runEmbedded()  — IL2CPPメタデータ初期化 + 最初のシーンロード。
        //                    シェーダのウォームアップやシーン軽量化で減らせる
        let tBundle = CFAbsoluteTimeGetCurrent()
        guard let framework = Self.loadUnityFramework() else {
            print("[UnityLauncher] UnityFramework.framework が見つかりません。" +
                  "Unityエクスポート産物がアプリターゲットに Embed されているか確認してください。")
            return
        }
        let bundleMs = (CFAbsoluteTimeGetCurrent() - tBundle) * 1000

        framework.setDataBundleId("com.unity3d.framework")

        let tRun = CFAbsoluteTimeGetCurrent()
        framework.runEmbedded(
            withArgc: CommandLine.argc,
            argv: CommandLine.unsafeArgv,
            appLaunchOpts: nil
        )
        let runMs = (CFAbsoluteTimeGetCurrent() - tRun) * 1000

        lastLaunchMs = bundleMs + runMs
        hasLaunchedOnce = true
        print(String(format: "[UnityLauncher] 起動 %.0fms (bundle.load %.0fms / runEmbedded %.0fms)",
                     lastLaunchMs, bundleMs, runMs))

        ufw = framework
        isRunning = true
    }

    /// 走行画面より前の画面で Unity を先に温めておく（初回のコストを前倒しする）。
    ///
    /// 初期化そのものを速くはできないが、**ユーザーが既に何かしている画面**へ
    /// 移せば体感の待ち時間は消える。呼んだ直後に休止させるので、
    /// 走行開始までの間 Unity は描画もARKitも回さない。
    ///
    /// - Note: 実機未検証。`runEmbedded` がUnityのウィンドウを前面に出す構成では
    ///   一瞬ちらつく可能性があるため、有効化する前に実機で確認すること。
    func prewarm() {
        guard Self.prewarmEnabled, !hasLaunchedOnce else { return }
        prepare { [weak self] in
            self?.pause()
        }
    }

    /// Unityの描画ビュー。UnityContainerView から参照される。
    var unityRootView: UIView? {
        ufw?.appController()?.rootView
    }

    /// 走行画面を離れるときに呼ぶ（Unityは休止するが破棄はしない）
    func pause() {
        ufw?.pause(true)
    }

    func resume() {
        ufw?.pause(false)
    }

    private static func loadUnityFramework() -> UnityFramework? {
        let bundlePath = Bundle.main.bundlePath + "/Frameworks/UnityFramework.framework"
        guard let bundle = Bundle(path: bundlePath) else { return nil }

        if !bundle.isLoaded {
            bundle.load()
        }

        guard let frameworkClass = bundle.principalClass as? UnityFramework.Type else {
            return nil
        }

        let framework = frameworkClass.getInstance()
        if framework?.appController() == nil {
            // Unityにホストアプリ（メイン実行ファイル）のMachヘッダを渡す。
            //
            // UaaL公式サンプルは `_mh_execute_header` を直接参照するが、Swiftから
            // 参照すると Xcode 26 のリンカで "Undefined symbol: __mh_execute_header"
            // になる。この記号は実行ファイルのリンク時にのみ供給されるものであり、
            // dyld から実行中イメージのヘッダを取ればリンク時記号に依存しない。
            // インデックス0は常にメイン実行ファイル。
            //
            // 併せて、以前はヘッダを構造体ごとコピーして渡していたが誤り。
            // Unityはヘッダ直後に続くロードコマンドを走査するため、
            // 構造体だけ複製したポインタでは不正なメモリを読むことになる。
            // 実体のポインタをそのまま渡す（確保も解放も不要）。
            if let mainImage = _dyld_get_image_header(0) {
                framework?.setExecuteHeader(
                    UnsafeRawPointer(mainImage).assumingMemoryBound(to: MachHeader.self))
            }
        }
        return framework
    }
#else
    // UnityFramework未リンク時のダミー実装（シミュレータ・UI単体開発用）
    func launch() { isRunning = true; hasLaunchedOnce = true }
    func prewarm() {}
    var unityRootView: UIView? { nil }
    func pause() {}
    func resume() {}
#endif
}

// MARK: - SwiftUI Container

/// UnityのARビューをSwiftUI階層に埋め込むコンテナ。
/// ARグラス(外部ディスプレイ)接続中はビューをグラス側へ譲り、
/// 切断されたらこのコンテナへ自動で回収する。
/// UnityFramework未リンク時はダーク背景のプレースホルダーを表示する。
struct UnityContainerView: UIViewRepresentable {

    // 変化時に updateUIView を発火させるための購読
    @ObservedObject private var unity = UnityLauncher.shared
    @ObservedObject private var external = ExternalDisplayManager.shared

    private static let placeholderTag = 990

    func makeUIView(context: Context) -> UIView {
        let container = UIView()
        container.backgroundColor = UIColor(red: 0.04, green: 0.06, blue: 0.10, alpha: 1)

        let label = UILabel()
        label.tag = Self.placeholderTag
        label.numberOfLines = 0
        label.textAlignment = .center
        label.textColor = UIColor(white: 0.6, alpha: 1)
        label.font = .systemFont(ofSize: 15, weight: .medium)
        label.translatesAutoresizingMaskIntoConstraints = false

        container.addSubview(label)
        NSLayoutConstraint.activate([
            label.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            label.centerYAnchor.constraint(equalTo: container.centerYAnchor),
            label.leadingAnchor.constraint(greaterThanOrEqualTo: container.leadingAnchor, constant: 24),
        ])
        return container
    }

    func updateUIView(_ container: UIView, context: Context) {
        let placeholder = container.viewWithTag(Self.placeholderTag) as? UILabel

        guard let unityView = unity.unityRootView else {
            placeholder?.text = "Unity AR View\n(UnityFramework 未リンク)"
            placeholder?.isHidden = false
            return
        }

        if external.isGlassesConnected {
            // ARグラスへ出力(グラス側ウィンドウにまだ載っていなければ移設)
            ExternalDisplayManager.shared.attachUnityViewIfPossible()
            placeholder?.text = "ARビューはグラスに出力中\n(iPhoneは操作パネル)"
            placeholder?.isHidden = false
        } else {
            // iPhone側で表示(グラス切断時の回収を含む)
            if unityView.superview !== container {
                unityView.frame = container.bounds
                unityView.autoresizingMask = [.flexibleWidth, .flexibleHeight]
                container.addSubview(unityView)
            }
            placeholder?.isHidden = true
        }
    }
}
