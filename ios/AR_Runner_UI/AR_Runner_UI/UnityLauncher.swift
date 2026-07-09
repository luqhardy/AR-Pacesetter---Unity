import SwiftUI
import MachO
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
            // Unityにホストアプリの実行ヘッダを渡す（UaaL公式サンプルと同じ手順）
            let header = UnsafeMutablePointer<MachHeader>.allocate(capacity: 1)
            header.pointee = _mh_execute_header
            framework?.setExecuteHeader(header)
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
/// UnityFramework未リンク時はダーク背景のプレースホルダーを表示する。
struct UnityContainerView: UIViewRepresentable {

    func makeUIView(context: Context) -> UIView {
        if let unityView = UnityLauncher.shared.unityRootView {
            return unityView
        }

        // Fallback placeholder (simulator / UI-only development)
        let placeholder = UIView()
        placeholder.backgroundColor = UIColor(red: 0.04, green: 0.06, blue: 0.10, alpha: 1)

        let label = UILabel()
        label.text = "Unity AR View\n(UnityFramework 未リンク)"
        label.numberOfLines = 0
        label.textAlignment = .center
        label.textColor = UIColor(white: 0.6, alpha: 1)
        label.font = .systemFont(ofSize: 15, weight: .medium)
        label.translatesAutoresizingMaskIntoConstraints = false

        placeholder.addSubview(label)
        NSLayoutConstraint.activate([
            label.centerXAnchor.constraint(equalTo: placeholder.centerXAnchor),
            label.centerYAnchor.constraint(equalTo: placeholder.centerYAnchor)
        ])
        return placeholder
    }

    func updateUIView(_ uiView: UIView, context: Context) {
        // Unity側がビュー階層を自己管理するため更新処理は不要
    }
}
