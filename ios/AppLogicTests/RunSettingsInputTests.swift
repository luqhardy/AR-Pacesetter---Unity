import XCTest
@testable import ARRunnerAppLogic

/// ランニング設定(S5)の入力: 全角でも読めること・＋/− が境界で止まること
final class RunSettingsInputTests: XCTestCase {

    // MARK: 全角・半角

    func testFullWidthDigitsReadTheSameAsHalfWidth() {
        XCTAssertEqual(NumericInput.parseDecimal("１０"), 10)
        XCTAssertEqual(NumericInput.parseDecimal("５．５"), 5.5)
        XCTAssertEqual(NumericInput.parseDecimal("5.5"), 5.5)
        XCTAssertEqual(NumericInput.parseDecimal("１２。５"), 12.5, "日本語入力の句点も小数点として読む")
    }

    func testSurroundingSpacesIncludingFullWidthAreIgnored() {
        XCTAssertEqual(NumericInput.parseDecimal("　8.0 "), 8.0)
    }

    func testUnreadableOrNonPositiveDecimalsAreRejected() {
        XCTAssertNil(NumericInput.parseDecimal(""))
        XCTAssertNil(NumericInput.parseDecimal("abc"))
        XCTAssertNil(NumericInput.parseDecimal("1.2.3"))
        XCTAssertNil(NumericInput.parseDecimal("0"))
        XCTAssertNil(NumericInput.parseDecimal("-3"))
    }

    func testDurationAcceptsHmsMsAndMinutesInEitherWidth() {
        XCTAssertEqual(NumericInput.parseDuration("01:30:00"), 5400)
        XCTAssertEqual(NumericInput.parseDuration("０１：３０：００"), 5400)
        XCTAssertEqual(NumericInput.parseDuration("25:30"), 1530)
        XCTAssertEqual(NumericInput.parseDuration("45"), 2700)
    }

    func testUnreadableDurationsAreRejected() {
        XCTAssertNil(NumericInput.parseDuration(""))
        XCTAssertNil(NumericInput.parseDuration("1:2:3:4"))
        XCTAssertNil(NumericInput.parseDuration("1::00"))
        XCTAssertNil(NumericInput.parseDuration("1:aa"))
        XCTAssertNil(NumericInput.parseDuration("1.5"))
    }

    // MARK: ＋/− と範囲

    func testButtonsStopAtTheLimits() {
        let d = RunSettingRanges.distanceKm
        XCTAssertFalse(d.canDecrement(0.5), "下限では − を押せない")
        XCTAssertTrue(d.canIncrement(0.5))
        XCTAssertFalse(d.canIncrement(200), "上限では ＋ を押せない")
        XCTAssertTrue(d.canDecrement(200))

        let t = RunSettingRanges.timeSeconds
        XCTAssertFalse(t.canDecrement(60))
        XCTAssertFalse(t.canIncrement(86_400))
    }

    func testStepsNeverLeaveTheRange() {
        let p = RunSettingRanges.paceKmH
        XCTAssertEqual(p.decremented(1.2), 1.0, "下限より下へは行かない")
        XCTAssertEqual(p.incremented(29.8), 30.0)
        XCTAssertEqual(RunSettingRanges.distanceKm.decremented(0.7), 0.5)
    }

    func testRepeatedStepsDoNotAccumulateFloatingPointError() {
        let d = RunSettingRanges.distanceKm
        var value = 0.5
        for _ in 0..<19 { value = d.incremented(value) }
        XCTAssertEqual(value, 10.0)   // 0.5 + 19×0.5 ちょうど(9.999999… にならない)
    }

    func testOffGridValueFromDirectInputStillStepsAndLimits() {
        // 直接入力で 199.8km → ＋ は 200 で止まり、押せるのはそこまで
        let d = RunSettingRanges.distanceKm
        XCTAssertTrue(d.canIncrement(199.8))
        XCTAssertEqual(d.incremented(199.8), 200)
        XCTAssertFalse(d.canIncrement(d.incremented(199.8)))
    }

    func testClampKeepsDirectInputInRange() {
        XCTAssertEqual(RunSettingRanges.paceKmH.clamped(99), 30)
        XCTAssertEqual(RunSettingRanges.timeSeconds.clamped(10), 60)
    }
}
