import Foundation

// MARK: - 表示言語
//
// スマホ設定で選ぶアプリの表示言語。**既定は日本語** — iPhone が英語でも日本語で出す
// (相手側 hsuyaminmyat625/AR_project 466dd48「force app language to Japanese」の目的)。
// 英語は選べば使える。「端末に合わせる」は iPhone の設定(このアプリの言語)に従う。
//
// 適用は画面側(App のルートで \.locale・文字列は AppStrings.resolve・日付は Locale.appLanguage)。
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum AppLanguage: String, CaseIterable, Codable {
    case japanese = "ja"
    case english = "en"
    case system

    /// UserDefaults(@AppStorage)のキー
    static let storageKey = "appLanguage"
    static let defaultValue: AppLanguage = .japanese

    /// 保存値を読む。無い・知らない値なら既定(日本語)
    static func stored(_ raw: String?) -> AppLanguage {
        raw.flatMap(AppLanguage.init(rawValue:)) ?? defaultValue
    }

    /// 画面に使う言語のコード(ja / en)。
    /// - Parameter systemLocalization: iOS がこのアプリ用に選んだ言語(`Bundle.main.preferredLocalizations.first`)。
    ///   アプリにある言語は日本語と英語だけなので、それ以外(Base 等)は元の言語の日本語にする
    func localizationIdentifier(systemLocalization: String?) -> String {
        switch self {
        case .japanese: return "ja"
        case .english: return "en"
        case .system:
            return (systemLocalization ?? "").lowercased().hasPrefix("en") ? "en" : "ja"
        }
    }
}
