import Foundation

// MARK: - ラン結果(S7)の数字の書式
//
// タスク表 S7 異常系「極端な測定値(超短距離・超長時間・GPS飛び)でも UI 描画が破綻しない」。
// 以前は時間を "分:秒" だけで出しており、100時間なら "6000:00"、0.01km を 100時間かけた
// 平均ペースは "600000'00"" になってカードからはみ出していた。
// 値が無い・数でないときは "-"(作り物の数字を出さない)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum RunResultFormat {
    /// 平均ペースとして意味を持つ距離の下限(km)。これ未満はペースを出さない(GPS の揺れだけの距離)
    static let minimumDistanceForPaceKm = 0.01

    /// 表示する平均ペースの上限(秒/km)。これより遅い(99分59秒/km 超)は桁が溢れるので上限で止める
    static let maximumPaceSecondsPerKm = 99 * 60 + 59

    /// 距離(km)。100km 以上は小数1桁、1000km 以上は整数(GPS の飛びで桁が増えてもはみ出さない)
    static func distanceKm(_ km: Double?) -> String {
        guard let km, km.isFinite, km >= 0 else { return "-" }
        if km >= 1000 { return String(format: "%.0f", km) }
        if km >= 100 { return String(format: "%.1f", km) }
        return String(format: "%.2f", km)
    }

    /// 時間。1時間未満は "分:秒"、それ以上は "時:分:秒"
    static func duration(_ seconds: Double?) -> String {
        guard let seconds, seconds.isFinite, seconds >= 0 else { return "-" }
        let total = Int(seconds.rounded(.down))
        let h = total / 3600, m = (total % 3600) / 60, s = total % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, s) : String(format: "%02d:%02d", m, s)
    }

    /// 平均ペース "5'21""。距離がほぼ0・数でないときは "-"、遅すぎるときは上限の "99'59""
    static func pace(distanceKm: Double?, elapsedSeconds: Double?) -> String {
        guard let distanceKm, let elapsedSeconds,
              distanceKm.isFinite, elapsedSeconds.isFinite,
              distanceKm >= minimumDistanceForPaceKm, elapsedSeconds > 0 else { return "-" }
        let perKm = elapsedSeconds / distanceKm
        // 秒で丸めてから分に分ける(分の小数から秒を出すと 5'21" が誤差で 5'20" になる)
        let secondsPerKm = perKm >= Double(maximumPaceSecondsPerKm) ? maximumPaceSecondsPerKm : Int(perKm.rounded())
        return String(format: "%d'%02d\"", secondsPerKm / 60, secondsPerKm % 60)
    }

    /// 消費カロリー(kcal)。0以下・数でないときは "-"
    static func calories(_ kcal: Double?) -> String {
        guard let kcal, kcal.isFinite, kcal > 0 else { return "-" }
        return String(Int(kcal.rounded()))
    }

    /// 疲労度(§4.3 の累積指数)。届いていない(古い Unity)ときは "-"
    static func fatigue(_ index: Double?) -> String {
        guard let index, index.isFinite, index >= 0 else { return "-" }
        return String(format: "%.1f", index)
    }
}
