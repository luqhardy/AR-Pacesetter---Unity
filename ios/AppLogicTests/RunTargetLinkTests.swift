import XCTest
@testable import ARRunnerAppLogic

/// ランニング設定(S5)の3値の相互依存と、ルート選択へ進む前の最終チェック
final class RunTargetLinkTests: XCTestCase {

    func testChangingDistanceKeepsThePaceAndMovesTheTime() {
        let s = RunTargetLink.keepingPace(distanceKm: 10, paceKmH: 8)
        XCTAssertEqual(s.distanceKm, 10)
        XCTAssertEqual(s.paceKmH, 8)
        XCTAssertEqual(s.timeSeconds, 4500, "10km を 8km/h なら 75分")
    }

    func testChangingTimeMovesThePaceNotTheDistance() {
        let s = RunTargetLink.keepingTime(distanceKm: 10, timeSeconds: 3600)
        XCTAssertEqual(s.distanceKm, 10, "距離は自動では変えない")
        XCTAssertEqual(s.timeSeconds, 3600)
        XCTAssertEqual(s.paceKmH, 10, accuracy: 1e-9)
    }

    func testTimeOutOfRangeIsClampedAndThePaceFollows() {
        // 200km を 1km/h → 200時間は上限(24時間)を超える。距離は保ち、ペースを合わせ直す
        let s = RunTargetLink.keepingPace(distanceKm: 200, paceKmH: 1)
        XCTAssertEqual(s.distanceKm, 200)
        XCTAssertEqual(s.timeSeconds, Int(RunSettingRanges.timeSeconds.maximum))
        XCTAssertEqual(s.paceKmH, 200.0 / 24.0, accuracy: 1e-9)
    }

    func testPaceOutOfRangeIsClampedAndTheTimeFollows() {
        // 200km を1分 → 12000km/h。ペースを上限(30km/h)へ寄せ、時間を合わせる
        let s = RunTargetLink.keepingTime(distanceKm: 200, timeSeconds: 60)
        XCTAssertEqual(s.distanceKm, 200)
        XCTAssertEqual(s.paceKmH, RunSettingRanges.paceKmH.maximum)
        XCTAssertEqual(s.timeSeconds, 24_000)
    }

    func testResultsAreAlwaysConsistent() {
        for distance in [0.5, 1, 5, 10, 21.0975, 42.195, 100, 200] {
            for pace in [1, 3, 8, 12.3, 20, 30] {
                let s = RunTargetLink.keepingPace(distanceKm: distance, paceKmH: pace)
                XCTAssertNotEqual(RunTargetIssue.check(s), .inconsistent, "\(distance)km \(pace)km/h")
            }
            for time in [60, 600, 3600, 10_000, 86_400] {
                let s = RunTargetLink.keepingTime(distanceKm: distance, timeSeconds: time)
                XCTAssertNotEqual(RunTargetIssue.check(s), .inconsistent, "\(distance)km \(time)s")
            }
        }
    }

    func testZeroOrNotANumberDoesNotDivideByZero() {
        XCTAssertNil(RunTargetLink.timeSeconds(distanceKm: 10, paceKmH: 0))
        XCTAssertNil(RunTargetLink.paceKmH(distanceKm: 10, timeSeconds: 0))
        XCTAssertNil(RunTargetLink.timeSeconds(distanceKm: .nan, paceKmH: 8))
        XCTAssertEqual(RunTargetLink.fallback.timeSeconds, 4500)
    }

    func testStepButtonsStopWhenTheOtherValueIsAtItsLimit() {
        // ペースが下限(1km/h)なら、時間は延ばせない(延ばすとペースが範囲外になる)
        let slowest = RunTargetLink.keepingPace(distanceKm: 1, paceKmH: RunSettingRanges.paceKmH.minimum)
        XCTAssertFalse(RunTargetLink.canIncreaseTime(slowest))
        XCTAssertTrue(RunTargetLink.canDecreaseTime(slowest))
        // 時間が上限なら、距離は延ばせない
        let longest = RunTargetLink.keepingPace(distanceKm: 200, paceKmH: 5)
        XCTAssertFalse(RunTargetLink.canIncreaseDistance(longest))
    }

    func testFinalCheckRejectsUnrunnablePaces() {
        let walk = RunTargetLink.keepingTime(distanceKm: 0.5, timeSeconds: 3600)    // 0.5km/h → 下限1km/h
        XCTAssertEqual(RunTargetIssue.check(walk), .paceTooSlow)
        let rocket = RunTargetLink.keepingPace(distanceKm: 10, paceKmH: 30)
        XCTAssertEqual(RunTargetIssue.check(rocket), .paceTooFast)
        let ok = RunTargetLink.keepingPace(distanceKm: 5, paceKmH: 10)
        XCTAssertNil(RunTargetIssue.check(ok))
    }

    func testFinalCheckCatchesMismatchedValues() {
        XCTAssertEqual(RunTargetIssue.check(RunTargetSettings(paceKmH: 8, distanceKm: 10, timeSeconds: 3600)), .inconsistent)
    }
}
