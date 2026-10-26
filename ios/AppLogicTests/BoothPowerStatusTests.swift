import XCTest
@testable import ARRunnerAppLogic

/// 展示ブースの電池・発熱(Kobe Calling スタッフ画面)— 絞られる手前(fair)で注意を出す
final class BoothPowerStatusTests: XCTestCase {

    private func status(battery: Double?, thermal: ThermalLevel = .nominal, charging: Bool? = false) -> BoothPowerStatus {
        BoothPowerStatus(PowerSample(at: Date(timeIntervalSince1970: 0), batteryLevel: battery,
                                     thermal: thermal, lowPowerMode: false, charging: charging))
    }

    func testNormalShowsNoAdvice() {
        let s = status(battery: 0.85)
        XCTAssertEqual(s.level, .ok)
        XCTAssertNil(s.advice)
        XCTAssertEqual(s.batteryText, "電池 85%")
        XCTAssertEqual(s.thermalText, "温度 平常")
    }

    func testWarmingUpWarnsBeforeIosThrottles() {
        // fair はまだ絞られない段階 — ここで注意を出すのが「過熱の前に」
        let s = status(battery: 0.85, thermal: .fair)
        XCTAssertEqual(s.thermalLevel, .caution)
        XCTAssertNotNil(s.advice)
    }

    func testThrottlingIsDanger() {
        XCTAssertEqual(status(battery: 0.85, thermal: .serious).level, .danger)
        XCTAssertEqual(status(battery: 0.85, thermal: .critical).level, .danger)
        XCTAssertTrue(status(battery: 0.85, thermal: .critical).advice!.contains("止め"))
    }

    func testBatteryThresholds() {
        XCTAssertEqual(status(battery: 0.45).batteryLevel, .ok)
        XCTAssertEqual(status(battery: 0.40).batteryLevel, .caution)
        XCTAssertEqual(status(battery: 0.20).batteryLevel, .danger)
    }

    func testChargingIsNotABatteryProblem() {
        // パススルー給電で回すのが前提(チェックリスト C)
        let s = status(battery: 0.15, charging: true)
        XCTAssertEqual(s.batteryLevel, .ok)
        XCTAssertNil(s.advice)
        XCTAssertEqual(s.batteryText, "電池 15%(給電中)")
    }

    func testHeatAdviceWinsOverBattery() {
        let s = status(battery: 0.30, thermal: .serious)
        XCTAssertEqual(s.level, .danger)
        XCTAssertTrue(s.advice!.contains("熱"))
    }

    func testLowBatteryWinsOverMildWarmth() {
        let s = status(battery: 0.10, thermal: .fair)
        XCTAssertTrue(s.advice!.contains("電池"))
    }

    func testUnreadableBatteryIsNotAWarning() {
        // シミュレータでは電池が読めない
        let s = status(battery: nil, charging: nil)
        XCTAssertEqual(s.level, .ok)
        XCTAssertEqual(s.batteryText, "電池 —")
    }

    func testOldSavedSamplesWithoutChargingStillDecode() throws {
        // 直近の走行の記録(PowerMonitor)は charging が無い形で端末に保存されている
        let json = #"{"at":0,"batteryLevel":0.5,"thermal":1,"lowPowerMode":false}"#
        let sample = try JSONDecoder().decode(PowerSample.self, from: Data(json.utf8))
        XCTAssertNil(sample.charging)
    }
}
