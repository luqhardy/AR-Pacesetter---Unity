import SwiftUI

/// 利用者に見える文言の一覧(hsuyaminmyat625/AR_project の AppStrings を移植・拡張)。
///
/// 画面は文字列を直接書かず、ここのキーを使う。日本語・英語の値は `Resources/Localizable.xcstrings`
/// (日本語が元の言語)。キーと値の対応は ios/AppLogicTests/LocalizationCatalogTests が CI で確かめる —
/// キーを足したらカタログにも ja / en を足すこと(無いと画面にキーがそのまま出る)。
///
/// 日本語のままの画面: 体験モードのスタッフ画面(BoothDemoView)・開発者モード(DevModeView)。
/// Unity から届く文言(アバター非表示の理由・VRM の判定結果)とグラスの HUD も日本語のまま。
enum AppStrings {
    /// 複数の画面で使う
    enum Common {
        static let back: LocalizedStringKey = "common.back"
        static let next: LocalizedStringKey = "common.next"
        static let close: LocalizedStringKey = "common.close"
        static let finish: LocalizedStringKey = "common.finish"
        static let end: LocalizedStringKey = "common.end"
        static let continueRun: LocalizedStringKey = "common.continue"
        static let openSettings: LocalizedStringKey = "common.openSettings"
        static let changeInSettings: LocalizedStringKey = "common.changeInSettings"
        static let endRunTitle: LocalizedStringKey = "common.endRun.title"
        static let noRecords: LocalizedStringKey = "common.noRecords"
        static let swipeToUnlock: LocalizedStringKey = "common.swipeToUnlock"
        static let settingsEyebrow: LocalizedStringKey = "settings.eyebrow"
    }

    /// ホーム(S3)
    enum Home {
        static let lastRun: LocalizedStringKey = "home.lastRun"
        static let latestStatus: LocalizedStringKey = "home.latestStatus"
        static let history: LocalizedStringKey = "home.history"
        static let arSettings: LocalizedStringKey = "home.arSettings"
        static let phoneSettings: LocalizedStringKey = "home.phoneSettings"
        static let tutorial: LocalizedStringKey = "home.tutorial"
        static let safety: LocalizedStringKey = "home.safety"
        static let boothDemo: LocalizedStringKey = "home.boothDemo"
        static let devMode: LocalizedStringKey = "home.devMode"
        static let date: LocalizedStringKey = "home.stat.date"
        static let time: LocalizedStringKey = "home.stat.time"
        static let distance: LocalizedStringKey = "home.stat.distance"
        static let speed: LocalizedStringKey = "home.stat.speed"
        static let runTogether: LocalizedStringKey = "home.runTogether"
        static let glassesConnected: LocalizedStringKey = "home.glasses.connected"
        static let glassesNotConnected: LocalizedStringKey = "home.glasses.notConnected"
        static let getStarted: LocalizedStringKey = "home.getStarted"
    }

    /// 位置情報の案内
    enum Location {
        static let permissionTitle: LocalizedStringKey = "location.permission.title"
        static let permissionMessage: LocalizedStringKey = "location.permission.message"
        static let restrictedTitle: LocalizedStringKey = "location.restricted.title"
        static let restrictedMessage: LocalizedStringKey = "location.restricted.message"
        static let servicesOffTitle: LocalizedStringKey = "location.servicesOff.title"
        static let servicesOffMessage: LocalizedStringKey = "location.servicesOff.message"
    }

    /// オンボーディング(S1)
    enum Onboarding {
        static let start: LocalizedStringKey = "onboarding.start"
        static let welcomeEyebrow: LocalizedStringKey = "onboarding.page.welcome.eyebrow"
        static let welcomeTitle: LocalizedStringKey = "onboarding.page.welcome.title"
        static let welcomeBody: LocalizedStringKey = "onboarding.page.welcome.body"
        static let avatarEyebrow: LocalizedStringKey = "onboarding.page.avatar.eyebrow"
        static let avatarTitle: LocalizedStringKey = "onboarding.page.avatar.title"
        static let avatarBody: LocalizedStringKey = "onboarding.page.avatar.body"
        static let syncEyebrow: LocalizedStringKey = "onboarding.page.sync.eyebrow"
        static let syncTitle: LocalizedStringKey = "onboarding.page.sync.title"
        static let syncBody: LocalizedStringKey = "onboarding.page.sync.body"
    }

