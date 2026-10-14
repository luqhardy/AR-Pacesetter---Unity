import XCTest
@testable import ARRunnerAppLogic

/// 体験モードのスタッフ画面の進行(BoothDemoFlow)。
/// 実際に踏んだ/踏みうる不具合を縛る: 前回の終了の再送・中断直後の開始の競合・猶予中の不要な Stop
final class BoothDemoFlowTests: XCTestCase {

    private let t0 = Date(timeIntervalSince1970: 1_000_000)

    func testCountdownStartsOnceAtTheDeadline() {
        var flow = BoothDemoFlow()
        XCTAssertNil(flow.begin(delaySeconds: 10, now: t0))
        XCTAssertEqual(flow.phase, .countingDown(remaining: 10))

        XCTAssertNil(flow.tick(now: t0.addingTimeInterval(3.2)))
        XCTAssertEqual(flow.phase, .countingDown(remaining: 7), "残り秒は時刻から出す(切り上げ)")

        XCTAssertEqual(flow.tick(now: t0.addingTimeInterval(10)), .sendStart)
        XCTAssertEqual(flow.phase, .starting)
        XCTAssertNil(flow.tick(now: t0.addingTimeInterval(11)), "開始は1回だけ送る")
    }

    func testZeroDelayStartsImmediately() {
        var flow = BoothDemoFlow()
        XCTAssertEqual(flow.begin(delaySeconds: 0, now: t0), .sendStart)
        XCTAssertEqual(flow.phase, .starting)
    }

    func testProgressMovesStartingToRunning_ButNotIdle() {
        var flow = BoothDemoFlow()
        flow.progressReceived()
        XCTAssertEqual(flow.phase, .idle, "送っていない体験の進行では動かない")

        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.progressReceived()
        XCTAssertEqual(flow.phase, .running)
    }

    func testStaleEndOnOpeningTheScreenIsIgnored() {
        // 画面を開いた瞬間、前回の終了が購読開始時に再送される
        var flow = BoothDemoFlow()
        flow.endReceived(.init(completed: true, reason: ""))
        XCTAssertEqual(flow.phase, .idle)
        XCTAssertNil(flow.resultMessage)
    }

    func testStopAckFromThePreviousRunDoesNotEndTheNewOne() {
        // 中断 → すぐ開始。前の体験の中断応答が新しい開始の後に届く
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 0, now: t0)
        XCTAssertEqual(flow.stop(), .sendStop)
        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.endReceived(.init(completed: false, reason: "stopped"))
        XCTAssertEqual(flow.phase, .starting, "新しい体験は続いている")
    }

    func testCompletedRunShowsTheResult() {
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.progressReceived()
        flow.endReceived(.init(completed: true, reason: ""))
        XCTAssertEqual(flow.phase, .finished)
        XCTAssertEqual(flow.resultMessage, "完走しました。グラスを外して、次の方へ。")
        XCTAssertFalse(flow.isActive)
    }

    func testStartFailureShowsTheReason() {
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.endReceived(.init(completed: false, reason: "走行中は開始できません"))
        XCTAssertEqual(flow.phase, .finished)
        XCTAssertEqual(flow.resultMessage, "開始できませんでした: 走行中は開始できません")
    }

    func testCancellingTheCountdownSendsNothingToUnity() {
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 10, now: t0)
        XCTAssertTrue(flow.isActive)
        XCTAssertNil(flow.stop(), "猶予中はまだ Unity へ何も送っていない")
        XCTAssertEqual(flow.phase, .idle)
        XCTAssertNil(flow.tick(now: t0.addingTimeInterval(20)), "取り消した猶予は開始しない")
    }

    func testStoppingARunningDemoSendsStop() {
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.progressReceived()
        XCTAssertEqual(flow.stop(), .sendStop)
        XCTAssertEqual(flow.resultMessage, "中断しました。")
    }

    func testNewStartClearsThePreviousResult() {
        var flow = BoothDemoFlow()
        _ = flow.begin(delaySeconds: 0, now: t0)
        flow.endReceived(.init(completed: true, reason: ""))
        XCTAssertNotNil(flow.resultMessage)
        _ = flow.begin(delaySeconds: 5, now: t0)
        XCTAssertNil(flow.resultMessage)
    }
}
