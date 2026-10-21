import XCTest
@testable import ARRunnerAppLogic

/// 選んだルートからの逸脱の案内(HANDOVER §5「ルート同期」)— 既定 OFF・過去のルートだけ
final class RouteGuidanceTests: XCTestCase {

    private let a = RouteCoordinate(latitude: 34.69, longitude: 135.19)
    private let b = RouteCoordinate(latitude: 34.70, longitude: 135.19)

    private var past: RouteCandidate {
        RouteCandidate(id: "2026-10-18T07:00:00", kind: .past, dateIso: "2026-10-18T07:00:00",
                       distanceKm: 1.1, coordinates: [a, b])
    }

    func testAPastRouteIsSentAsFlatLatLonWhenEnabled() {
        XCTAssertEqual(RouteGuidance.payload(for: past, enabled: true), [34.69, 135.19, 34.70, 135.19])
    }

    func testNothingIsSentWhenTheSwitchIsOff() {
        XCTAssertNil(RouteGuidance.payload(for: past, enabled: false), "既定 OFF")
    }

    func testTheGeneratedOutAndBackRouteIsNeverSent() {
        let generated = RouteCandidate(id: RouteCandidates.generatedID, kind: .generated, dateIso: nil,
                                       distanceKm: 10, coordinates: [a, b, a])
        XCTAssertFalse(RouteGuidance.isAvailable(for: generated))
        XCTAssertNil(RouteGuidance.payload(for: generated, enabled: true))
    }

    func testARouteWithoutALineIsNotAvailable() {
        let onePoint = RouteCandidate(id: "x", kind: .past, dateIso: nil, distanceKm: 0, coordinates: [a])
        XCTAssertFalse(RouteGuidance.isAvailable(for: onePoint))
        XCTAssertFalse(RouteGuidance.isAvailable(for: nil))
        XCTAssertNil(RouteGuidance.payload(for: nil, enabled: true))
    }
}
