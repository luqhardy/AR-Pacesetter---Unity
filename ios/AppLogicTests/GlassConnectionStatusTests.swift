import XCTest
@testable import ARRunnerAppLogic

/// 準備画面のARグラス行と、開発者モードの屋内テストスイッチの読み取り
final class GlassConnectionStatusTests: XCTestCase {

    func testOnlyARealConnectionCountsAsConnected() {
        // 以前はタップから1.5秒後に、何も挿さっていなくても「接続済み」になった
        XCTAssertEqual(GlassRowStatus.resolve(glassesConnected: false, mirroring: false, manualConnect: true), .manual)
        XCTAssertEqual(GlassRowStatus.resolve(glassesConnected: true, mirroring: false, manualConnect: false), .connected)
        XCTAssertEqual(GlassRowStatus.resolve(glassesConnected: false, mirroring: false, manualConnect: false), .notDetected)
    }

    func testMirroringIsNotHiddenByAManualConnect() {
        XCTAssertEqual(GlassRowStatus.resolve(glassesConnected: false, mirroring: true, manualConnect: true), .mirroring)
    }

    func testRealConnectionWinsOverEverything() {
        XCTAssertEqual(GlassRowStatus.resolve(glassesConnected: true, mirroring: true, manualConnect: true), .connected)
    }

    func testManualConnectIsOfferedOnlyWhenNothingIsConnected() {
        XCTAssertTrue(GlassRowStatus.notDetected.canConnectManually)
        XCTAssertTrue(GlassRowStatus.mirroring.canConnectManually)
        XCTAssertFalse(GlassRowStatus.connected.canConnectManually)
        XCTAssertFalse(GlassRowStatus.manual.canConnectManually)
    }

    func testGpsLostHandlingReadsUnitysDiagnosticsValue() {
        // Unity の DevDiagnostics が出す値
        XCTAssertEqual(GpsLostHandlingReading.parse("ON(仕様どおり)"), true)
        XCTAssertEqual(GpsLostHandlingReading.parse("OFF(手動で無効化中)"), false)
        XCTAssertNil(GpsLostHandlingReading.parse(nil), "Unity 未応答")
        XCTAssertNil(GpsLostHandlingReading.parse("-"))
    }
    // MARK: 準備画面(S4)の「次へ」とエラー(タスク表 S4 異常系)

    func testGlassesAreRequiredToProceed() {
        XCTAssertTrue(DeviceGate.canProceed(glass: .connected, allowRunWithoutGlasses: false))
        XCTAssertTrue(DeviceGate.canProceed(glass: .manual, allowRunWithoutGlasses: false), "手動の接続は利用者が選んだ予備手段")
        XCTAssertFalse(DeviceGate.canProceed(glass: .notDetected, allowRunWithoutGlasses: false))
        XCTAssertFalse(DeviceGate.canProceed(glass: .mirroring, allowRunWithoutGlasses: false), "不完全な接続では進めない")
    }

    func testDeveloperSettingLetsYouRunWithoutGlasses() {
        XCTAssertTrue(DeviceGate.canProceed(glass: .notDetected, allowRunWithoutGlasses: true))
        XCTAssertTrue(DeviceGate.canProceed(glass: .mirroring, allowRunWithoutGlasses: true))
    }

    func testNoProblemOnceConnected() {
        XCTAssertNil(GlassConnectionProblem.resolve(status: .connected, droppedHere: true, returnedFromRun: true))
        XCTAssertNil(GlassConnectionProblem.resolve(status: .manual, droppedHere: false, returnedFromRun: false))
    }

    func testProblemsAreToldApart() {
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .notDetected, droppedHere: false, returnedFromRun: false), .notDetected)
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: false, returnedFromRun: false), .incomplete)
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .notDetected, droppedHere: true, returnedFromRun: false), .droppedAfterConnect)
        // 走行中に切れて戻ってきたことを最優先で伝える(記録は続いている)
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: true, returnedFromRun: true), .lostDuringRun)
    }
    func testFreshMirroringIsConnectingNotAnError() {
        // 挿した直後は iOS がグラス用の画面を作るまでミラーリングに見える(タスク表 S4「ARタイムアウト」)
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: false, returnedFromRun: false,
                                                      mirroringSeconds: 2), .connecting)
        // 走行中に切れて挿し直したときも、まずは待つ
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: false, returnedFromRun: true,
                                                      mirroringSeconds: 0), .connecting)
    }

    func testMirroringThatLastsIsAnIncompleteConnection() {
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: false, returnedFromRun: false,
                                                      mirroringSeconds: GlassConnectionProblem.connectTimeoutSeconds),
                       .incomplete)
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .mirroring, droppedHere: false, returnedFromRun: false,
                                                      mirroringSeconds: nil), .incomplete, "経過が分からなければ待たない")
    }

    func testOnlyMirroringWaits() {
        XCTAssertEqual(GlassConnectionProblem.resolve(status: .notDetected, droppedHere: false, returnedFromRun: false,
                                                      mirroringSeconds: 1), .notDetected)
        XCTAssertNil(GlassConnectionProblem.resolve(status: .connected, droppedHere: false, returnedFromRun: false,
                                                    mirroringSeconds: 1))
    }
}