    /// 安全上の注意 — 文言はドラフト(チーム・法務の確認待ち)
    enum Safety {
        static let eyebrow: LocalizedStringKey = "safety.eyebrow"
        static let title: LocalizedStringKey = "safety.title"
        static let surroundingsTitle: LocalizedStringKey = "safety.surroundings.title"
        static let surroundingsBody: LocalizedStringKey = "safety.surroundings.body"
        static let placeTitle: LocalizedStringKey = "safety.place.title"
        static let placeBody: LocalizedStringKey = "safety.place.body"
        static let virtualTitle: LocalizedStringKey = "safety.virtual.title"
        static let virtualBody: LocalizedStringKey = "safety.virtual.body"
        static let healthTitle: LocalizedStringKey = "safety.health.title"
        static let healthBody: LocalizedStringKey = "safety.health.body"
        static let disclaimerTitle: LocalizedStringKey = "safety.disclaimer.title"
        static let disclaimerBody: LocalizedStringKey = "safety.disclaimer.body"
        static let agree: LocalizedStringKey = "safety.agree"
    }

    /// デバイス接続(S4)
    enum Devices {
        static let setupEyebrow: LocalizedStringKey = "devices.setup.eyebrow"
        static let title: LocalizedStringKey = "devices.title"
        static let subtitle: LocalizedStringKey = "devices.subtitle"
        static let arGlasses: LocalizedStringKey = "devices.arGlasses"
        static let outOfScope: LocalizedStringKey = "devices.outOfScope"
        static let hintConnected: LocalizedStringKey = "devices.hint.connected"
        static let hintMirroring: LocalizedStringKey = "devices.hint.mirroring"
        static let hintManual: LocalizedStringKey = "devices.hint.manual"
        static let hintNotDetected: LocalizedStringKey = "devices.hint.notDetected"
        static let connected: LocalizedStringKey = "devices.connected"
        static let mirroring: LocalizedStringKey = "devices.mirroring"
        static let manual: LocalizedStringKey = "devices.manual"
        static let connectManually: LocalizedStringKey = "devices.connectManually"
    }

    /// ランニング設定(S5)
    enum Settings {
        static let title: LocalizedStringKey = "settings.title"
        static let subtitle: LocalizedStringKey = "settings.subtitle"
        static let duration: LocalizedStringKey = "settings.duration"
        static let distance: LocalizedStringKey = "settings.distance"
        static let pace: LocalizedStringKey = "settings.pace"
        static let wheel: LocalizedStringKey = "settings.wheel"
        static let wheelLabel: LocalizedStringKey = "settings.wheelLabel"
    }

    /// ルート選択(S8)
    enum Route {
        static let eyebrow: LocalizedStringKey = "route.eyebrow"
        static let title: LocalizedStringKey = "route.title"
        static let subtitle: LocalizedStringKey = "route.subtitle"
        static let pastRoutes: LocalizedStringKey = "route.pastRoutes"
        static let noRoutedRecords: LocalizedStringKey = "route.noRoutedRecords"
        static let start: LocalizedStringKey = "route.start"
    }

    /// 走行中(S6)・ロック画面
    enum Running {
        static let preparingTitle: LocalizedStringKey = "running.preparing.title"
        static let preparingNote: LocalizedStringKey = "running.preparing.note"
        static let active: LocalizedStringKey = "running.active"
        static let ghost: LocalizedStringKey = "running.ghost"
        static let distance: LocalizedStringKey = "running.distance"
        static let estimated: LocalizedStringKey = "running.estimated"
        static let pace: LocalizedStringKey = "running.pace"
        static let syncRate: LocalizedStringKey = "running.syncRate"
        static let onGlasses: LocalizedStringKey = "running.onGlasses"
        static let lockScreen: LocalizedStringKey = "running.lockScreen"
        static let goalReached: LocalizedStringKey = "running.goalReached"
        static let lowBattery: LocalizedStringKey = "running.lowBattery"
        static let gpsLost: LocalizedStringKey = "running.gpsLost"
        static func avatarHidden(_ reason: String) -> LocalizedStringKey { "running.avatarHidden \(reason)" }
        static let lockTitle: LocalizedStringKey = "running.lock.title"
    }

