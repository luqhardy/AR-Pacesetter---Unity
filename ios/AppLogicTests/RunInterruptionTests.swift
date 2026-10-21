import XCTest
@testable import ARRunnerAppLogic

/// 記録中の強制終了(タスク表 S3 異常系「前回の記録に『-』と表示」)
final class RunInterruptionTests: XCTestCase {

    private var store: UserDefaults!
    private var suiteName = ""

    override func setUp() {
        super.setUp()
        suiteName = "RunInterruptionTests-\(UUID().uuidString)"
        store = UserDefaults(suiteName: suiteName)
    }

    override func tearDown() {
        store.removePersistentDomain(forName: suiteName)
        store = nil
        super.tearDown()
    }

    func testNothingIsInterruptedOnFirstLaunch() {
        XCTAssertFalse(RunInterruption.wasInterrupted(in: store, sessionActive: false))
    }

    func testARunThatNeverEndedIsInterrupted() {
        RunInterruption.markStarted(in: store)
        // アプリが落ちて、次の起動
        XCTAssertTrue(RunInterruption.wasInterrupted(in: store, sessionActive: false))
    }

    func testANormallyEndedRunIsNotInterrupted() {
        RunInterruption.markStarted(in: store)
        RunInterruption.markEnded(in: store)
        XCTAssertFalse(RunInterruption.wasInterrupted(in: store, sessionActive: false))
    }

    func testTheRunInProgressIsNotInterrupted() {
        // グラス切断(§8.3)で走行中にホームへ戻った等
        RunInterruption.markStarted(in: store)
        XCTAssertFalse(RunInterruption.wasInterrupted(in: store, sessionActive: true))
    }
}
