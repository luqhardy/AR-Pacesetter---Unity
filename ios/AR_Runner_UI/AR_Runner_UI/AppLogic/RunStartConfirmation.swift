import Foundation

/// Opening the AR view prepares the preview; only an explicit tap starts the countdown.
nonisolated enum RunStartConfirmation: Equatable {
    case preparing
    case ready
    case started
    case cancelled

    mutating func preparationFinished() {
        guard self == .preparing else { return }
        self = .ready
    }

    /// Consume the tap once so repeated taps cannot send multiple StartSession commands.
    mutating func confirmStart() -> Bool {
        guard self == .ready else { return false }
        self = .started
        return true
    }

    mutating func cancel() {
        guard self != .started else { return }
        self = .cancelled
    }
}
