import SwiftUI
import UIKit
import Combine   // ObservableObject / @Published (Xcode 26 は SwiftUI 経由の暗黙再エクスポートをしない)

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

    /// グラス側ディスプレイの実解像度(px)とリフレッシュレート。Unityの画角プロファイル選択に使う。
    /// iOSから取れるのはここまでで、機種名・画角はグラスからは取得できない。
    @Published private(set) var displayPixelSize: CGSize = .zero
    @Published private(set) var displayRefreshHz: Double = 0

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

        // 実解像度(points × scale)とリフレッシュレート。XREAL Oneは1920×1080で受ける
        let screen = scene.screen
        let scale = screen.scale > 0 ? screen.scale : 1
        displayPixelSize = CGSize(width: screen.bounds.width * scale,
                                  height: screen.bounds.height * scale)
        displayRefreshHz = Double(screen.maximumFramesPerSecond)

        attachUnityViewIfPossible()

        // Unity側のReadyチェックを実接続で更新(手動タップと同じ経路)。
        // 併せて表示メトリクスを渡し、グラスの画角で描かせる
        UnityBridge.shared.connect(pixelWidth: Int(displayPixelSize.width.rounded()),
                                   pixelHeight: Int(displayPixelSize.height.rounded()),
                                   refreshHz: displayRefreshHz)
        print("[ExternalDisplay] ARグラス接続 — ARビューをグラスへ出力します " +
              "(\(Int(displayPixelSize.width))x\(Int(displayPixelSize.height)) @\(Int(displayRefreshHz))Hz)")
    }

    fileprivate func externalDisplayDisconnected() {
        externalWindow = nil
        isGlassesConnected = false
        displayPixelSize = .zero
        displayRefreshHz = 0
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
