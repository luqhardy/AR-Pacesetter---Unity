import XCTest
@testable import ARRunnerAppLogic

/// 走行履歴(RunHistoryEntry)。Unity の HistoryData を読み、経路つきで出せること
final class RunHistoryTests: XCTestCase {

    /// Unity(SwiftMessageSender.BuildHistoryJson)が送る形そのもの
    private let historyJson = """
    {"event":"HistoryData","sessions":[
     {"dateIso":"2026-10-13T07:12:00.0000000+09:00","distanceKm":5.012,"elapsedSeconds":1610,
      "averageSync":91.2,"grade":"S","route":[[34.693700,135.502300],[34.694000,135.502600]]},
     {"dateIso":"2026-10-01T18:40:00.0000000+09:00","distanceKm":3.200,"elapsedSeconds":1064,
      "averageSync":83.5,"grade":"A","route":[]}
    ]}
    """

    private func sessions() throws -> [[String: Any]] {
        let dict = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(historyJson.utf8)) as? [String: Any])
        return try XCTUnwrap(dict["sessions"] as? [[String: Any]])
    }

    func testParsesUnityHistoryWithRoute() throws {
        let entries = RunHistoryEntry.parse(sessions: try sessions())
        XCTAssertEqual(entries.count, 2)

        let latest = entries[0]
        XCTAssertEqual(latest.distanceKm, 5.012, accuracy: 1e-9)
        XCTAssertEqual(latest.elapsedSeconds, 1610, accuracy: 1e-9)   // 整数で届いても読める
        XCTAssertEqual(latest.grade, "S")
        XCTAssertEqual(latest.route.count, 2)
        XCTAssertEqual(latest.route[0].latitude, 34.6937, accuracy: 1e-9)
        XCTAssertEqual(latest.route[0].longitude, 135.5023, accuracy: 1e-9)
        XCTAssertEqual(latest.route[1].latitude, 34.694, accuracy: 1e-9)
        XCTAssertEqual(latest.route[1].longitude, 135.5026, accuracy: 1e-9)
        XCTAssertTrue(latest.hasRoute)

        XCTAssertTrue(entries[1].route.isEmpty, "経路を記録する前の走行は空")
        XCTAssertFalse(entries[1].hasRoute)
    }

    func testOldHistoryWithoutRouteKeyStillParses() {
        // 2026-10-13 より前の Unity エクスポートは route を送らない
        let entries = RunHistoryEntry.parse(sessions: [
            ["dateIso": "2026-09-01T06:55:00", "distanceKm": 2.1, "elapsedSeconds": 705.0,
             "averageSync": 76.8, "grade": "B"]
        ])
        XCTAssertEqual(entries.count, 1)
        XCTAssertEqual(entries[0].route, [])
    }

    func testBrokenRoutePointsAndUndatedRowsAreDropped() {
        let entries = RunHistoryEntry.parse(sessions: [
            ["dateIso": "", "distanceKm": 1.0],                       // 識別子にならない
            ["dateIso": "2026-10-13T07:00:00", "distanceKm": 1.0,
             "route": [[34.0, 135.0], [91.0, 135.0], [34.1], "x", [34.2, 135.2]] as [Any]]
        ])
        XCTAssertEqual(entries.count, 1)
        XCTAssertEqual(entries[0].route.map(\.latitude), [34.0, 34.2])
    }

    func testDisplayLabels() {
        let e = RunHistoryEntry(dateIso: "2026-10-13T07:12:00", distanceKm: 2.0,
                                elapsedSeconds: 642, averageSync: 80, grade: "B")
        XCTAssertEqual(e.dateLabel, "10月13日")
        XCTAssertEqual(e.timeLabel, "10:42")
        XCTAssertEqual(e.paceLabel, "5'21\"")

        let standing = RunHistoryEntry(dateIso: "2026-10-13T07:12:00", distanceKm: 0,
                                       elapsedSeconds: 60, averageSync: 0, grade: "D")
        XCTAssertNil(standing.paceLabel, "距離ほぼ0ではペースを出さない")
    }

    func testCacheRoundTripsThroughJson() throws {
        let cache = RunHistoryCache(entries: RunHistoryEntry.parse(sessions: try sessions()),
                                    receivedAt: Date(timeIntervalSince1970: 2_000_000))
        let decoded = try JSONDecoder().decode(RunHistoryCache.self, from: JSONEncoder().encode(cache))
        XCTAssertEqual(decoded, cache)
    }

    // MARK: 過去ルートの並べ方

    private func entry(_ day: Int, km: Double, route: Bool = true) -> RunHistoryEntry {
        RunHistoryEntry(dateIso: String(format: "2026-10-%02dT07:00:00", day), distanceKm: km,
                        elapsedSeconds: km * 330, averageSync: 80, grade: "B",
                        route: route ? [RouteCoordinate(latitude: 34, longitude: 135),
                                        RouteCoordinate(latitude: 34.001, longitude: 135)] : [])
    }

    func testCandidatesAreRoutedRunsClosestToTheTargetDistance() {
        // 新しい順で届く
        let history = [entry(13, km: 3.0), entry(12, km: 9.5), entry(11, km: 5.2, route: false), entry(10, km: 10.4)]
        let picked = PastRouteOrdering.candidates(from: history, targetDistanceKm: 10)
        XCTAssertEqual(picked.map(\.distanceKm), [10.4, 9.5, 3.0])
    }

    func testEquallyCloseCandidatesKeepNewestFirst() {
        let history = [entry(13, km: 5.0), entry(12, km: 5.0)]
        let picked = PastRouteOrdering.candidates(from: history, targetDistanceKm: 5)
        XCTAssertEqual(picked.map(\.dateIso), [history[0].dateIso, history[1].dateIso])
    }

    // MARK: 地図の表示範囲

    func testBoundsFitTheRouteWithPadding() throws {
        let b = try XCTUnwrap(RouteBounds(route: [
            RouteCoordinate(latitude: 34.000, longitude: 135.000),
            RouteCoordinate(latitude: 34.010, longitude: 135.020),
        ], padding: 1.5))
        XCTAssertEqual(b.centerLatitude, 34.005, accuracy: 1e-9)
        XCTAssertEqual(b.centerLongitude, 135.010, accuracy: 1e-9)
        XCTAssertEqual(b.latitudeSpan, 0.015, accuracy: 1e-9)
        XCTAssertEqual(b.longitudeSpan, 0.030, accuracy: 1e-9)
    }

    func testBoundsNeverCollapseAndNeedAPoint() {
        let single = RouteBounds(route: [RouteCoordinate(latitude: 34, longitude: 135)])
        XCTAssertEqual(single?.latitudeSpan, 0.002)
        XCTAssertNil(RouteBounds(route: []))
    }
}
