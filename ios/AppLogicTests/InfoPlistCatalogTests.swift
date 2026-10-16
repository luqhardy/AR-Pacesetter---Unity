import XCTest

/// 許可ダイアログの文言(project.pbxproj の INFOPLIST_KEY_*UsageDescription)と、その英語(Resources/InfoPlist.xcstrings)。
///
/// 許可ダイアログは iOS が iPhone の言語で出す(アプリ内の言語設定には従わない)。英語が無いと英語の iPhone でも
/// 日本語のダイアログになる。日本語はビルド設定が元なので、変えたらカタログの日本語も揃えること — ここで CI が気づく。
final class InfoPlistCatalogTests: XCTestCase {

    private static let iosRoot = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()        // AppLogicTests
        .deletingLastPathComponent()        // ios

    /// ビルド設定の許可文言(キー → 日本語)。Debug / Release の両方にあり、同じであること
    private func usageDescriptions() throws -> [String: String] {
        let pbxproj = try String(contentsOf: Self.iosRoot.appendingPathComponent("AR_Runner_UI/AR_Runner_UI.xcodeproj/project.pbxproj"),
                                 encoding: .utf8)
        let regex = try NSRegularExpression(pattern: #"INFOPLIST_KEY_(NS\w+UsageDescription) = "([^"]*)";"#)
        var found: [String: String] = [:]
        for match in regex.matches(in: pbxproj, range: NSRange(pbxproj.startIndex..., in: pbxproj)) {
            guard let k = Range(match.range(at: 1), in: pbxproj), let v = Range(match.range(at: 2), in: pbxproj) else { continue }
            let key = String(pbxproj[k]), value = String(pbxproj[v])
            if let earlier = found[key] {
                XCTAssertEqual(earlier, value, "\(key) が Debug と Release で違う")
            }
            found[key] = value
        }
        return found
    }

    private func catalog() throws -> [String: [String: Any]] {
        let data = try Data(contentsOf: Self.iosRoot.appendingPathComponent("AR_Runner_UI/AR_Runner_UI/Resources/InfoPlist.xcstrings"))
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(root["sourceLanguage"] as? String, "ja")
        return try XCTUnwrap(root["strings"] as? [String: [String: Any]])
    }

    private func value(_ entry: [String: Any]?, _ language: String) -> String? {
        let localizations = entry?["localizations"] as? [String: Any]
        let unit = (localizations?[language] as? [String: Any])?["stringUnit"] as? [String: Any]
        return unit?["value"] as? String
    }

    func testEveryPermissionTextHasEnglish() throws {
        let descriptions = try usageDescriptions()
        XCTAssertGreaterThanOrEqual(descriptions.count, 5, "pbxproj の許可文言を読めている")
        let strings = try catalog()
        for key in descriptions.keys.sorted() {
            XCTAssertFalse((value(strings[key], "en") ?? "").isEmpty, "\(key) の英語が InfoPlist.xcstrings に無い")
        }
    }

    func testJapaneseMatchesTheBuildSetting() throws {
        let strings = try catalog()
        for (key, japanese) in try usageDescriptions() {
            XCTAssertEqual(value(strings[key], "ja"), japanese,
                           "\(key): InfoPlist.xcstrings の日本語がビルド設定と違う(片方だけ直した?)")
        }
    }
}
