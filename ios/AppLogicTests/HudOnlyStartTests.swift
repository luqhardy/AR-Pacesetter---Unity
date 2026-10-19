import XCTest
@testable import ARRunnerAppLogic

/// GPSが使えないときはHUDだけで始める(タスク表 ARグラス)— 既定はGPSが無くてもアバターを出す
final class HudOnlyStartTests: XCTestCase {

    private var store: UserDefaults!
    private var suiteName = ""

    override func setUp() {
        super.setUp()
        suiteName = "HudOnlyStartTests-\(UUID().uuidString)"
        store = UserDefaults(suiteName: suiteName)
    }

    override func tearDown() {
        store.removePersistentDomain(forName: suiteName)
        store = nil
        super.tearDown()
    }

    func testOffByDefault() {
        XCTAssertFalse(HudOnlyStart.isEnabled(in: store), "既定は従来どおり、GPSが無くてもアバターを出す")
    }

    func testTheSwitchIsRemembered() {
        store.set(true, forKey: HudOnlyStart.storageKey)
        XCTAssertTrue(HudOnlyStart.isEnabled(in: store))
        store.set(false, forKey: HudOnlyStart.storageKey)
        XCTAssertFalse(HudOnlyStart.isEnabled(in: store))
    }

    func testAwaitingFixIsReadFromTheGpsLostEvent() {
        XCTAssertTrue(HudOnlyStart.isAwaitingFirstFix(gpsLostEvent: ["event": "GPSLost", "awaitingFix": true]))
        XCTAssertFalse(HudOnlyStart.isAwaitingFirstFix(gpsLostEvent: ["event": "GPSLost"]),
                       "付いていなければ通常のGPSロスト(アバターは慣性で走っている)")
        XCTAssertFalse(HudOnlyStart.isAwaitingFirstFix(gpsLostEvent: ["event": "GPSLost", "awaitingFix": false]))
    }

    func testAwaitingFixSurvivesJsonDecoding() throws {
        // Unity からは JSON で届く(JSONSerialization の真偽値は NSNumber)
        let data = Data(#"{"event":"GPSLost","awaitingFix":true}"#.utf8)
        let event = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertTrue(HudOnlyStart.isAwaitingFirstFix(gpsLostEvent: event))
    }
}
