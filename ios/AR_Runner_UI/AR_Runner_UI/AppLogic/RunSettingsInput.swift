import Foundation

// MARK: - ランニング設定(S5)の入力
//
// 時間・距離・ペースの直接入力と ＋/− の判断。仕様(タスク表 S5):
//   - 入力は半角・全角のどちらでも受け付ける — 以前は全角の「５」が黙って捨てられていた
//   - ＋/− は境界値に達したら押せなくする — 以前は押せるまま値だけが止まっていた
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum NumericInput {
    /// 全角の数字・記号を半角へ、前後の空白(全角スペース含む)を除く。
    /// 日本語キーボードやコピー&ペーストで全角が混ざっても同じ値として読む
    static func normalized(_ text: String) -> String {
        var out = ""
        for scalar in text.unicodeScalars {
            switch scalar.value {
            case 0xFF10...0xFF19:                           // ０〜９
                out.unicodeScalars.append(Unicode.Scalar(scalar.value - 0xFF10 + 0x30)!)
            case 0xFF0E, 0x3002: out.append(".")            // ． 。
            case 0xFF1A:         out.append(":")            // ：
            case 0x3000:         out.append(" ")            // 全角スペース
            default:             out.unicodeScalars.append(scalar)
            }
        }
        return out.trimmingCharacters(in: .whitespaces)
    }

    /// 0より大きい小数(距離 km・ペース km/h)。読めなければ nil
    static func parseDecimal(_ text: String) -> Double? {
        let s = normalized(text)
        guard !s.isEmpty, let value = Double(s), value.isFinite, value > 0 else { return nil }
        return value
    }

    /// 時間(秒)。"時:分:秒" / "分:秒" / "分" を受ける。読めなければ nil
    static func parseDuration(_ text: String) -> Int? {
        let s = normalized(text)
        guard !s.isEmpty else { return nil }
        let parts = s.split(separator: ":", omittingEmptySubsequences: false)
        guard (1...3).contains(parts.count) else { return nil }
        var values: [Int] = []
        for part in parts {
            guard let v = Int(part.trimmingCharacters(in: .whitespaces)), v >= 0 else { return nil }
            values.append(v)
        }
        switch values.count {
        case 3: return values[0] * 3600 + values[1] * 60 + values[2]
        case 2: return values[0] * 60 + values[1]
        default: return values[0] * 60
        }
    }
}

/// 設定値の範囲と ＋/− の刻み
nonisolated struct SettingRange: Equatable {
    let minimum: Double
    let maximum: Double
    let step: Double
    /// ＋/− の結果を丸める小数桁(浮動小数の誤差で 9.999999 にならないように)
    let decimals: Int

    func clamped(_ value: Double) -> Double { min(maximum, max(minimum, value)) }

    /// 下限に達していたら − は押せない
    func canDecrement(_ value: Double) -> Bool { value > minimum + tolerance }
    /// 上限に達していたら ＋ は押せない
    func canIncrement(_ value: Double) -> Bool { value < maximum - tolerance }

    func decremented(_ value: Double) -> Double { clamped(rounded(value - step)) }
    func incremented(_ value: Double) -> Double { clamped(rounded(value + step)) }

    private var tolerance: Double { pow(10, -Double(decimals)) / 2 }

    private func rounded(_ value: Double) -> Double {
        let scale = pow(10, Double(decimals))
        return (value * scale).rounded() / scale
    }
}

/// ランニング設定の3値の範囲(画面の ＋/− と直接入力が共有する)
nonisolated enum RunSettingRanges {
    /// 時間(秒): 1分〜24時間、1分刻み
    static let timeSeconds = SettingRange(minimum: 60, maximum: 86_400, step: 60, decimals: 0)
    /// 距離(km): 0.5〜200km、0.5km刻み
    static let distanceKm = SettingRange(minimum: 0.5, maximum: 200, step: 0.5, decimals: 2)
    /// ペース(km/h): 1〜30km/h、0.5km/h刻み
    static let paceKmH = SettingRange(minimum: 1, maximum: 30, step: 0.5, decimals: 1)
}
