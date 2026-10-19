import XCTest
@testable import ARRunnerAppLogic

/// アバター非表示の履歴(AvatarVisibilityLog)。走行後に「いつ・何秒・なぜ」を読めること
final class AvatarVisibilityLogTests: XCTestCase {

    private let t0 = Date(timeIntervalSince1970: 2_000_000)

    func testHiddenThenShownRecordsTheDuration() {
        var log = AvatarVisibilityLog()
        XCTAssertTrue(log.record(visible: false, reason: "GPSロスト: フェードアウト中", at: t0))
        XCTAssertNotNil(log.openEvent)
        XCTAssertTrue(log.record(visible: true, reason: "visible", at: t0.addingTimeInterval(7.5)))

        XCTAssertEqual(log.events.count, 1)
        XCTAssertEqual(log.events[0].reason, "GPSロスト: フェードアウト中")
        XCTAssertEqual(try XCTUnwrap(log.events[0].durationSeconds), 7.5, accuracy: 1e-9)
        XCTAssertNil(log.openEvent)
    }

    func testVisibleWithNothingOpenChangesNothing() {
        var log = AvatarVisibilityLog()
        XCTAssertFalse(log.record(visible: true, reason: "visible", at: t0))
        XCTAssertTrue(log.events.isEmpty)
    }

    func testRepeatedSameReasonIsOneEvent() {
        var log = AvatarVisibilityLog()
        log.record(visible: false, reason: "視野外", at: t0)
        XCTAssertFalse(log.record(visible: false, reason: "視野外", at: t0.addingTimeInterval(1)))
        XCTAssertEqual(log.events.count, 1)
    }

    func testReasonChangeWhileHiddenStartsANewEvent() {
        // GPSロスト: 慣性 → フェード → スタンバイ のように、見えないまま経路が変わる
        var log = AvatarVisibilityLog()
        log.record(visible: false, reason: "フェードアウト中", at: t0)
        log.record(visible: false, reason: "スタンバイ", at: t0.addingTimeInterval(1))

        XCTAssertEqual(log.events.count, 2)
        XCTAssertEqual(log.events[0].reason, "スタンバイ", "新しい順")
        XCTAssertNil(log.events[0].shownAt)
        XCTAssertEqual(try XCTUnwrap(log.events[1].durationSeconds), 1, accuracy: 1e-9, "前の区間は閉じる")
    }

    func testKeepsOnlyTheNewestEvents() {
        var log = AvatarVisibilityLog()
        for i in 0..<(AvatarVisibilityLog.maxEvents + 20) {
            let t = t0.addingTimeInterval(Double(i) * 10)
            log.record(visible: false, reason: "r\(i)", at: t)
            log.record(visible: true, reason: "visible", at: t.addingTimeInterval(1))
        }
        XCTAssertEqual(log.events.count, AvatarVisibilityLog.maxEvents)
        XCTAssertEqual(log.events.first?.reason, "r\(AvatarVisibilityLog.maxEvents + 19)")
    }

    func testSurvivesARoundTripThroughJSON() throws {
        // アプリを再起動しても残す(UserDefaults に JSON で保存する)
        var log = AvatarVisibilityLog()
        log.record(visible: false, reason: "背後にいる", at: t0)
        let data = try JSONEncoder().encode(log)
        XCTAssertEqual(try JSONDecoder().decode(AvatarVisibilityLog.self, from: data), log)
    }

    func testClear() {
        var log = AvatarVisibilityLog()
        log.record(visible: false, reason: "x", at: t0)
        log.clear()
        XCTAssertTrue(log.events.isEmpty)
    }
}
