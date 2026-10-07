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

    /// グラスは挿さっているのに、iOS がアプリ専用の画面(外部ディスプレイのシーン)を作らず
    /// **iPhoneの画面をミラーリングしている**か。診断表示用。
    ///
    /// このときグラスには iPhone の画面(カメラ映像・ボタン)がそのまま映り、`isGlassesConnected`
    /// は false のまま — 「グラス未接続」と区別がつかないと原因に辿り着けない(2026-10-07 実機で発生)。
    @Published private(set) var isMirroring = false

    fileprivate var externalWindow: UIWindow?
    /// 移設前(iPhone)の描画スケール。切断時に戻すために保持する
    private var phoneContentScale: CGFloat?

    private init() {
        // UIScreen の接続通知と UIScreen.screens は iOS 16 で非推奨だが、シーンを持たない
        // (=ミラーリング中の)画面を知る手段は他に無い。診断表示にだけ使う
        for name in [UIScreen.didConnectNotification, UIScreen.didDisconnectNotification] {
            NotificationCenter.default.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                self?.refreshMirroring()
            }
        }
        DispatchQueue.main.async { [weak self] in self?.refreshMirroring() }
    }

    /// 外部画面はあるのに専用シーンが無い = ミラーリング
    func refreshMirroring() {
        let hasExternalScreen = UIScreen.screens.count > 1
        isMirroring = hasExternalScreen && !isGlassesConnected
        if isMirroring {
            print("[ExternalDisplay] 外部画面は接続されているが、iOSはミラーリング中 — " +
                  "外部ディスプレイのシーンが作られていない(Info.plist の UIApplicationSceneManifest を確認)")
        }
    }

    fileprivate func externalDisplayConnected(scene: UIWindowScene) {
        let window = UIWindow(windowScene: scene)
        let host = UIViewController()
        host.view.backgroundColor = .black // 透過型グラスでは黒=非表示
        window.rootViewController = host
        window.isHidden = false
        externalWindow = window
        isGlassesConnected = true
        isMirroring = false

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
        refreshMirroring()
        displayPixelSize = .zero
        displayRefreshHz = 0
        print("[ExternalDisplay] ARグラス切断 — ARビューをiPhoneへ戻します")

        // 描画スケールをiPhoneへ戻す(戻さないとグラスの@1xのままiPhoneで描かれ、
        // Retinaの1/3解像度でぼやける)
        restorePhoneRenderScale()

        // §8.3: Unityをスタンバイへ(アバター消去)。走行記録・CSVログは継続し、
        // 再接続後は準備画面からの再スタートを待つ
        UnityBridge.shared.disconnectGlass()
        // Unityビューの回収は phone 側 UnityContainerView.updateUIView が行う
    }

    /// Unity起動後・グラス接続後に呼ぶと、ARビューをグラス側ウィンドウへ移設する。
    /// 何度呼んでも安全(既に載っていれば何もしない)。
    ///
    /// - Important: **移設しただけでは描画解像度は追従しない**。Unityのレンダリング面
    ///   (CAMetalLayer)の大きさは「ビューのbounds × contentScaleFactor」で決まるため、
    ///   iPhone(例: @3x の縦長)のスケールのままグラス(1920×1080 @1x の横長)へ載せると、
    ///   縦横比が合わずに引き伸ばし/レターボックスになり、しかも描画面積が数倍になって
    ///   60fpsとM2P 20msの予算を壊す。**スケールを移設先の画面に合わせ、
    ///   レイアウトを確定させて面を作り直させる**必要がある。
    func attachUnityViewIfPossible() {
        guard isGlassesConnected,
              let window = externalWindow,
              let hostView = window.rootViewController?.view,
              let unityView = UnityLauncher.shared.unityRootView,
              unityView.superview !== hostView else { return }

        let screen = window.windowScene?.screen ?? UIScreen.main

        // 移設元(iPhone)のスケールを覚えておき、切断時に正しく戻せるようにする
        if phoneContentScale == nil { phoneContentScale = unityView.contentScaleFactor }

        window.frame = screen.bounds
        hostView.frame = window.bounds

        unityView.frame = hostView.bounds
        unityView.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        hostView.addSubview(unityView)

        applyRenderScale(screen.scale, to: unityView)
        forceLayout(unityView, label: "attach")
    }

    /// iPhone側へ戻すときに描画スケールを元へ戻す。
    /// `UnityContainerView` がビューを回収する前に呼ぶこと。
    func restorePhoneRenderScale() {
        guard let unityView = UnityLauncher.shared.unityRootView,
              let scale = phoneContentScale else { return }

        applyRenderScale(scale, to: unityView)
        forceLayout(unityView, label: "restore")
        phoneContentScale = nil
    }

    // MARK: - 描画面の追従

    /// 移設先の画面に描画スケールを合わせ、レイアウトを確定させる。
    /// グラス側への移設・iPhone側への回収の**両方**から呼ぶ(片側だけだと解像度がずれたままになる)。
    func matchRenderScale(of view: UIView, to screen: UIScreen, label: String) {
        applyRenderScale(screen.scale, to: view)
        forceLayout(view, label: label)
    }

    /// Unityのレンダリング面はビュー階層のどこに載っているか(rootView直下とは限らない)ため、
    /// 子孫まで再帰的にスケールを揃える。Unityの内部型に依存しないのが要点。
    private func applyRenderScale(_ scale: CGFloat, to view: UIView) {
        view.contentScaleFactor = scale
        view.layer.contentsScale = scale
        for sub in view.subviews { applyRenderScale(scale, to: sub) }
    }

    /// レイアウトを確定させ、Unityの `layoutSubviews` にレンダリング面を作り直させる。
    ///
    /// 2回流すのは、ウィンドウがキーになる前の1回目ではboundsが未確定なことがあるため
    /// (端末回転時にUnity自身が面を作り直すのと同じ経路に乗せている)。
    private func forceLayout(_ view: UIView, label: String) {
        view.setNeedsLayout()
        view.layoutIfNeeded()

        DispatchQueue.main.async { [weak self] in
            view.setNeedsLayout()
            view.layoutIfNeeded()
            self?.logRenderSurface(view, label: label)
        }
    }

    private func logRenderSurface(_ view: UIView, label: String) {
        let px = CGSize(width: view.bounds.width * view.contentScaleFactor,
                        height: view.bounds.height * view.contentScaleFactor)
        let aspect = px.height > 0 ? px.width / px.height : 0
        print("[ExternalDisplay] \(label): 描画面 \(Int(px.width))x\(Int(px.height)) " +
              "(scale \(view.contentScaleFactor), アスペクト \(String(format: "%.3f", aspect)))")
    }
}

// MARK: - AppDelegate
// SwiftUIライフサイクルに外部ディスプレイ用シーンの構成を追加する
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     configurationForConnecting connectingSceneSession: UISceneSession,
                     options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        // グラスがミラーリングになるとき、ここに外部ディスプレイの役割が来ているかが切り分けの起点
        print("[ExternalDisplay] シーン構成の要求: role=\(connectingSceneSession.role.rawValue)")
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
