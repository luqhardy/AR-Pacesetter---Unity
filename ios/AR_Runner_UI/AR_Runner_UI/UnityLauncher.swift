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

        guard let framework = Self.loadUnityFramework() else {
            print("[UnityLauncher] UnityFramework.framework が見つかりません。" +
                  "Unityエクスポート産物がアプリターゲットに Embed されているか確認してください。")
            return
        }

        framework.setDataBundleId("com.unity3d.framework")
        framework.runEmbedded(
            withArgc: CommandLine.argc,
            argv: CommandLine.unsafeArgv,
            appLaunchOpts: nil
        )

        ufw = framework
        isRunning = true
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
    func launch() { isRunning = true }
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
