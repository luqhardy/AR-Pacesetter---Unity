import XCTest
@testable import ARRunnerAppLogic

/// 速度のならし(SpeedSmoother)と目標ペースとの差(PaceGap)
final class PaceMetricsTests: XCTestCase {

    private let t0 = Date(timeIntervalSince1970: 1_000_000)

    // MARK: - PaceGap: 目標どおり・遅れ・先行

    func testOnPaceGapIsNearlyZero() {
        // 8 km/h = 8/3.6 m/s。600秒でアバター距離 = 600 × 8/3.6 ≒ 1333.333...m
        let snap = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: 600,
            runnerDistanceMeters: 600 * 8 / 3.6,
            smoothedSpeedKmH: 8
        )
        XCTAssertTrue(snap.isReliable)
        XCTAssertEqual(snap.avatarDistanceMeters, 1333.333333, accuracy: 1e-6)
        XCTAssertEqual(snap.gapMeters ?? 999, 0, accuracy: 1e-6)
        XCTAssertEqual(snap.gapSeconds ?? 999, 0, accuracy: 1e-9)
        XCTAssertEqual(snap.smoothedSpeedKmH ?? -1, 8, accuracy: 1e-12)
    }

    func testBehindProducesPositiveGaps() {
        // アバター想定 1333.33m、ランナー 1000m → 遅れ
        let snap = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: 600,
            runnerDistanceMeters: 1000,
            smoothedSpeedKmH: 6
        )
        XCTAssertTrue(snap.isReliable)
        XCTAssertEqual(snap.gapMeters ?? 0, 333.333333, accuracy: 1e-4)
        XCTAssertGreaterThan(snap.gapSeconds ?? 0, 0)
        XCTAssertEqual(snap.gapSeconds ?? 0, 600 - 1000 / (8 / 3.6), accuracy: 1e-9)
    }

    func testAheadProducesNegativeGaps() {
        let snap = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: 600,
            runnerDistanceMeters: 1600,
            smoothedSpeedKmH: 10
        )
        XCTAssertTrue(snap.isReliable)
        XCTAssertLessThan(snap.gapMeters ?? 0, 0)
        XCTAssertLessThan(snap.gapSeconds ?? 0, 0)
        XCTAssertEqual(snap.gapMeters ?? 0, 1333.333333 - 1600, accuracy: 1e-4)
    }

    func testElapsedZeroGivesAvatarDistanceZero() {
        let snap = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: 0,
            runnerDistanceMeters: 0,
            smoothedSpeedKmH: nil
        )
        XCTAssertTrue(snap.isReliable)
        XCTAssertEqual(snap.avatarDistanceMeters, 0, accuracy: 1e-12)
        XCTAssertEqual(snap.gapMeters ?? 999, 0, accuracy: 1e-12)
        XCTAssertEqual(snap.gapSeconds ?? 999, 0, accuracy: 1e-12)
    }

    func testNilRunnerDistanceLeavesGapsNil() {
        let snap = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: 600,
            runnerDistanceMeters: nil,
            smoothedSpeedKmH: 8
        )
        XCTAssertFalse(snap.isReliable)
        XCTAssertNil(snap.gapMeters)
        XCTAssertNil(snap.gapSeconds)
        XCTAssertEqual(snap.avatarDistanceMeters, 1333.333333, accuracy: 1e-6)
        XCTAssertEqual(snap.smoothedSpeedKmH ?? -1, 8, accuracy: 1e-12)
    }

    func testInvalidTargetPaceLeavesGapsNil() {
        for bad in [0.0, -1.0, Double.nan, Double.infinity, -Double.infinity] {
            let snap = PaceGap.snapshot(
                targetPaceKmH: bad,
                elapsedSeconds: 600,
                runnerDistanceMeters: 1000,
                smoothedSpeedKmH: 8
            )
            XCTAssertFalse(snap.isReliable, "pace \(bad) should be unreliable")
            XCTAssertNil(snap.gapMeters)
            XCTAssertNil(snap.gapSeconds)
        }
    }

    func testNegativeOrNanElapsedIsTreatedAsZero() {
        let fromNegative = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: -10,
            runnerDistanceMeters: 0,
            smoothedSpeedKmH: nil
        )
        XCTAssertEqual(fromNegative.elapsedSeconds, 0, accuracy: 1e-12)
        XCTAssertEqual(fromNegative.avatarDistanceMeters, 0, accuracy: 1e-12)

        let fromNan = PaceGap.snapshot(
            targetPaceKmH: 8,
            elapsedSeconds: .nan,
            runnerDistanceMeters: 0,
            smoothedSpeedKmH: nil
        )
        XCTAssertEqual(fromNan.elapsedSeconds, 0, accuracy: 1e-12)
        XCTAssertEqual(fromNan.avatarDistanceMeters, 0, accuracy: 1e-12)
    }

    // MARK: - SpeedSmoother

    func testNegativeNanInfiniteSpeedsAreDropped() {
        var s = SpeedSmoother(windowSeconds: 5)
        s.add(speedMetersPerSecond: -0.1, at: t0)
        s.add(speedMetersPerSecond: .nan, at: t0.addingTimeInterval(1))
        s.add(speedMetersPerSecond: .infinity, at: t0.addingTimeInterval(2))
        s.add(speedMetersPerSecond: -.infinity, at: t0.addingTimeInterval(3))
        XCTAssertEqual(s.sampleCount, 0)
        XCTAssertNil(s.smoothedSpeedKmH(at: t0.addingTimeInterval(3)))
    }

    func testZeroSpeedIsIncludedInAverage() {
        var s = SpeedSmoother(windowSeconds: 5)
        // 2 m/s と 0 m/s → 平均 1 m/s = 3.6 km/h
        s.add(speedMetersPerSecond: 2, at: t0)
        s.add(speedMetersPerSecond: 0, at: t0.addingTimeInterval(1))
        let kmh = s.smoothedSpeedKmH(at: t0.addingTimeInterval(1))
        XCTAssertEqual(kmh ?? -1, 3.6, accuracy: 1e-12)
        XCTAssertEqual(s.sampleCount, 2)
    }

    func testMetersPerSecondConvertedToKmH() {
        var s = SpeedSmoother(windowSeconds: 5)
        s.add(speedMetersPerSecond: 2.5, at: t0) // 9 km/h
        XCTAssertEqual(s.smoothedSpeedKmH(at: t0) ?? -1, 9.0, accuracy: 1e-12)
    }

    func testNoSamplesReturnsNil() {
        let s = SpeedSmoother(windowSeconds: 5)
        XCTAssertNil(s.smoothedSpeedKmH(at: t0))
    }

    func testSamplesOlderThanWindowAreExcludedFromAverage() {
        var s = SpeedSmoother(windowSeconds: 5)
        s.add(speedMetersPerSecond: 10, at: t0)                          // 窓の外になる
        s.add(speedMetersPerSecond: 1, at: t0.addingTimeInterval(6))     // いま
        // at=t0+6 の窓は [t0+1, t0+6]。t0 の 10m/s は含まれない → 1m/s = 3.6km/h
        XCTAssertEqual(s.smoothedSpeedKmH(at: t0.addingTimeInterval(6)) ?? -1, 3.6, accuracy: 1e-12)
    }

    func testAddPrunesSamplesOlderThanWindow() {
        var s = SpeedSmoother(windowSeconds: 5)
        s.add(speedMetersPerSecond: 10, at: t0)
        XCTAssertEqual(s.sampleCount, 1)
        // 5秒より後に追加すると、t0 は window より古くなり削除される
        s.add(speedMetersPerSecond: 1, at: t0.addingTimeInterval(5.1))
        XCTAssertEqual(s.sampleCount, 1, "古いサンプルは add 時に削除される")
    }

    func testSampleExactlyAtWindowBoundaryIsIncluded() {
        var s = SpeedSmoother(windowSeconds: 5)
        let now = t0.addingTimeInterval(10)
        let boundary = now.addingTimeInterval(-5) // time − windowSeconds ちょうど
        s.add(speedMetersPerSecond: 2, at: boundary)
        s.add(speedMetersPerSecond: 4, at: now)
        // 平均 (2+4)/2 = 3 m/s = 10.8 km/h
        XCTAssertEqual(s.smoothedSpeedKmH(at: now) ?? -1, 10.8, accuracy: 1e-12)
    }

    func testFutureSamplesAreExcluded() {
        var s = SpeedSmoother(windowSeconds: 5)
        s.add(speedMetersPerSecond: 1, at: t0)
        s.add(speedMetersPerSecond: 100, at: t0.addingTimeInterval(2)) // 未来(問い合わせ時刻より後)
        // at=t0 では未来の 100 は含めない → 1 m/s = 3.6 km/h
        XCTAssertEqual(s.smoothedSpeedKmH(at: t0) ?? -1, 3.6, accuracy: 1e-12)
    }
}
