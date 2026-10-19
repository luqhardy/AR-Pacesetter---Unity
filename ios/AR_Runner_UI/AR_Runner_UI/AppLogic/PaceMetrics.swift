import Foundation

// MARK: - 速度のならしと目標ペースとの差
//
// 走行中の瞬間速度(m/s)を時間窓で平均し km/h にする部品と、
// 目標ペースのアバター位置とランナー距離の差を出す部品。
// UI / CoreLocation / Unity には依存しない(ios/AppLogicTests で `swift test` できる)。

/// 直近の速度サンプルを時間窓で平均する。
/// サンプルは m/s、出力は km/h。負・NaN・無限大は捨て、0(立ち止まり)は有効。
nonisolated struct SpeedSmoother {
    /// 平均に含める過去方向の秒数(既定 5 秒)。変更可能。
    var windowSeconds: Double

    private var samples: [(speed: Double, time: Date)] = []

    init(windowSeconds: Double = 5) {
        self.windowSeconds = windowSeconds
    }

    /// 速度サンプルを追加する。無効値は捨て、追加後に窓より古いものを削除する。
    mutating func add(speedMetersPerSecond: Double, at time: Date) {
        guard speedMetersPerSecond.isFinite, speedMetersPerSecond >= 0 else { return }
        samples.append((speedMetersPerSecond, time))
        prune(relativeTo: time)
    }

    /// `time − windowSeconds ≦ サンプル時刻 ≦ time` の平均を km/h で返す。
    /// 該当がなければ nil。`time` より未来のサンプルは含めない。
    func smoothedSpeedKmH(at time: Date) -> Double? {
        let oldest = time.addingTimeInterval(-windowSeconds)
        let inWindow = samples.filter { $0.time >= oldest && $0.time <= time }
        guard !inWindow.isEmpty else { return nil }
        let meanMs = inWindow.map(\.speed).reduce(0, +) / Double(inWindow.count)
        return meanMs * 3.6
    }

    /// 保持中のサンプル数(テストと診断用)
    var sampleCount: Int { samples.count }

    private mutating func prune(relativeTo time: Date) {
        let oldest = time.addingTimeInterval(-windowSeconds)
        samples.removeAll { $0.time < oldest }
    }
}

/// 目標ペースのアバターとランナーの差の一時点。
/// 符号: gap がプラス = ユーザー遅れ、マイナス = ユーザー先行。
nonisolated struct PaceGapSnapshot: Equatable {
    var targetPaceKmH: Double
    var elapsedSeconds: Double
    /// 目標ペースで走り続けたアバターの想定距離(m)
    var avatarDistanceMeters: Double
    var runnerDistanceMeters: Double?
    var gapMeters: Double?
    var gapSeconds: Double?
    var smoothedSpeedKmH: Double?
    /// ランナー距離があり、目標ペースが有効なとき true
    var isReliable: Bool
}

/// 目標ペースとの差を計算する入口。
nonisolated enum PaceGap {
    /// 入力から `PaceGapSnapshot` を作る。
    /// - targetPaceKmH: 目標ペース(km/h)。NaN・無限大・0以下なら差は出さない
    /// - elapsedSeconds: 経過秒。負・NaN は 0 として扱う
    /// - runnerDistanceMeters: 実測距離(m)。不安定・推定のみなら nil
    /// - smoothedSpeedKmH: ならした現在速度(km/h)。あるならスナップショットへ載せるだけ
    static func snapshot(
        targetPaceKmH: Double,
        elapsedSeconds: Double,
        runnerDistanceMeters: Double?,
        smoothedSpeedKmH: Double?
    ) -> PaceGapSnapshot {
        let elapsed = (elapsedSeconds.isFinite && elapsedSeconds >= 0) ? elapsedSeconds : 0

        let paceValid = targetPaceKmH.isFinite && targetPaceKmH > 0
        let speedMs = paceValid ? (targetPaceKmH / 3.6) : 0
        let avatarDistance = paceValid ? (elapsed * speedMs) : 0

        guard paceValid, let runner = runnerDistanceMeters, runner.isFinite else {
            return PaceGapSnapshot(
                targetPaceKmH: targetPaceKmH,
                elapsedSeconds: elapsed,
                avatarDistanceMeters: avatarDistance,
                runnerDistanceMeters: runnerDistanceMeters,
                gapMeters: nil,
                gapSeconds: nil,
                smoothedSpeedKmH: smoothedSpeedKmH,
                isReliable: false
            )
        }

        return PaceGapSnapshot(
            targetPaceKmH: targetPaceKmH,
            elapsedSeconds: elapsed,
            avatarDistanceMeters: avatarDistance,
            runnerDistanceMeters: runner,
            gapMeters: avatarDistance - runner,
            gapSeconds: elapsed - runner / speedMs,
            smoothedSpeedKmH: smoothedSpeedKmH,
            isReliable: true
        )
    }
}
