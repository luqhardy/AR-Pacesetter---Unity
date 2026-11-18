import XCTest
@testable import ARRunnerAppLogic

final class RunStartConfirmationTests: XCTestCase {
    func testLoadingAndReadyPreviewNeverStartTheSession() {
        var confirmation = RunStartConfirmation.preparing
        XCTAssertFalse(confirmation.confirmStart())
        confirmation.preparationFinished()
        XCTAssertEqual(confirmation, .ready)
        confirmation.preparationFinished()
        XCTAssertEqual(confirmation, .ready)
    }

    func testExplicitTapStartsOnlyOnce() {
        var confirmation = RunStartConfirmation.preparing
        confirmation.preparationFinished()
        XCTAssertTrue(confirmation.confirmStart())
        XCTAssertFalse(confirmation.confirmStart())
        confirmation.preparationFinished()
        XCTAssertEqual(confirmation, .started)
    }

    func testLeavingDuringPreparationIgnoresLateCompletion() {
        var confirmation = RunStartConfirmation.preparing
        confirmation.cancel()
        confirmation.preparationFinished()
        XCTAssertEqual(confirmation, .cancelled)
        XCTAssertFalse(confirmation.confirmStart())
    }

    func testLeavingReadyPreviewDoesNotStartARun() {
        var confirmation = RunStartConfirmation.ready
        confirmation.cancel()
        XCTAssertFalse(confirmation.confirmStart())
    }

    func testExistingSessionCannotBeStartedAgain() {
        var confirmation = RunStartConfirmation.started
        confirmation.cancel()
        confirmation.preparationFinished()
        XCTAssertEqual(confirmation, .started)
        XCTAssertFalse(confirmation.confirmStart())
    }
}
