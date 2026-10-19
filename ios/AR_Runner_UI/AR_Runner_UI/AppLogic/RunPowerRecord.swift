import Foundation

// MARK: - 走行中の電池・発熱
//
// 非機能要件 §10「連続稼働60分(バッテリー30%以上)」を実地テストで確かめるための記録。
// これまで電池の残量も端末の発熱も、どこにも残っていなかった(CSV の列は §5.2 で固定)。
// 走行の開始・終了の電池と、走行中に一番高かった発熱の段階を残し、実地テストの書き出しに載せる。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。値の取得は Core/Device/PowerMonitor。

/// 端末の発熱の段階(ProcessInfo.ThermalState と同じ順)。serious 以上で iOS が処理を絞り始める
nonisolated enum ThermalLevel: Int, Codable, Comparable {
    case nominal = 0, fair, serious, critical

    static func < (a: ThermalLevel, b: ThermalLevel) -> Bool { a.rawValue < b.rawValue }

    /// 実地テストの書き出し用(開発者モードは日本語のみ)
    var reportLabel: String {
        switch self {
        case .nominal: return "平常"
        case .fair: return "やや高い"
        case .serious: return "高い(処理を絞り始める)"
        case .critical: return "危険(大きく絞られる)"
        }
    }
}

/// ある時点の電池・発熱
nonisolated struct PowerSample: Codable, Equatable {
    var at: Date
    /// 電池残量 0〜1。取れなければ nil(シミュレータ等)。iOS は 5% 刻みでしか返さない
    var batteryLevel: Double?
    var thermal: ThermalLevel
    var lowPowerMode: Bool
}

/// 1回の走行の電池・発熱
nonisolated struct RunPowerRecord: Codable, Equatable {
    /// §10: 60分走った後に残っているべき電池(%)
    static let minimumPercentAfter60Minutes = 30.0
    /// これより短い走行からは60分後を見積もらない(5%刻みの電池では誤差が大きすぎる)
    static let minimumMinutesForProjection = 10.0

    var start: PowerSample
    var end: PowerSample?
    /// 走行中に一番高かった発熱
    var peakThermal: ThermalLevel

    init(start: PowerSample) {
        self.start = start
        self.end = nil
        self.peakThermal = start.thermal
    }

    mutating func observe(thermal: ThermalLevel) {
        peakThermal = max(peakThermal, thermal)
    }

    mutating func finish(with sample: PowerSample) {
        observe(thermal: sample.thermal)
        end = sample
    }

    var durationMinutes: Double? {
        end.map { $0.at.timeIntervalSince(start.at) / 60 }
    }

    /// 走行中に減った電池(パーセントポイント)
    var batteryDropPercent: Double? {
        guard let s = start.batteryLevel, let e = end?.batteryLevel else { return nil }
        return (s - e) * 100
    }

    /// 60分走ったときに残る電池(%)。60分以上走ったなら終了時の値、短ければ同じ減り方が続いた場合の見込み。
    /// 電池が取れない・短すぎる・充電で増えた場合は nil
    var percentAt60Minutes: Double? {
        guard let minutes = durationMinutes, let s = start.batteryLevel, let e = end?.batteryLevel else { return nil }
        if minutes >= 60 { return e * 100 }
        guard minutes >= Self.minimumMinutesForProjection, e <= s else { return nil }
        let dropPerMinute = (s - e) / minutes
        return max(0, (s - dropPerMinute * 60) * 100)
    }

    /// §10 を満たすか(判断できなければ nil)
    var meetsSixtyMinuteSpec: Bool? {
        percentAt60Minutes.map { $0 >= Self.minimumPercentAfter60Minutes }
    }
}
