import Foundation

// MARK: - 実地テストの書き出し
//
// 開発者モードの「レポートをコピー」が作る Markdown。Docs/field-tests/TEMPLATE.md の
// 「開発者モードからの書き出し」にそのまま貼れる形にする。
// 以前は端末・M2P・アバター非表示の履歴・Unity の状態を画面から手で書き写していた(T1〜T9 の記録の手間)。
//
// 数字は作らない — 未計測(-1)は「未計測」と書く(開発者モードと同じ方針)。
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated struct FieldTestReport {
    struct Device {
        var model: String          // 例 "iPhone16,2"
        var systemVersion: String  // 例 "26.0"
        var appVersion: String     // 例 "1.0 (12)"
    }

    /// M2P(§10)。値が負なら未計測
    struct Latency {
        var lastMs: Double
        var maxMs: Double
        var overBudgetRatio: Double
        var sampleCount: Int
    }

    var generatedAt: Date
    var device: Device
    var glassesStatus: String
    var latency: Latency
    var visibilityEvents: [AvatarVisibilityEvent]   // 新しい順(AvatarVisibilityLog のまま)
    var diagnostics: [(key: String, value: String)]
    /// 直近の走行の電池・発熱(§10)。一度も走っていなければ nil
    var power: RunPowerRecord? = nil
    /// 書き出した時点の電池・発熱
    var currentPower: PowerSample? = nil

    func markdown(timeZone: TimeZone = .current) -> String {
        var lines: [String] = []
        lines.append("## 開発者モードからの書き出し(\(Self.format(generatedAt, "yyyy-MM-dd HH:mm", timeZone)))")
        lines.append("")
        lines.append("| | |")
        lines.append("|---|---|")
        lines.append("| iPhone(機種・iOS) | \(Self.cell(device.model)) / iOS \(Self.cell(device.systemVersion)) |")
        lines.append("| アプリ(版) | \(Self.cell(device.appVersion)) |")
        lines.append("| ARグラス | \(Self.cell(glassesStatus)) |")
        lines.append("")
        lines.append("### M2P(§10)")
        lines.append("")
        lines.append("| 直近 | 区間最大 | 20ms 超過率 | サンプル |")
        lines.append("|---|---|---|---|")
        lines.append("| \(Self.ms(latency.lastMs)) | \(Self.ms(latency.maxMs)) | \(Self.ratio(latency.overBudgetRatio)) | \(latency.sampleCount) |")
        lines.append("")
        lines.append(contentsOf: powerSection(timeZone: timeZone))
        lines.append("### アバター非表示の履歴(古い順)")
        lines.append("")
        lines.append("| 時刻 | 見えなかった秒数 | 理由 |")
        lines.append("|---|---|---|")
        if visibilityEvents.isEmpty {
            lines.append("| (なし) | | |")
        } else {
            for event in visibilityEvents.reversed() {
                let duration = event.durationSeconds.map { String(format: "%.1f", $0) } ?? "継続中"
                lines.append("| \(Self.format(event.hiddenAt, "MM/dd HH:mm:ss", timeZone)) | \(duration) | \(Self.cell(event.reason)) |")
            }
        }
        lines.append("")
        lines.append("### 状態(Unity のスナップショット)")
        lines.append("")
        if diagnostics.isEmpty {
            lines.append("(Unity から応答なし — 走行画面を一度開いてから書き出す)")
        } else {
            lines.append("| キー | 値 |")
            lines.append("|---|---|")
            for row in diagnostics {
                lines.append("| \(Self.cell(row.key)) | \(Self.cell(row.value)) |")
            }
        }
        return lines.joined(separator: "\n") + "\n"
    }

    /// §10 連続稼働60分・電池30%以上
    private func powerSection(timeZone: TimeZone) -> [String] {
        var lines = ["### 電池・発熱(§10 連続稼働60分・電池30%以上)", ""]
        if let run = power {
            lines.append("| | 開始 | 終了 |")
            lines.append("|---|---|---|")
            lines.append("| 時刻 | \(Self.format(run.start.at, "MM/dd HH:mm:ss", timeZone)) | \(run.end.map { Self.format($0.at, "MM/dd HH:mm:ss", timeZone) } ?? "走行中") |")
            lines.append("| 電池 | \(Self.battery(run.start.batteryLevel)) | \(run.end.map { Self.battery($0.batteryLevel) } ?? "—") |")
            lines.append("| 発熱 | \(run.start.thermal.reportLabel) | \(run.end?.thermal.reportLabel ?? "—") |")
            lines.append("| 低電力モード | \(Self.onOff(run.start.lowPowerMode)) | \(run.end.map { Self.onOff($0.lowPowerMode) } ?? "—") |")
            lines.append("")
            if let minutes = run.durationMinutes {
                let drop = run.batteryDropPercent.map { String(format: "%.0f%%", $0) } ?? "不明"
                lines.append("走行 \(String(format: "%.1f", minutes))分・電池の減り \(drop)・走行中の最大の発熱: \(run.peakThermal.reportLabel)")
                if let at60 = run.percentAt60Minutes {
                    let kind = minutes >= 60 ? "実測" : "同じ減り方での見込み"
                    let verdict = (run.meetsSixtyMinuteSpec ?? false) ? "満たす" : "満たさない"
                    lines.append("60分走ったときの電池: \(String(format: "%.0f%%", at60))(\(kind))→ 基準 \(Int(RunPowerRecord.minimumPercentAfter60Minutes))% 以上を\(verdict)")
                } else {
                    lines.append("60分後の電池は見積もれない(電池が取れない・\(Int(RunPowerRecord.minimumMinutesForProjection))分未満・充電中のいずれか)")
                }
            }
        } else {
            lines.append("(走行の記録なし)")
        }
        if let now = currentPower {
            lines.append("")
            lines.append("書き出し時点: 電池 \(Self.battery(now.batteryLevel))・発熱 \(now.thermal.reportLabel)・低電力モード \(Self.onOff(now.lowPowerMode))")
        }
        lines.append("")
        return lines
    }

    // MARK: 書式

    static func battery(_ level: Double?) -> String {
        level.map { String(format: "%.0f%%", $0 * 100) } ?? "不明"
    }

    static func onOff(_ on: Bool) -> String { on ? "オン" : "オフ" }

    /// 表のセル: 縦棒と改行で表が壊れないようにする
    static func cell(_ text: String) -> String {
        text.replacingOccurrences(of: "|", with: "\\|")
            .replacingOccurrences(of: "\r\n", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
    }

    static func ms(_ value: Double) -> String {
        value < 0 ? "未計測" : String(format: "%.1fms", value)
    }

    static func ratio(_ value: Double) -> String {
        value < 0 ? "未計測" : String(format: "%.1f%%", value * 100)
    }

    private static func format(_ date: Date, _ pattern: String, _ timeZone: TimeZone) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = timeZone
        f.dateFormat = pattern
        return f.string(from: date)
    }
}
