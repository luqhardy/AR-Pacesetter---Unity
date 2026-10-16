import XCTest
@testable import ARRunnerAppLogic

/// 実地テストの書き出し(FieldTestReport)。TEMPLATE.md に貼れる Markdown になること・数字を作らないこと
final class FieldTestReportTests: XCTestCase {

    private let utc = TimeZone(identifier: "UTC")!
    private let t0 = Date(timeIntervalSince1970: 1_760_400_000)   // 2025-10-14 00:00:00 UTC

    private func report(latency: FieldTestReport.Latency = .init(lastMs: 14.2, maxMs: 31, overBudgetRatio: 0.031, sampleCount: 1234),
                        events: [AvatarVisibilityEvent] = [],
                        diagnostics: [(key: String, value: String)] = [("m2p.native", "true")]) -> String {
        FieldTestReport(
            generatedAt: t0,
            device: .init(model: "iPhone16,2", systemVersion: "26.0", appVersion: "1.0 (12)"),
            glassesStatus: "接続済み",
            latency: latency,
            visibilityEvents: events,
            diagnostics: diagnostics
        ).markdown(timeZone: utc)
    }

    func testHeaderAndDeviceRows() {
        let md = report()
        XCTAssertTrue(md.hasPrefix("## 開発者モードからの書き出し(2025-10-14 00:00)"))
        XCTAssertTrue(md.contains("| iPhone(機種・iOS) | iPhone16,2 / iOS 26.0 |"))
        XCTAssertTrue(md.contains("| アプリ(版) | 1.0 (12) |"))
        XCTAssertTrue(md.contains("| ARグラス | 接続済み |"))
    }

    func testLatencyRowUsesMeasuredValues() {
        XCTAssertTrue(report().contains("| 14.2ms | 31.0ms | 3.1% | 1234 |"))
    }

    func testUnmeasuredLatencyIsNotTurnedIntoANumber() {
        let md = report(latency: .init(lastMs: -1, maxMs: -1, overBudgetRatio: -1, sampleCount: 0))
        XCTAssertTrue(md.contains("| 未計測 | 未計測 | 未計測 | 0 |"))
    }

    func testVisibilityHistoryIsOldestFirstWithOpenEvents() {
        // AvatarVisibilityLog は新しい順で持つ。表は時系列(古い順)で読む
        let older = AvatarVisibilityEvent(id: UUID(), hiddenAt: t0, shownAt: t0.addingTimeInterval(2.4), reason: "GPSロスト")
        let newer = AvatarVisibilityEvent(id: UUID(), hiddenAt: t0.addingTimeInterval(60), shownAt: nil, reason: "スタンバイ")
        let md = report(events: [newer, older])
        let olderLine = "| 10/14 00:00:00 | 2.4 | GPSロスト |"
        let newerLine = "| 10/14 00:01:00 | 継続中 | スタンバイ |"
        XCTAssertTrue(md.contains(olderLine))
        XCTAssertTrue(md.contains(newerLine))
        XCTAssertLessThan(md.range(of: olderLine)!.lowerBound, md.range(of: newerLine)!.lowerBound)
    }

    func testEmptyHistoryAndNoUnityResponse() {
        let md = report(diagnostics: [])
        XCTAssertTrue(md.contains("| (なし) | | |"))
        XCTAssertTrue(md.contains("Unity から応答なし"))
    }

    func testPowerSectionShowsTheRunAndTheSixtyMinuteJudgement() {
        var run = RunPowerRecord(start: PowerSample(at: t0, batteryLevel: 0.9, thermal: .nominal, lowPowerMode: false))
        run.observe(thermal: .serious)
        run.finish(with: PowerSample(at: t0.addingTimeInterval(20 * 60), batteryLevel: 0.8, thermal: .fair, lowPowerMode: false))
        let md = FieldTestReport(
            generatedAt: t0, device: .init(model: "iPhone16,2", systemVersion: "26.0", appVersion: "1.0 (12)"),
            glassesStatus: "未接続", latency: .init(lastMs: -1, maxMs: -1, overBudgetRatio: -1, sampleCount: 0),
            visibilityEvents: [], diagnostics: [],
            power: run,
            currentPower: PowerSample(at: t0, batteryLevel: 0.75, thermal: .fair, lowPowerMode: true)
        ).markdown(timeZone: utc)

        XCTAssertTrue(md.contains("### 電池・発熱(§10 連続稼働60分・電池30%以上)"))
        XCTAssertTrue(md.contains("| 電池 | 90% | 80% |"))
        XCTAssertTrue(md.contains("走行 20.0分・電池の減り 10%・走行中の最大の発熱: 高い(処理を絞り始める)"))
        XCTAssertTrue(md.contains("60分走ったときの電池: 60%(同じ減り方での見込み)→ 基準 30% 以上を満たす"))
        XCTAssertTrue(md.contains("書き出し時点: 電池 75%・発熱 やや高い・低電力モード オン"))
    }

    func testNoRunYetSaysSo() {
        XCTAssertTrue(report().contains("(走行の記録なし)"))
    }

    func testPipesAndNewlinesDoNotBreakTheTable() {
        let md = report(diagnostics: [("glass.fit", "144cm | 3.0m\n足元OK")])
        XCTAssertTrue(md.contains("| glass.fit | 144cm \\| 3.0m 足元OK |"))
    }
}
