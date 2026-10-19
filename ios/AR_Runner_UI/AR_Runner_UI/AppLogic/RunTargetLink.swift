import Foundation

// MARK: - ランニング設定(S5)の3値の相互依存
//
// タスク表 S5「時間・距離・ペースは相互依存。距離は基本的に自動修正の対象としない」。
// 以前は3値が独立していて、10km・8km/h・60分のように**つじつまの合わない組み合わせ**のまま
// 走り出せた(10km を 8km/h なら 75分)。
//
// 決まり:
//   - 距離を変えた → ペースは目標なので保ち、時間を計算し直す
//   - ペースを変えた → 時間を計算し直す
//   - 時間を変えた → ペースを計算し直す
//   - 計算した値が範囲(RunSettingRanges)を外れたら端へ寄せ、もう一方を合わせ直す(距離は動かさない)
//
// 端数(タスク表 S5「端数処理」): 内部の値は **時間 = 整数秒、ペース = 丸めない km/h**。
// 画面は表示のときだけ丸める(時間 hh:mm:ss、ペース 0.1km/h と 分'秒"/km)。
// 1回の操作で計算するのは「動かした値 → もう一方」の一方向だけなので、丸めで行き来が止まらないことは無い。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum RunTargetLink {
    private static let timeRange = RunSettingRanges.timeSeconds
    private static let paceRange = RunSettingRanges.paceKmH
    private static let distanceRange = RunSettingRanges.distanceKm

    /// 計算できないとき(ゼロ・数でない)の値。既定の距離とペースに時間を合わせたもの(10km・8km/h → 75分)
    static var fallback: RunTargetSettings {
        let d = RunTargetSettings.defaults
        return RunTargetSettings(paceKmH: d.paceKmH, distanceKm: d.distanceKm,
                                 timeSeconds: Int((d.distanceKm / d.paceKmH * 3600).rounded()))
    }

    /// 距離とペースから時間(秒)。ゼロ・数でないときは nil(除算エラーにしない)
    static func timeSeconds(distanceKm: Double, paceKmH: Double) -> Int? {
        guard distanceKm.isFinite, paceKmH.isFinite, distanceKm > 0, paceKmH > 0 else { return nil }
        return Int((distanceKm / paceKmH * 3600).rounded())
    }

    /// 距離と時間からペース(km/h)。ゼロ・数でないときは nil
    static func paceKmH(distanceKm: Double, timeSeconds: Int) -> Double? {
        guard distanceKm.isFinite, distanceKm > 0, timeSeconds > 0 else { return nil }
        return distanceKm / (Double(timeSeconds) / 3600)
    }

    /// ペースを保って時間を合わせる。時間が範囲を外れたら時間を端へ寄せ、ペースを合わせ直す
    static func keepingPace(distanceKm: Double, paceKmH: Double) -> RunTargetSettings {
        let distance = distanceRange.clamped(distanceKm)
        let pace = paceRange.clamped(paceKmH)
        guard let time = Self.timeSeconds(distanceKm: distance, paceKmH: pace) else {
            return fallback
        }
        let clampedTime = Int(timeRange.clamped(Double(time)))
        if clampedTime == time {
            return RunTargetSettings(paceKmH: pace, distanceKm: distance, timeSeconds: time)
        }
        let adjustedPace = paceRange.clamped(Self.paceKmH(distanceKm: distance, timeSeconds: clampedTime) ?? pace)
        return RunTargetSettings(paceKmH: adjustedPace, distanceKm: distance, timeSeconds: clampedTime)
    }

    /// 時間を保ってペースを合わせる。ペースが範囲を外れたらペースを端へ寄せ、時間を合わせ直す
    static func keepingTime(distanceKm: Double, timeSeconds: Int) -> RunTargetSettings {
        let distance = distanceRange.clamped(distanceKm)
        let time = Int(timeRange.clamped(Double(timeSeconds)))
        guard let pace = Self.paceKmH(distanceKm: distance, timeSeconds: time) else {
            return fallback
        }
        let clampedPace = paceRange.clamped(pace)
        if clampedPace == pace {
            return RunTargetSettings(paceKmH: pace, distanceKm: distance, timeSeconds: time)
        }
        return keepingPace(distanceKm: distance, paceKmH: clampedPace)
    }

    // MARK: ＋/− を押せるか(相手の値が範囲の端にあると、押しても戻されるだけなので押せなくする)

    /// 時間を延ばす = ペースが遅くなる
    static func canIncreaseTime(_ s: RunTargetSettings) -> Bool {
        timeRange.canIncrement(Double(s.timeSeconds)) && paceRange.canDecrement(s.paceKmH)
    }

    static func canDecreaseTime(_ s: RunTargetSettings) -> Bool {
        timeRange.canDecrement(Double(s.timeSeconds)) && paceRange.canIncrement(s.paceKmH)
    }

    /// 距離を延ばす = (ペースを保つので)時間が延びる
    static func canIncreaseDistance(_ s: RunTargetSettings) -> Bool {
        distanceRange.canIncrement(s.distanceKm) && timeRange.canIncrement(Double(s.timeSeconds))
    }

    static func canDecreaseDistance(_ s: RunTargetSettings) -> Bool {
        distanceRange.canDecrement(s.distanceKm) && timeRange.canDecrement(Double(s.timeSeconds))
    }

    /// ペースを上げる = 時間が縮む
    static func canIncreasePace(_ s: RunTargetSettings) -> Bool {
        paceRange.canIncrement(s.paceKmH) && timeRange.canDecrement(Double(s.timeSeconds))
    }

    static func canDecreasePace(_ s: RunTargetSettings) -> Bool {
        paceRange.canDecrement(s.paceKmH) && timeRange.canIncrement(Double(s.timeSeconds))
    }
}

// MARK: - ルート選択(S8)へ進む前の最終チェック

/// タスク表 S5「画面遷移(S8 進む操作)時のバリデーション」。入力の範囲(RunSettingRanges)には収まっていても、
/// ペーサーとして走れない値(1km 30分の歩き・世界記録より速い)では先へ進めず、ダイアログで知らせる
nonisolated enum RunTargetIssue: Equatable {
    /// 設定値が小さすぎる(ペースが遅すぎる)
    case paceTooSlow
    /// 設定値が大きすぎる(ペースが速すぎる)
    case paceTooFast
    /// 3値のつじつまが合っていない(通常は起きない — 相互依存で常に合わせている)
    case inconsistent

    /// 走れるペースの範囲(km/h)。3km/h = 1km 20分(ゆっくり歩く)、25km/h = 1km 2分24秒(世界記録級)
    static let minimumPaceKmH = 3.0
    static let maximumPaceKmH = 25.0

    /// 時間と「距離÷ペース」のずれの許容(秒)。時間は整数秒に丸めているので1秒まで
    static let consistencyToleranceSeconds = 1.0

    static func check(_ s: RunTargetSettings) -> RunTargetIssue? {
        guard s.paceKmH.isFinite, s.distanceKm.isFinite, s.paceKmH > 0, s.distanceKm > 0, s.timeSeconds > 0
        else { return .inconsistent }
        if s.paceKmH < minimumPaceKmH { return .paceTooSlow }
        if s.paceKmH > maximumPaceKmH { return .paceTooFast }
        let expected = s.distanceKm / s.paceKmH * 3600
        if abs(expected - Double(s.timeSeconds)) > consistencyToleranceSeconds { return .inconsistent }
        return nil
    }
}
