import XCTest
@testable import ARRunnerAppLogic

/// AR設定の反映(タスク表 S9 異常系「送信失敗時のトーストと再送信」)
final class SettingDeliveryTests: XCTestCase {

    func testSendingWhenUnityCanAnswer() {
        XCTAssertEqual(SettingDeliveryState.onSend(unityLaunched: true, unityReady: true), .sending)
    }

    func testQueuedUntilTheNextRunWhenUnityHasNeverStarted() {
        // Unity は走行画面で初めて起動する。それまでは溜めて、次の走行の開始時に反映する
        XCTAssertEqual(SettingDeliveryState.onSend(unityLaunched: false, unityReady: false), .queued)
        XCTAssertEqual(SettingDeliveryState.onSend(unityLaunched: true, unityReady: false), .queued)
    }

    func testUnityIsWokenOnlyWhenItIsPausedAfterARun() {
        XCTAssertTrue(SettingDeliveryState.needsTemporaryResume(unityLaunched: true, unityReady: true, sessionActive: false))
        // 走行中は動いているので触らない
        XCTAssertFalse(SettingDeliveryState.needsTemporaryResume(unityLaunched: true, unityReady: true, sessionActive: true))
        // 一度も起動していなければ動かせない(溜めるだけ)
        XCTAssertFalse(SettingDeliveryState.needsTemporaryResume(unityLaunched: false, unityReady: false, sessionActive: false))
    }

    func testTheTimeoutIsShort() {
        // Unity は通常1フレームで答える。走者を長く待たせない
        XCTAssertLessThanOrEqual(SettingDeliveryState.responseTimeoutSeconds, 10)
    }
}
