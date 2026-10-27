import XCTest
@testable import ARRunnerAppLogic

final class XrealImuFusionTests: XCTestCase {

    private static let g = Float(XrealImuFusion.standardGravity)

    private func record(_ ms: Double, gyro: (Float, Float, Float) = (0, 0, 0),
                        accel: (Float, Float, Float) = (0, -g, 0)) -> XrealImuRecord {
        XrealImuRecord(timestampNanoseconds: UInt64(ms * 1_000_000), gyro: gyro, accel: accel, temperatureCelsius: 30)
    }

    /// 1kHz で `seconds` 秒ぶん流す。受信はグラス時刻 + 遅れ(既定 5ms)
    @discardableResult
    private func feed(_ fusion: inout XrealImuFusion, from startMs: Double, seconds: Double,
                      gyro: (Float, Float, Float) = (0, 0, 0), accel: (Float, Float, Float) = (0, -g, 0),
                      clockOffset: Double = 100, delay: (Int) -> Double = { _ in 0.005 }) -> [XrealAccelSample] {
        var samples: [XrealAccelSample] = []
        for i in 0..<Int(seconds * 1000) {
            let ms = startMs + Double(i)
            if let s = fusion.add(record(ms, gyro: gyro, accel: accel),
                                  receivedAt: ms / 1000 + clockOffset + delay(i)) {
                samples.append(s)
            }
        }
        return samples
    }

    func testTurningRightIsPositiveYaw() {
        var f = XrealImuFusion()
        feed(&f, from: 0, seconds: 1, gyro: (0, .pi / 2, 0))   // 90°/s で右へ1秒
        XCTAssertEqual(f.yawDegrees ?? 0, 90, accuracy: 1)
    }

    func testYawFollowsWorldUpWhenHeadIsPitched() {
        // 30° うつむいた頭で、世界の鉛直軸まわりに右へ 90°/s 回る
        let pitch = Float.pi / 6
        let up = (x: Float(0), y: -cos(pitch), z: -sin(pitch))        // 加速度計が読む上向き(グラスの軸)
        let rate = Float.pi / 2
        let gyro = (-rate * up.x, -rate * up.y, -rate * up.z)            // 右回り = 上向きまわりに負
        var f = XrealImuFusion()
        feed(&f, from: 0, seconds: 1, gyro: gyro, accel: (up.x * Self.g, up.y * Self.g, up.z * Self.g))
        // gyro y だけを積分すると cos30° ≈ 0.87 倍(78°)に縮む
        XCTAssertEqual(f.yawDegrees ?? 0, 90, accuracy: 1.5)
    }

    func testYawWrapsToPlusMinus180() {
        var f = XrealImuFusion()
        feed(&f, from: 0, seconds: 2.5, gyro: (0, .pi / 2, 0))   // 225°
        XCTAssertEqual(f.yawDegrees ?? 0, -135, accuracy: 2)
    }

    func testGyroBiasIsLearnedWhileStillAndRemovedFromYaw() {
        var f = XrealImuFusion()
        feed(&f, from: 0, seconds: 30, gyro: (0, 0.01, 0))   // 静止しているがジャイロが 0.01 rad/s ずれている
        XCTAssertEqual(f.gyroBias.y, 0.01, accuracy: 0.0005)
        let before = f.yawDegrees ?? 0
        feed(&f, from: 30_000, seconds: 10, gyro: (0, 0.01, 0))
        // 学習後は10秒で 0.1° 未満しか流れない(学習しなければ 5.7°)
        XCTAssertEqual(f.yawDegrees ?? 0, before, accuracy: 0.1)
    }

    func testAccelIsGravityFreeAt100Hz() {
        var f = XrealImuFusion()
        feed(&f, from: 0, seconds: 3)   // 重力の推定を落ち着かせる
        let samples = feed(&f, from: 3000, seconds: 1, accel: (1, -Self.g, 0))   // 右へ 1 m/s² の一定加速度
        XCTAssertTrue((99...101).contains(samples.count), "\(samples.count)")
        // 一定の加速度は時定数 0.5s で重力へ吸われていくが、最初の 0.1 秒はほぼそのまま見える
        XCTAssertGreaterThan(samples[5].x, 0.8)
        XCTAssertEqual(samples[5].y, 0, accuracy: 0.05)
    }

    func testAccelTimesAreOnTheIphoneClockUsingTheSmallestDelay() {
        var f = XrealImuFusion()
        // 遅れは 5〜25ms でばらつくが、ときどき 1ms で届く
        let samples = feed(&f, from: 0, seconds: 2, clockOffset: 100,
                           delay: { i in i % 97 == 0 ? 0.001 : 0.005 + Double(i % 5) * 0.005 })
        let last = try! XCTUnwrap(samples.last)
        // 最後の区切りはグラス時刻 1.990s → iPhone 時刻 101.990 + 最小の遅れ 1ms
        XCTAssertEqual(last.mediaTime, 101.990 + 0.001, accuracy: 0.001)
        // 単調増加
        XCTAssertEqual(zip(samples, samples.dropFirst()).filter { $0.mediaTime >= $1.mediaTime }.count, 0)
    }

    func testGlassRestartResetsTheClock() {
        var f = XrealImuFusion()
        feed(&f, from: 5_000, seconds: 1, clockOffset: 100)
        // グラスを挿し直すとグラスの時計が 0 からやり直す
        let samples = feed(&f, from: 0, seconds: 1, clockOffset: 200)
        let last = try! XCTUnwrap(samples.last)
        XCTAssertEqual(last.mediaTime, 200.990 + 0.005, accuracy: 0.002)
    }

    func testStatus() {
        XCTAssertEqual(GlassImuStatus.resolve(requested: false, lastSampleAge: 0, secondsSinceRequest: 0), .off)
        XCTAssertEqual(GlassImuStatus.resolve(requested: true, lastSampleAge: nil, secondsSinceRequest: 2), .searching)
        XCTAssertEqual(GlassImuStatus.resolve(requested: true, lastSampleAge: 0.1, secondsSinceRequest: 2), .active)
        XCTAssertEqual(GlassImuStatus.resolve(requested: true, lastSampleAge: nil, secondsSinceRequest: 9), .notFound)
        // 届いていたのに途切れた(グラスを抜いた)
        XCTAssertEqual(GlassImuStatus.resolve(requested: true, lastSampleAge: 3, secondsSinceRequest: 2), .notFound)
        XCTAssertTrue(GlassImuStatus.notFound.isLost)
    }
}
