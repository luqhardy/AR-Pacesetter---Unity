import XCTest
@testable import ARRunnerAppLogic

/// ラン結果(S7)の表示状態。作り物の数字の代わりに「集計中」「結果なし」を出す
final class SessionResultStateTests: XCTestCase {

    func testResultShowsAsSoonAsItArrives() {
        XCTAssertEqual(SessionResultState.resolve(hasResult: true, secondsSinceEnd: 0), .ready)
        XCTAssertEqual(SessionResultState.resolve(hasResult: true, secondsSinceEnd: 60), .ready)
    }

    func testWaitsBrieflyForUnity() {
        XCTAssertEqual(SessionResultState.resolve(hasResult: false, secondsSinceEnd: 0), .waiting)
        XCTAssertEqual(SessionResultState.resolve(hasResult: false,
                                                  secondsSinceEnd: SessionResultState.waitSeconds - 0.1), .waiting)
    }

    func testGivesUpAfterTheWait() {
        XCTAssertEqual(SessionResultState.resolve(hasResult: false,
                                                  secondsSinceEnd: SessionResultState.waitSeconds), .unavailable)
    }
}
