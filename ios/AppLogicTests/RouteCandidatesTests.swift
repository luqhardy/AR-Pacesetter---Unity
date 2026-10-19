import XCTest
@testable import ARRunnerAppLogic

/// ルート選択(S8)の候補(タスク表 S8)
final class RouteCandidatesTests: XCTestCase {

    private let here = RouteCoordinate(latitude: 34.69, longitude: 135.19)

    private func run(_ iso: String, km: Double, routed: Bool = true) -> RunHistoryEntry {
        RunHistoryEntry(dateIso: iso, distanceKm: km, elapsedSeconds: km * 360, averageSync: 80, grade: "A",
                        route: routed ? [here, RouteCoordinate(latitude: 34.70, longitude: 135.19)] : [])
    }

    func testPastRoutesNearestToTheTargetComeFirst() {
        let list = RouteCandidates.build(history: [run("2026-10-18T07:00:00", km: 3),
                                                  run("2026-10-17T07:00:00", km: 9.5),
                                                  run("2026-10-16T07:00:00", km: 5)],
                                         targetDistanceKm: 10, currentLocation: here)
        XCTAssertEqual(list.map(\.distanceKm), [9.5, 5, 3])
        XCTAssertTrue(list.allSatisfy { $0.kind == .past })
    }

    func testAGeneratedRouteIsOfferedWhenThereAreNoCandidates() throws {
        // 経路の無い(2026-10-13 より前の)記録しか無い
        let list = RouteCandidates.build(history: [run("2026-10-01T07:00:00", km: 5, routed: false)],
                                         targetDistanceKm: 10, currentLocation: here)
        let only = try XCTUnwrap(list.first)
        XCTAssertEqual(list.count, 1)
        XCTAssertEqual(only.kind, .generated)
        XCTAssertEqual(only.id, RouteCandidates.generatedID)
        XCTAssertEqual(only.distanceKm, 10)
        XCTAssertTrue(only.hasLine)
    }

    func testGeneratedRouteWithoutLocationHasNoLine() {
        let list = RouteCandidates.build(history: [], targetDistanceKm: 5, currentLocation: nil)
        XCTAssertEqual(list.first?.kind, .generated)
        XCTAssertEqual(list.first?.hasLine, false)
    }

    func testOutAndBackCoversTheTargetDistanceAndReturns() throws {
        let line = RouteCandidates.outAndBack(from: here, distanceKm: 10)
        XCTAssertEqual(line.count, 3)
        XCTAssertEqual(line.first, here)
        XCTAssertEqual(line.last, here, "スタートへ戻る")
        // 片道 5km ≒ 緯度 0.0449°
        XCTAssertEqual(try XCTUnwrap(line.dropFirst().first).latitude - here.latitude, 5 / 111.32, accuracy: 1e-9)
        XCTAssertTrue(RouteCandidates.outAndBack(from: here, distanceKm: 0).isEmpty)
    }

    func testTheFirstCandidateIsSelectedAndAnExistingChoiceIsKept() {
        let list = RouteCandidates.build(history: [run("2026-10-18T07:00:00", km: 3),
                                                  run("2026-10-17T07:00:00", km: 9.5)],
                                         targetDistanceKm: 10, currentLocation: here)
        XCTAssertEqual(RouteCandidates.initialSelection(list, keeping: nil), "2026-10-17T07:00:00")
        XCTAssertEqual(RouteCandidates.initialSelection(list, keeping: "2026-10-18T07:00:00"), "2026-10-18T07:00:00")
        XCTAssertEqual(RouteCandidates.initialSelection(list, keeping: "gone"), "2026-10-17T07:00:00")
        XCTAssertNil(RouteCandidates.initialSelection([], keeping: nil))
    }

    func testEstimatedTimeUsesTheTargetPace() {
        let route = RouteCandidate(id: "x", kind: .past, dateIso: nil, distanceKm: 10, coordinates: [])
        XCTAssertEqual(route.estimatedSeconds(paceKmH: 8), 4500)
        XCTAssertNil(route.estimatedSeconds(paceKmH: 0))
    }
}
