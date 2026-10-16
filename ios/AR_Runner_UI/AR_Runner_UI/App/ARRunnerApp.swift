import SwiftUI

@main
struct AR_Runner_UIApp: App {
    // 外部ディスプレイ(ARグラス)シーンの構成を担当
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    /// 表示言語(スマホ設定で選ぶ・既定は日本語 — iPhone が英語でも日本語で出す)。
    /// 変えるとここが描き直され、全画面(シートを含む)の文言がその場で切り替わる
    @AppStorage(AppLanguage.storageKey) private var appLanguage = AppLanguage.defaultValue.rawValue

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(\.locale, Locale(identifier: AppLanguage.stored(appLanguage)
                    .localizationIdentifier(systemLocalization: Bundle.main.preferredLocalizations.first)))
        }
    }
}
