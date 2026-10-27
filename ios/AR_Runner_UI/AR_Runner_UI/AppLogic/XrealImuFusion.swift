import Foundation

// MARK: - XREAL One のIMUから「頭のヨー」と「100Hz の加速度」を作る
//
// グラスは 1kHz で生のジャイロ・加速度(重力の反力込み)を流す(形式は XrealImuStream.swift)。
// そのままでは使えないので、ここで3つを作る:
//   1. 頭のヨー(度・右が正・基準は任意)— Unity の UpdateHeadYaw へ。AirPods の代わり。
//      ジャイロを「重力から求めた上向き」へ射影して積分するので、うつむいても首振りだけを拾う。
//      基準は任意で良い — Unity が進行方向との差の長周期平均を「まっすぐ前」とする(HeadYawBaseline)。
//      ジャイロのバイアスは静止中だけゆっくり学習する(残るドリフトも Unity の基準が吸収する)
//   2. 重力を除いた加速度の 100Hz 平均 — 走行ログCSV(F-11)の imu_accel 列へ。
//      iPhone の userAcceleration と同じ意味(m/s²・重力除去)にそろえる。軸はグラスの軸(x=右・y=下・z=前)
//   3. グラスの時計 → iPhone の時計(CACurrentMediaTime 軸)の対応 — CSV の時刻を他の列とそろえるため
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// 走行ログへ渡す 100Hz の加速度サンプル
nonisolated struct XrealAccelSample: Equatable {
    /// iPhone の CACurrentMediaTime 軸(秒)
    let mediaTime: Double
    /// 重力を除いた加速度(m/s²・グラスの軸)
    let x: Float, y: Float, z: Float
}

