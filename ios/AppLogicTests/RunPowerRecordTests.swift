import XCTest
@testable import ARRunnerAppLogic

/// 走行中の電池・発熱(RunPowerRecord)。§10「連続稼働60分・電池30%以上」の判定に使う
final class RunPowerRecordTests: XCTestCase {

    private let t0 = Date(timeIntervalSince1970: 1_760_400_000)

    private func sample(minutes: Double, battery: Double?, thermal: ThermalLevel = .nominal) -> PowerSample {
        PowerSample(at: t0.addingTimeInterval(minutes * 60), batteryLevel: battery, thermal: thermal, lowPowerMode: false)
    }

    private func run(from start: Double?, to end: Double?, minutes: Double) -> RunPowerRecord {
        var r = RunPowerRecord(start: sample(minutes: 0, battery: start))
        r.finish(with: sample(minutes: minutes, battery: end))
        return r
    }

    func testSixtyMinuteRunUsesTheMeasuredEndLevel() {
        let r = run(from: 0.95, to: 0.55, minutes: 61)
        XCTAssertEqual(r.batteryDropPercent!, 40, accuracy: 1e-9)
        XCTAssertEqual(r.percentAt60Minutes!, 55, accuracy: 1e-9)
        XCTAssertEqual(r.meetsSixtyMinuteSpec, true)
    }

    func testShorterRunIsProjectedAtTheSameRate() {
        // 20分で 90%→80%(10ポイント)→ 60分なら 30ポイント減って 60%
        let r = run(from: 0.90, to: 0.80, minutes: 20)
        XCTAssertEqual(r.percentAt60Minutes!, 60, accuracy: 1e-9)
        XCTAssertEqual(r.meetsSixtyMinuteSpec, true)
    }

    func testProjectionBelowThirtyPercentFailsTheSpec() {
        // 15分で 50%→40% → 60分なら 10% しか残らない
        let r = run(from: 0.50, to: 0.40, minutes: 15)
        XCTAssertEqual(r.percentAt60Minutes!, 10, accuracy: 1e-9)
        XCTAssertEqual(r.meetsSixtyMinuteSpec, false)
    }

    func testProjectionNeverGoesBelowZero() {
        let r = run(from: 0.30, to: 0.10, minutes: 10)
        XCTAssertEqual(r.percentAt60Minutes, 0)
    }

    func testNoJudgementForShortRunsUnknownBatteryOrCharging() {
        XCTAssertNil(run(from: 0.9, to: 0.85, minutes: 5).percentAt60Minutes, "短すぎる(電池は5%刻み)")
        XCTAssertNil(run(from: nil, to: nil, minutes: 30).percentAt60Minutes, "電池が取れない")
        XCTAssertNil(run(from: 0.5, to: 0.7, minutes: 30).percentAt60Minutes, "充電で増えた")
        XCTAssertNil(run(from: 0.5, to: 0.7, minutes: 30).meetsSixtyMinuteSpec)
    }

    func testRunInProgressHasNoEndValues() {
        let r = RunPowerRecord(start: sample(minutes: 0, battery: 0.9))
        XCTAssertNil(r.durationMinutes)
        XCTAssertNil(r.batteryDropPercent)
        XCTAssertNil(r.percentAt60Minutes)
    }

    func testPeakThermalKeepsTheHighestLevel() {
        var r = RunPowerRecord(start: sample(minutes: 0, battery: 0.9, thermal: .fair))
        r.observe(thermal: .serious)
        r.observe(thermal: .nominal)
        r.finish(with: sample(minutes: 30, battery: 0.8, thermal: .fair))
        XCTAssertEqual(r.peakThermal, .serious, "下がっても走行中の最大を残す")
    }

    func testSurvivesAnAppRestart() throws {
        var r = RunPowerRecord(start: sample(minutes: 0, battery: 0.9))
        r.observe(thermal: .serious)
        let decoded = try JSONDecoder().decode(RunPowerRecord.self, from: JSONEncoder().encode(r))
        XCTAssertEqual(decoded, r)
    }
    // MARK: 走行中の電池の警告(タスク表 S6)

    private func sample(battery: Double?, lowPower: Bool) -> PowerSample {
        PowerSample(at: Date(timeIntervalSince1970: 0), batteryLevel: battery, thermal: .nominal, lowPowerMode: lowPower)
    }

    func testNoWarningWhenPowerIsFine() {
        XCTAssertNil(RunPowerWarning.resolve(sample(battery: 0.8, lowPower: false)))
        XCTAssertNil(RunPowerWarning.resolve(sample(battery: nil, lowPower: false)), "電池が読めない(シミュレータ)")
    }

    func testLowBatteryIsWarnedWithThePercentage() {
        XCTAssertEqual(RunPowerWarning.resolve(sample(battery: 0.15, lowPower: false)), .lowBattery(percent: 15))
        XCTAssertEqual(RunPowerWarning.resolve(sample(battery: 0.20, lowPower: false)), .lowBattery(percent: 20))
    }

    func testLowBatteryWinsOverLowPowerMode() {
        XCTAssertEqual(RunPowerWarning.resolve(sample(battery: 0.10, lowPower: true)), .lowBattery(percent: 10))
        XCTAssertEqual(RunPowerWarning.resolve(sample(battery: 0.60, lowPower: true)), .lowPowerMode)
    }
}
