import Foundation

// MARK: - ランニング設定(S5)の確定値の保存
//
// 「次へ」で確定した時間・距離・ペースを端末に保存し、アプリを終了しても次の起動で同じ値から始める。
// 以前は画面を戻るだけなら残ったが、アプリを閉じると 60分・10km・8km/h に戻っていた。
// 保存するのは確定値だけ — 入力途中の値は保存しない(タスク表 S5「強制終了時は前回の確定値」)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated struct RunTargetSettings: Codable, Equatable {
    var paceKmH: Double
    var distanceKm: Double
    var timeSeconds: Int

    /// 初回起動・保存が読めないときの値
    static let defaults = RunTargetSettings(paceKmH: 8.0, distanceKm: 10.0, timeSeconds: 3600)

    static let storageKey = "runTargetSettings"

    /// 範囲外は範囲内へ寄せ、数でない値(NaN 等)は既定値にする。
    /// 古い版や手で書き換えた保存値で、＋/− が押せない・Unity に変な値が渡る、を防ぐ
    func sanitized() -> RunTargetSettings {
        func fix(_ value: Double, _ range: SettingRange, _ fallback: Double) -> Double {
            value.isFinite ? range.clamped(value) : fallback
        }
        return RunTargetSettings(
            paceKmH: fix(paceKmH, RunSettingRanges.paceKmH, Self.defaults.paceKmH),
            distanceKm: fix(distanceKm, RunSettingRanges.distanceKm, Self.defaults.distanceKm),
            timeSeconds: Int(fix(Double(timeSeconds), RunSettingRanges.timeSeconds, Double(Self.defaults.timeSeconds)))
        )
    }

    /// 保存値を読む。無い・壊れていれば既定値
    static func load(from store: UserDefaults) -> RunTargetSettings {
        guard let data = store.data(forKey: storageKey),
              let saved = try? JSONDecoder().decode(RunTargetSettings.self, from: data)
        else { return defaults }
        return saved.sanitized()
    }

    func save(to store: UserDefaults) {
        if let data = try? JSONEncoder().encode(sanitized()) {
            store.set(data, forKey: Self.storageKey)
        }
    }
}
