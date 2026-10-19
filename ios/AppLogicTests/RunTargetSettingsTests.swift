import XCTest
@testable import ARRunnerAppLogic

/// ランニング設定の確定値の保存(RunTargetSettings)。アプリを閉じても同じ値から始まること
final class RunTargetSettingsTests: XCTestCase {

    private var store: UserDefaults!
    private var suiteName = ""

    override func setUp() {
        super.setUp()
        suiteName = "RunTargetSettingsTests-\(UUID().uuidString)"
        store = UserDefaults(suiteName: suiteName)
    }

    override func tearDown() {
        store.removePersistentDomain(forName: suiteName)
        store = nil
        super.tearDown()
    }

    func testFirstLaunchUsesTheDefaults() {
        XCTAssertEqual(RunTargetSettings.load(from: store), .defaults)
    }

    func testConfirmedValuesSurviveARestart() {
        let confirmed = RunTargetSettings(paceKmH: 11.5, distanceKm: 5.0, timeSeconds: 1800)
        confirmed.save(to: store)
        XCTAssertEqual(RunTargetSettings.load(from: store), confirmed)
    }

    func testBrokenSaveFallsBackToTheDefaults() {
        store.set(Data("not json".utf8), forKey: RunTargetSettings.storageKey)
        XCTAssertEqual(RunTargetSettings.load(from: store), .defaults)
    }

    func testOutOfRangeValuesAreBroughtIntoRange() {
        let odd = RunTargetSettings(paceKmH: 99, distanceKm: 0.1, timeSeconds: 5).sanitized()
        XCTAssertEqual(odd.paceKmH, RunSettingRanges.paceKmH.maximum)
        XCTAssertEqual(odd.distanceKm, RunSettingRanges.distanceKm.minimum)
        XCTAssertEqual(odd.timeSeconds, Int(RunSettingRanges.timeSeconds.minimum))
    }

    func testNotANumberBecomesTheDefault() {
        let broken = RunTargetSettings(paceKmH: .nan, distanceKm: .infinity, timeSeconds: 3600).sanitized()
        XCTAssertEqual(broken.paceKmH, RunTargetSettings.defaults.paceKmH)
        XCTAssertEqual(broken.distanceKm, RunTargetSettings.defaults.distanceKm)
    }

    func testSavingNotANumberDoesNotLoseTheSave() {
        // JSONEncoder は NaN を書けない — 保存前に補正するので、保存自体は残る
        RunTargetSettings(paceKmH: .nan, distanceKm: 3, timeSeconds: 900).save(to: store)
        let loaded = RunTargetSettings.load(from: store)
        XCTAssertEqual(loaded.distanceKm, 3)
        XCTAssertEqual(loaded.paceKmH, RunTargetSettings.defaults.paceKmH)
    }
}
