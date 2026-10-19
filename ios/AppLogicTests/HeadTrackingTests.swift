import XCTest
@testable import ARRunnerAppLogic

/// AirPods による頭の向き(タスク表 ARグラス「首を振り向けた時に想定する場所を走る」)
final class HeadTrackingTests: XCTestCase {

    func testOffUntilRequested() {
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: false, supported: true, authorized: true,
                                                  lastSampleAge: 0.1, secondsSinceRequest: 0), .off)
    }

    func testUnsupportedAndDeniedAreReportedFirst() {
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: false, authorized: true,
                                                  lastSampleAge: nil, secondsSinceRequest: 0), .unsupported)
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: true, authorized: false,
                                                  lastSampleAge: nil, secondsSinceRequest: 0), .denied)
    }

    func testSearchingThenNotFound() {
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: true, authorized: true,
                                                  lastSampleAge: nil, secondsSinceRequest: 1), .searching)
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: true, authorized: true,
                                                  lastSampleAge: nil,
                                                  secondsSinceRequest: HeadTrackingStatus.searchTimeoutSeconds), .notFound)
    }

    func testActiveWhileSamplesArriveAndLostWhenTheyStop() {
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: true, authorized: true,
                                                  lastSampleAge: 0.05, secondsSinceRequest: 30), .active)
        // AirPods を外した・接続が切れた
        XCTAssertEqual(HeadTrackingStatus.resolve(requested: true, supported: true, authorized: true,
                                                  lastSampleAge: HeadTrackingStatus.staleSeconds + 1,
                                                  secondsSinceRequest: 30), .notFound)
    }

    func testFailuresAreErrorsButNotBlocking() {
        XCTAssertTrue(HeadTrackingStatus.notFound.isError)
        XCTAssertTrue(HeadTrackingStatus.denied.isError)
        XCTAssertFalse(HeadTrackingStatus.active.isError)
        XCTAssertFalse(HeadTrackingStatus.off.isError)
    }

    func testYawIsConvertedToUnitysClockwiseDegrees() throws {
        // CoreMotion は反時計回り(左)が正。Unity は右回りが正
        XCTAssertEqual(try XCTUnwrap(HeadYawSample.unityYawDegrees(attitudeYawRadians: .pi / 2)), -90, accuracy: 1e-9)
        XCTAssertEqual(try XCTUnwrap(HeadYawSample.unityYawDegrees(attitudeYawRadians: -.pi / 6)), 30, accuracy: 1e-9)
        XCTAssertNil(HeadYawSample.unityYawDegrees(attitudeYawRadians: .nan))
    }

    func testSendsAtAbout30Hz() {
        XCTAssertTrue(HeadYawSample.shouldSend(now: 10, lastSent: nil))
        XCTAssertFalse(HeadYawSample.shouldSend(now: 10.01, lastSent: 10))
        XCTAssertTrue(HeadYawSample.shouldSend(now: 10 + HeadYawSample.sendInterval, lastSent: 10))
        // 60Hz で届く頭の向きは2回に1回送る(浮動小数の誤差で3回に1回 = 20Hz にならない)
        var sent = 0
        var last: TimeInterval? = nil
        for i in 0..<60 {
            let now = 100 + Double(i) / 60.0
            if HeadYawSample.shouldSend(now: now, lastSent: last) { sent += 1; last = now }
        }
        XCTAssertEqual(sent, 30)
        // 時計が戻った(再起動等)なら送る
        XCTAssertTrue(HeadYawSample.shouldSend(now: 5, lastSent: 10))
    }
}