nonisolated struct XrealImuFusion {
    /// 重力の推定に使う時定数(秒)。走りの上下動(2〜3Hz)は通し、重力の向きだけを残す
    static let gravityTimeConstant = 0.5
    /// ジャイロのバイアスを学習する時定数(秒)
    static let biasTimeConstant = 5.0
    /// 静止とみなすジャイロの大きさ(rad/s)・加速度の大きさのずれ(m/s²)
    static let stillGyro = 0.03
    static let stillAccelError = 0.5
    /// CSV の間隔(秒)
    static let accelInterval = 0.01
    /// これより空いたら積分をつながない(途切れ・グラスの再起動)
    static let maxGapSeconds = 0.1
    /// 時計の対応がゆっくり後ろへずれるのを許す速さ(s/s)。グラスと iPhone の水晶の差(数十ppm)を吸収する
    static let clockRelaxRate = 1e-4
    static let standardGravity = 9.80665

    /// 頭のヨー(度・右が正・-180〜180)。まだ1件も来ていなければ nil
    private(set) var yawDegrees: Double?
    /// 学習したジャイロのバイアス(rad/s)
    private(set) var gyroBias: (x: Double, y: Double, z: Double) = (0, 0, 0)

    private var lastTimestamp: UInt64?
    private var gravity: (x: Double, y: Double, z: Double)?
    private var clockOffset: Double?          // mediaTime − glassTime(秒)
    private var accelSum: (x: Double, y: Double, z: Double) = (0, 0, 0)
    private var accelCount = 0
    private var accelWindowEnd: UInt64?

    init() {}

    /// IMU を1件取り込む。100Hz の区切りをまたいだら加速度サンプルを返す
    /// - Parameter receivedAt: 受け取った時刻(CACurrentMediaTime 軸・秒)
    mutating func add(_ record: XrealImuRecord, receivedAt: Double) -> XrealAccelSample? {
        let t = record.timestampNanoseconds
        let glassSeconds = Double(t) / 1e9
        let a = (x: Double(record.accel.x), y: Double(record.accel.y), z: Double(record.accel.z))
        let w = (x: Double(record.gyro.x), y: Double(record.gyro.y), z: Double(record.gyro.z))

        guard let last = lastTimestamp, t > last, Double(t - last) / 1e9 <= Self.maxGapSeconds else {
            // 最初の1件・時計が戻った(グラスの再起動)・長い途切れ: 積分と区切りをつなぎ直す
            if lastTimestamp.map({ t <= $0 }) ?? false { clockOffset = nil }
            lastTimestamp = t
            if gravity == nil { gravity = a }
            if yawDegrees == nil { yawDegrees = 0 }
            accelSum = (0, 0, 0)
            accelCount = 0
            accelWindowEnd = t + UInt64(Self.accelInterval * 1e9)
            updateClock(glassSeconds: glassSeconds, receivedAt: receivedAt, dt: 0)
            return nil
        }
        let dt = Double(t - last) / 1e9
        lastTimestamp = t
        updateClock(glassSeconds: glassSeconds, receivedAt: receivedAt, dt: dt)

        // 1. 重力(上向き)の推定
        var g = gravity ?? a
        let k = min(1, dt / Self.gravityTimeConstant)
        g = (g.x + (a.x - g.x) * k, g.y + (a.y - g.y) * k, g.z + (a.z - g.z) * k)
        gravity = g

        // 2. 静止中だけジャイロのバイアスを学習する
        let gyroNorm = sqrt(w.x * w.x + w.y * w.y + w.z * w.z)
        let accelNorm = sqrt(a.x * a.x + a.y * a.y + a.z * a.z)
        if gyroNorm < Self.stillGyro && abs(accelNorm - Self.standardGravity) < Self.stillAccelError {
            let kb = min(1, dt / Self.biasTimeConstant)
            gyroBias = (gyroBias.x + (w.x - gyroBias.x) * kb,
                        gyroBias.y + (w.y - gyroBias.y) * kb,
                        gyroBias.z + (w.z - gyroBias.z) * kb)
        }

        // 3. ヨー: 上向き(加速度計は重力の反力 = 上向きを読む)まわりの回転。
        //    上から見て反時計回りが正なので、右回りが正の Unity のヨーへは符号を反転する。
        //    頭が水平なら上向き ≈ −y で、結果は +gyro y と同じ
        let gNorm = sqrt(g.x * g.x + g.y * g.y + g.z * g.z)
        if gNorm > 1 {
            let up = (x: g.x / gNorm, y: g.y / gNorm, z: g.z / gNorm)
            let wc = (x: w.x - gyroBias.x, y: w.y - gyroBias.y, z: w.z - gyroBias.z)
            let yawRate = -(wc.x * up.x + wc.y * up.y + wc.z * up.z)
            yawDegrees = Self.wrap((yawDegrees ?? 0) + yawRate * dt * 180 / .pi)
        }

        // 4. 重力を除いた加速度を 10ms ごとに平均する
        accelSum = (accelSum.x + a.x - g.x, accelSum.y + a.y - g.y, accelSum.z + a.z - g.z)
        accelCount += 1
        guard let end = accelWindowEnd, t >= end, accelCount > 0, let offset = clockOffset else { return nil }
        let n = Double(accelCount)
        let sample = XrealAccelSample(mediaTime: glassSeconds + offset,
                                      x: Float(accelSum.x / n), y: Float(accelSum.y / n), z: Float(accelSum.z / n))
        accelSum = (0, 0, 0)
        accelCount = 0
        accelWindowEnd = end + UInt64(Self.accelInterval * 1e9)
        if let next = accelWindowEnd, next <= t { accelWindowEnd = t + UInt64(Self.accelInterval * 1e9) }
        return sample
    }

    /// 受信時刻 − グラス時刻 の最小値を時計の差とする(TCP の遅れが一番小さかった1件が一番正しい)。
    /// 水晶の差で本当の差は少しずつ変わるので、最小値はゆっくり後ろへ緩める
    private mutating func updateClock(glassSeconds: Double, receivedAt: Double, dt: Double) {
        let observed = receivedAt - glassSeconds
        if let offset = clockOffset {
            clockOffset = min(observed, offset + dt * Self.clockRelaxRate)
        } else {
            clockOffset = observed
        }
    }

    static func wrap(_ degrees: Double) -> Double {
        var d = degrees.truncatingRemainder(dividingBy: 360)
        if d > 180 { d -= 360 }
        if d <= -180 { d += 360 }
        return d
    }
}

/// 準備画面のグラスIMU行の状態
nonisolated enum GlassImuStatus: Equatable {
    /// 使っていない(既定)
    case off
    /// 押した直後、IMU を待っている
    case searching
    /// 届いている
    case active
    /// 待っても届かない(USBネットワークが上がらない・ローカルネットワークの許可が無い・古いグラス)
    case notFound

    /// 押した後、これだけ待っても届かなければ見つからない扱い
    static let searchTimeoutSeconds: TimeInterval = 8
    /// 途切れたとみなす秒数(1kHz なので短くて良い)
    static let staleSeconds: TimeInterval = 1

    static func resolve(requested: Bool, lastSampleAge: TimeInterval?, secondsSinceRequest: TimeInterval) -> GlassImuStatus {
        guard requested else { return .off }
        if let age = lastSampleAge, age <= staleSeconds { return .active }
        if lastSampleAge == nil && secondsSinceRequest < searchTimeoutSeconds { return .searching }
        return .notFound
    }

    /// 使っていたのに届かなくなった(走行中の警告に使う)
    var isLost: Bool { self == .notFound }
}
