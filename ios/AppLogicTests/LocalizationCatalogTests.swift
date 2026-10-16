import XCTest

/// 文言のキー(Core/Localization/AppStrings.swift)と日本語・英語の値(Resources/Localizable.xcstrings)の対応。
///
/// SwiftUI はカタログに無いキーをそのまま画面に出す(「common.back」と表示される)ため、
/// キーを足してカタログに足し忘れても、ビルドは通ってしまう。ここで CI(macOS)が気づく。
/// AppStrings は SwiftUI に依存するのでパッケージには入れず、ソースとカタログをファイルとして読む。
final class LocalizationCatalogTests: XCTestCase {

    private static let appRoot = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()        // AppLogicTests
        .deletingLastPathComponent()        // ios
        .appendingPathComponent("AR_Runner_UI/AR_Runner_UI")

    /// AppStrings.swift が使うキー。補間 `\(x)` はカタログのキーの形 `%@` にする
    private func keysUsedInCode() throws -> Set<String> {
        let source = try String(contentsOf: Self.appRoot.appendingPathComponent("Core/Localization/AppStrings.swift"),
                                encoding: .utf8)
        let patterns = [
            #"LocalizedStringKey = "([^"]+)""#,
            #"-> LocalizedStringKey \{ "([^"]+)" \}"#,
            #"String\(localized: "([^"]+)"\)"#,
            #"resolve\("([^"]+)"\)"#,       // String が要る文言(選んだ表示言語で引く)
        ]
        var keys = Set<String>()
        for pattern in patterns {
            let regex = try NSRegularExpression(pattern: pattern)
            let range = NSRange(source.startIndex..., in: source)
            for match in regex.matches(in: source, range: range) {
                guard let r = Range(match.range(at: 1), in: source) else { continue }
                let key = String(source[r]).replacingOccurrences(of: #"\\\([^)]*\)"#, with: "%@",
                                                                   options: .regularExpression)
                keys.insert(key)
            }
        }
        return keys
    }

    private func catalog() throws -> [String: [String: Any]] {
        let data = try Data(contentsOf: Self.appRoot.appendingPathComponent("Resources/Localizable.xcstrings"))
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(root["sourceLanguage"] as? String, "ja", "日本語が元の言語")
        return try XCTUnwrap(root["strings"] as? [String: [String: Any]])
    }

    private func value(_ entry: [String: Any], _ language: String) -> String? {
        let localizations = entry["localizations"] as? [String: Any]
        let unit = (localizations?[language] as? [String: Any])?["stringUnit"] as? [String: Any]
        return unit?["value"] as? String
    }

    /// 書式指定子の数(%@ / %1$@ / %.2f / %lld など。%% は数えない)
    private func placeholderCount(_ text: String) -> Int {
        let regex = try! NSRegularExpression(pattern: #"%(\d+\$)?[-+ 0#]*\d*(\.\d+)?(lld|ld|@|f|d)"#)
        return regex.numberOfMatches(in: text, range: NSRange(text.startIndex..., in: text))
    }

    func testCodeUsesKeys() throws {
        XCTAssertGreaterThan(try keysUsedInCode().count, 100, "AppStrings.swift のキーを読めている")
    }

    func testEveryKeyHasJapaneseAndEnglish() throws {
        let strings = try catalog()
        for key in try keysUsedInCode().sorted() {
            guard let entry = strings[key] else {
                XCTFail("Localizable.xcstrings に \(key) が無い(画面にキーがそのまま出る)")
                continue
            }
            XCTAssertFalse((value(entry, "ja") ?? "").isEmpty, "\(key) の日本語が無い")
            XCTAssertFalse((value(entry, "en") ?? "").isEmpty, "\(key) の英語が無い")
        }
    }

    func testPlaceholdersMatchBetweenLanguages() throws {
        let strings = try catalog()
        for key in try keysUsedInCode().sorted() {
            guard let entry = strings[key], let ja = value(entry, "ja"), let en = value(entry, "en") else { continue }
            XCTAssertEqual(placeholderCount(ja), placeholderCount(en), "\(key): 日本語と英語で差し込む値の数が違う")
            let inKey = placeholderCount(key)
            if inKey > 0 {
                XCTAssertEqual(placeholderCount(ja), inKey, "\(key): キーと値で差し込む値の数が違う")
            }
        }
    }

    func testNoManualEntryIsLeftUnused() throws {
        // 手で管理しているエントリ(Xcode が自動で拾ったものは除く)はコードから使われていること
        let used = try keysUsedInCode()
        for (key, entry) in try catalog() where (entry["extractionState"] as? String) == "manual" {
            XCTAssertTrue(used.contains(key), "\(key) はカタログにあるがコードから使われていない")
        }
    }
}
