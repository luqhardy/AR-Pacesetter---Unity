import SwiftUI
import UIKit

// MARK: - External Display (ARグラス) Manager
// XREAL One は iPhone(USB-C) に対して外部ディスプレイとして振る舞うため、
// NRSDK なしでも UIWindowScene(externalDisplay) に Unity のARビューを出せば
// 「グラスにアバター・iPhoneに操作UI」という本来の構成が成立する。
//
// 接続の流れ:
//   グラス接続 → iOSが外部ディスプレイシーンを生成 → ExternalSceneDelegate
//   → ExternalDisplayManager が黒背景ウィンドウを作成し Unity ビューを移設
//   グラス切断 → phone側 UnityContainerView が updateUIView で回収
final class ExternalDisplayManager: ObservableObject {

    static let shared = ExternalDisplayManager()

    /// ARグラス(外部ディスプレイ)が接続中かどうか
    @Published private(set) var isGlassesConnected = false

    fileprivate var externalWindow: UIWindow?

    private init() {}

    fileprivate func externalDisplayConnected(scene: UIWindowScene) {
        let window = UIWindow(windowScene: scene)
        let host = UIViewController()
        host.view.backgroundColor = .black // 透過型グラスでは黒=非表示
        window.rootViewController = host
        window.isHidden = false
        externalWindow = window
        isGlassesConnected = true

        attachUnityViewIfPossible()

        // Unity側のReadyチェックを実接続で更新(手動タップと同じ経路)
        UnityBridge.shared.connect()
        print("[ExternalDisplay] ARグラス接続 — ARビューをグラスへ出力します")
    }

    fileprivate func externalDisplayDisconnected() {
        externalWindow = nil
        isGlassesConnected = false
        print("[ExternalDisplay] ARグラス切断 — ARビューをiPhoneへ戻します")

        // §8.3: Unityをスタンバイへ(アバター消去)。走行記録・CSVログは継続し、
        // 再接続後は準備画面からの再スタートを待つ
        UnityBridge.shared.disconnectGlass()
        // Unityビューの回収は phone 側 UnityContainerView.updateUIView が行う
    }

    /// Unity起動後・グラス接続後に呼ぶと、ARビューをグラス側ウィンドウへ移設する。
    /// 何度呼んでも安全(既に載っていれば何もしない)。
    func attachUnityViewIfPossible() {
        guard isGlassesConnected,
              let hostView = externalWindow?.rootViewController?.view,
              let unityView = UnityLauncher.shared.unityRootView,
              unityView.superview !== hostView else { return }

        unityView.frame = hostView.bounds
        unityView.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        hostView.addSubview(unityView)
    }
}

// MARK: - AppDelegate
// SwiftUIライフサイクルに外部ディスプレイ用シーンの構成を追加する
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     configurationForConnecting connectingSceneSession: UISceneSession,
                     options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        if connectingSceneSession.role == .windowExternalDisplayNonInteractive {
            let config = UISceneConfiguration(name: "ARGlassDisplay",
                                              sessionRole: connectingSceneSession.role)
            config.delegateClass = ExternalSceneDelegate.self
            return config
        }
        return UISceneConfiguration(name: nil, sessionRole: connectingSceneSession.role)
    }
}

// MARK: - External Scene Delegate
final class ExternalSceneDelegate: NSObject, UIWindowSceneDelegate {
    func scene(_ scene: UIScene,
               willConnectTo session: UISceneSession,
               options connectionOptions: UIScene.ConnectionOptions) {
        guard let windowScene = scene as? UIWindowScene else { return }
        DispatchQueue.main.async {
            ExternalDisplayManager.shared.externalDisplayConnected(scene: windowScene)
        }
    }

    func sceneDidDisconnect(_ scene: UIScene) {
        DispatchQueue.main.async {
            ExternalDisplayManager.shared.externalDisplayDisconnected()
        }
    }
}
