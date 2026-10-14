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
}
