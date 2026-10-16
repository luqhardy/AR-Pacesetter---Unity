import XCTest
@testable import ARRunnerAppLogic

/// 表示言語(AppLanguage)。既定は日本語で、英語の iPhone でも日本語になること
final class AppLanguageTests: XCTestCase {

    func testDefaultIsJapaneseEvenOnAnEnglishIPhone() {
        let language = AppLanguage.stored(nil)
        XCTAssertEqual(language, .japanese)
        XCTAssertEqual(language.localizationIdentifier(systemLocalization: "en"), "ja")
    }

    func testUnknownStoredValueFallsBackToJapanese() {
        XCTAssertEqual(AppLanguage.stored("fr"), .japanese)
        XCTAssertEqual(AppLanguage.stored(""), .japanese)
    }

    func testStoredChoicesRoundTrip() {
        for language in AppLanguage.allCases {
            XCTAssertEqual(AppLanguage.stored(language.rawValue), language)
        }
    }

    func testExplicitChoiceIgnoresTheIPhoneLanguage() {
        XCTAssertEqual(AppLanguage.english.localizationIdentifier(systemLocalization: "ja"), "en")
        XCTAssertEqual(AppLanguage.japanese.localizationIdentifier(systemLocalization: "en"), "ja")
    }

    func testFollowIPhoneUsesWhatIOSPickedForThisApp() {
        XCTAssertEqual(AppLanguage.system.localizationIdentifier(systemLocalization: "en"), "en")
        XCTAssertEqual(AppLanguage.system.localizationIdentifier(systemLocalization: "en-GB"), "en")
        XCTAssertEqual(AppLanguage.system.localizationIdentifier(systemLocalization: "ja"), "ja")
    }

    func testFollowIPhoneFallsBackToJapaneseForOtherLanguages() {
        // アプリにあるのは日本語と英語だけ。Base や未知の値は元の言語(日本語)
        XCTAssertEqual(AppLanguage.system.localizationIdentifier(systemLocalization: "Base"), "ja")
        XCTAssertEqual(AppLanguage.system.localizationIdentifier(systemLocalization: nil), "ja")
    }
}
