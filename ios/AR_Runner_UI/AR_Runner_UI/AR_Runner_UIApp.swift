import SwiftUI

@main
struct AR_Runner_UIApp: App {
    // 外部ディスプレイ(ARグラス)シーンの構成を担当
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