    /// ラン結果(S7)
    enum Results {
        static let activityEyebrow: LocalizedStringKey = "history.activity.eyebrow"
        static let niceRun: LocalizedStringKey = "history.niceRun"
        static let syncRate: LocalizedStringKey = "history.syncRate"
        static func gradeBadge(_ grade: String, _ rank: String) -> LocalizedStringKey { "results.gradeBadge \(grade) \(rank)" }
        static let waiting: LocalizedStringKey = "results.waiting"
        static let none: LocalizedStringKey = "results.none"
        static let unavailable: LocalizedStringKey = "results.unavailable"
        static let saveFailedTitle: LocalizedStringKey = "results.saveFailed.title"
        static let saveFailedBody: LocalizedStringKey = "results.saveFailed.body"
        static let distance: LocalizedStringKey = "history.distance"
        static let duration: LocalizedStringKey = "history.duration"
        static let averagePace: LocalizedStringKey = "history.averagePace"
        static let calories: LocalizedStringKey = "history.calories"
        static let viewHistory: LocalizedStringKey = "history.viewHistory"
    }

    /// ランニング履歴(S10)
    enum History {
        static let eyebrow: LocalizedStringKey = "history.eyebrow"
        static let title: LocalizedStringKey = "history.title"
        static let notLoadedTitle: LocalizedStringKey = "history.notLoaded.title"
        static let emptyBody: LocalizedStringKey = "history.empty.body"
        static let notLoadedBody: LocalizedStringKey = "history.notLoaded.body"
        static let ghostRace: LocalizedStringKey = "history.ghostRace"
    }

    /// アバター
    enum Avatar {
        static let title: LocalizedStringKey = "avatar.title"
        static let intro: LocalizedStringKey = "avatar.intro"
        static let vrchatNote: LocalizedStringKey = "avatar.vrchatNote"
        static let importVrm: LocalizedStringKey = "avatar.import"
        static let importFailedTitle: LocalizedStringKey = "avatar.importFailed.title"
        static func applied(_ name: String) -> LocalizedStringKey { "avatar.applied \(name)" }
        static func unusable(_ name: String) -> LocalizedStringKey { "avatar.unusable \(name)" }
        static let available: LocalizedStringKey = "avatar.available"
        static let none: LocalizedStringKey = "avatar.none"
        static let inUse: LocalizedStringKey = "avatar.inUse"
        static var thisModel: String { String(localized: "avatar.thisModel") }
        static var defaultModel: String { String(localized: "avatar.default") }
        static func fileImportFailed(_ detail: String) -> String { String(localized: "avatar.fileImportFailed \(detail)") }
    }

    /// AR設定(S9-3)
    enum ARSettings {
        static let title: LocalizedStringKey = "arSettings.title"
        static let subtitle: LocalizedStringKey = "arSettings.subtitle"
        static let glasses: LocalizedStringKey = "arSettings.glasses"
        static let notConnected: LocalizedStringKey = "arSettings.notConnected"
        static let brightnessNote: LocalizedStringKey = "arSettings.brightnessNote"
        static let avatar: LocalizedStringKey = "arSettings.avatar"
        static let chooseAvatar: LocalizedStringKey = "arSettings.chooseAvatar"
    }

    /// スマホ設定
    enum PhoneSettings {
        static let title: LocalizedStringKey = "phoneSettings.title"
        static let subtitle: LocalizedStringKey = "phoneSettings.subtitle"
        static let language: LocalizedStringKey = "phoneSettings.language"
        static let languageNote: LocalizedStringKey = "phoneSettings.languageNote"
        static let location: LocalizedStringKey = "phoneSettings.location"
        static let locationNote: LocalizedStringKey = "phoneSettings.locationNote"
        static let locationAlways: LocalizedStringKey = "phoneSettings.location.always"
        static let locationWhenInUse: LocalizedStringKey = "phoneSettings.location.whenInUse"
        static let locationNotDetermined: LocalizedStringKey = "phoneSettings.location.notDetermined"
        static let locationDenied: LocalizedStringKey = "phoneSettings.location.denied"
        static let locationRestricted: LocalizedStringKey = "phoneSettings.location.restricted"
        static let locationServicesOff: LocalizedStringKey = "phoneSettings.location.servicesOff"
    }

    /// 数値の書式。値(Localizable.xcstrings)が書式指定子を持つ
    enum Formats {
        static func routeDistance(_ km: Double) -> String {
            String.localizedStringWithFormat(String(localized: "format.route.distance"), km)
        }
        static func paceWheel(_ kmh: Double, _ perKm: String) -> String {
            String.localizedStringWithFormat(String(localized: "format.settings.paceWheel"), kmh, perKm)
        }
    }
}

extension Locale {
    /// 画面の表示言語(iPhone の設定アプリの「言語」に従う)。日付の書式をこれに合わせる
    static var appLanguage: Locale {
        Locale(identifier: Bundle.main.preferredLocalizations.first ?? "ja")
    }
}
