import XCTest
@testable import ARRunnerAppLogic

/// アバターの走り方(タスク表「指定した速さで走る」)— 既定は「走者+3m」
final class AvatarPacingTests: XCTestCase {

    private var store: UserDefaults!
    private var suiteName = ""

    override func setUp() {
        super.setUp()
        suiteName = "AvatarPacingTests-\(UUID().uuidString)"
        store = UserDefaults(suiteName: suiteName)
    }

    override func tearDown() {
        store.removePersistentDomain(forName: suiteName)
        store = nil
        super.tearDown()
    }

    func testOffByDefault() {
        XCTAssertFalse(AvatarPacing.isSelfPaced(in: store), "既定は走者+3m")
    }

    func testTheSwitchIsRemembered() {
        store.set(true, forKey: AvatarPacing.storageKey)
        XCTAssertTrue(AvatarPacing.isSelfPaced(in: store))
        store.set(false, forKey: AvatarPacing.storageKey)
        XCTAssertFalse(AvatarPacing.isSelfPaced(in: store))
    }
}
