import XCTest
@testable import ARRunnerAppLogic

/// トラックのレーン(Docs/PACESETTER_BEHAVIOR_SPEC.md R-40〜R-43)
final class TrackRoutesTests: XCTestCase {
    func testTheFirstLaneIsOfferedAsARouteAfterTheOtherCandidates() throws {
        let list = RouteCandidates.build(history: [], targetDistanceKm: 5, currentLocation: nil)
        XCTAssertEqual(list.first?.kind, .generated, "最初に選ばれるルートは変えない")

        let lane = try XCTUnwrap(list.last)
        XCTAssertEqual(lane.kind, .track)
        XCTAssertEqual(lane.id, "track400_lane1")
        XCTAssertEqual(lane.lapLengthMeters, 400)
        XCTAssertEqual(lane.distanceKm, 0.4)
    }

    func testRouteGuidanceDoesNotApplyToALane() {
        let lane = RouteCandidates.build(history: [], targetDistanceKm: 5, currentLocation: nil).last
        XCTAssertFalse(RouteGuidance.isAvailable(for: lane))
    }

    func testOnlyLanesHaveALapLength() {
        let list = RouteCandidates.build(history: [], targetDistanceKm: 5, currentLocation: nil)
        XCTAssertNil(list.first?.lapLengthMeters)
    }

    func testLapsAndRemainingUseTheSelectedLane() throws {
        let start = try XCTUnwrap(LapProgress(distanceMeters: 0, lapLengthMeters: 400))
        XCTAssertEqual(start.currentLap, 1)
        XCTAssertEqual(start.remainingMeters, 400)

        let middle = try XCTUnwrap(LapProgress(distanceMeters: 650, lapLengthMeters: 400))
        XCTAssertEqual(middle.completedLaps, 1)
        XCTAssertEqual(middle.currentLap, 2)
        XCTAssertEqual(middle.remainingMeters, 150)

        let outerLane = try XCTUnwrap(LapProgress(distanceMeters: 650, lapLengthMeters: 407))
        XCTAssertEqual(outerLane.remainingMeters, 164, "外側のレーンは距離が違う")
    }

    func testALapBoundaryStartsTheNextLap() throws {
        let lap = try XCTUnwrap(LapProgress(distanceMeters: 400, lapLengthMeters: 400))
        XCTAssertEqual(lap.completedLaps, 1)
        XCTAssertEqual(lap.remainingMeters, 400)
    }

    func testNoLapProgressOffTheTrack() {
        XCTAssertNil(LapProgress(distanceMeters: 100, lapLengthMeters: nil))
        XCTAssertNil(LapProgress(distanceMeters: 100, lapLengthMeters: 0))
        XCTAssertNil(LapProgress(distanceMeters: .nan, lapLengthMeters: 400))
    }
}
