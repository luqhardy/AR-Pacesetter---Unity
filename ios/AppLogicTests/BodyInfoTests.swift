import XCTest
@testable import ARRunnerAppLogic

/// 身体情報(S9-2)の入力の補正と保存
final class BodyInfoTests: XCTestCase {

    func testFullWidthDigitsBecomeHalfWidth() {
        let result = BodyInfoInput.correct("１７０", field: .height)
        XCTAssertEqual(result.text, "170")
        XCTAssertNil(result.error)
    }

    func testNonDigitsAreRemovedWithAnError() {
        let result = BodyInfoInput.correct("17a0", field: .height)
        XCTAssertEqual(result.text, "170")
        XCTAssertEqual(result.error, .invalidCharacter)
    }

    func testLeadingZerosAreRemoved() {
        XCTAssertEqual(BodyInfoInput.correct("065", field: .weight).text, "65")
    }

    func testDigitsOverTheMaximumAreNotEntered() {
        let result = BodyInfoInput.correct("999", field: .height)
        XCTAssertEqual(result.text, "99")
        XCTAssertEqual(result.error, .outOfRange)
        XCTAssertEqual(BodyInfoInput.correct("1700", field: .height).text, "170")
    }

    func testValuesBelowTheMinimumWaitUntilTheFieldIsLeft() {
        // 「1」は 170 の途中なので、打っている間は止めない
        XCTAssertNil(BodyInfoInput.correct("1", field: .height).error)
        XCTAssertEqual(BodyInfoInput.validate("1", field: .height), .outOfRange)
        XCTAssertNil(BodyInfoInput.validate("50", field: .height))
        XCTAssertEqual(BodyInfoInput.validate("9", field: .weight), .outOfRange)
    }

    func testBlankIsAllowed() {
        XCTAssertNil(BodyInfoInput.validate("", field: .height))
        XCTAssertNil(BodyInfoInput.correct("", field: .weight).error)
    }

    func testPickerStartsFromAValidEntryOrTheDefault() {
        XCTAssertEqual(BodyInfoInput.pickerStartValue("182", field: .height), 182)
        XCTAssertEqual(BodyInfoInput.pickerStartValue("", field: .height), 170)
        XCTAssertEqual(BodyInfoInput.pickerStartValue("5", field: .weight), 60)
    }

    func testStoreRoundTripsAndRemembersTheInitialSetup() throws {
        let suite = "BodyInfoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = BodyInfoStore(defaults: defaults)

        XCTAssertEqual(store.load(), .empty)
        XCTAssertFalse(store.hasCompletedInitialSetup)

        let info = BodyInfo(heightCm: 168, weightKg: nil, gender: .female)
        try store.save(info)
        store.hasCompletedInitialSetup = true

        XCTAssertEqual(store.load(), info)
        XCTAssertTrue(BodyInfoStore(defaults: defaults).hasCompletedInitialSetup)
    }
}
