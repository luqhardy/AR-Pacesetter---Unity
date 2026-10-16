import XCTest
@testable import ARRunnerAppLogic

/// 位置情報が使えない理由(LocationProblem)。拒否・制限・サービスオフで案内を分ける(タスク表 位置情報許可の異常系)
final class LocationProblemTests: XCTestCase {

    func testServicesOffComesFirst() {
        // サービスがオフなら、アプリへの許可がどうであれ測位できない
        for auth in [LocationAuthorization.notDetermined, .restricted, .denied, .authorized] {
            XCTAssertEqual(LocationProblem.resolve(servicesEnabled: false, authorization: auth), .servicesOff)
        }
    }

    func testRestrictedAndDeniedAreDifferent() {
        XCTAssertEqual(LocationProblem.resolve(servicesEnabled: true, authorization: .restricted), .restricted)
        XCTAssertEqual(LocationProblem.resolve(servicesEnabled: true, authorization: .denied), .denied)
    }

    func testNoProblemWhenAllowedOrNotYetAsked() {
        XCTAssertNil(LocationProblem.resolve(servicesEnabled: true, authorization: .authorized))
        XCTAssertNil(LocationProblem.resolve(servicesEnabled: true, authorization: .notDetermined), "これから許可を求める")
    }

    func testOnlyRestrictedCannotBeFixedByTheUser() {
        XCTAssertFalse(LocationProblem.restricted.userCanFix, "制限は本人には変えられない — 設定を開くを出さない")
        XCTAssertTrue(LocationProblem.denied.userCanFix)
        XCTAssertTrue(LocationProblem.servicesOff.userCanFix)
    }
}
