import XCTest
@testable import ARRunnerAppLogic

/// ラン結果(S7)の数字の書式 — 極端な値でもカードからはみ出さない(タスク表 S7 異常系)
final class RunResultFormatTests: XCTestCase {

    func testOrdinaryRun() {
        XCTAssertEqual(RunResultFormat.distanceKm(5.0), "5.00")
        XCTAssertEqual(RunResultFormat.duration(1610), "26:50")
        XCTAssertEqual(RunResultFormat.pace(distanceKm: 5.0, elapsedSeconds: 1605), "5'21\"")
    }

    func testMissingValuesAreDashes() {
        XCTAssertEqual(RunResultFormat.distanceKm(nil), "-")
        XCTAssertEqual(RunResultFormat.duration(nil), "-")
        XCTAssertEqual(RunResultFormat.pace(distanceKm: nil, elapsedSeconds: 100), "-")
        XCTAssertEqual(RunResultFormat.distanceKm(.nan), "-")
        XCTAssertEqual(RunResultFormat.duration(-5), "-")
        XCTAssertEqual(RunResultFormat.calories(0), "-")
        XCTAssertEqual(RunResultFormat.fatigue(nil), "-", "古い Unity は疲労度を送らない")
    }

    func testVeryLongRunsShowHours() {
        // 以前は "6000:00" になっていた
        XCTAssertEqual(RunResultFormat.duration(100 * 3600), "100:00:00")
        XCTAssertEqual(RunResultFormat.duration(3600 + 61), "1:01:01")
    }

    func testTinyDistanceHasNoPace() {
        XCTAssertEqual(RunResultFormat.distanceKm(0.004), "0.00")
        XCTAssertEqual(RunResultFormat.pace(distanceKm: 0.004, elapsedSeconds: 600), "-")
    }

    func testAbsurdlySlowPaceIsCapped() {
        // 0.01km を100時間 — 以前は "600000'00"" になっていた
        XCTAssertEqual(RunResultFormat.pace(distanceKm: 0.01, elapsedSeconds: 100 * 3600), "99'59\"")
    }

    func testGpsJumpDistanceStaysShort() {
        XCTAssertEqual(RunResultFormat.distanceKm(123.456), "123.5")
        XCTAssertEqual(RunResultFormat.distanceKm(12_345.6), "12346")
    }

    func testCaloriesAndFatigue() {
        XCTAssertEqual(RunResultFormat.calories(312.6), "313")
        XCTAssertEqual(RunResultFormat.fatigue(42.24), "42.2")
    }
}
