import Foundation

// MARK: - 記録中の強制終了(タスク表 S3 異常系)
//
// 走行中にアプリが落ちた(クラッシュ・電池切れ・タスクキル)直後の起動では、ホームの「前回の記録」に
// 1つ前の(正常に終わった)走行が出ていた — 実際の前回はその後の途中で終わった走行なのに。
// 走行の開始で印を付け、正常な終了で外す。印が残ったまま起動したら、前回は途中で終わったとみなして
// 「前回の記録」を「-」にする。途中の記録は Unity が控え(中断スナップショット)から次に Unity が起動したとき
// (次の走行の開始時)に履歴へ復元する。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum RunInterruption {
    static let storageKey = "runInProgressSince"

    /// 走行の開始(ARSessionManager.start)
    static func markStarted(in store: UserDefaults, at date: Date = Date()) {
        store.set(date.timeIntervalSince1970, forKey: storageKey)
    }

    /// 走行の正常な終了(ARSessionManager.end / endLocally)
    static func markEnded(in store: UserDefaults) {
        store.removeObject(forKey: storageKey)
    }

    /// 前回の走行が途中で終わった(印が残っている)か。今まさに走行中なら途中ではない
    static func wasInterrupted(in store: UserDefaults, sessionActive: Bool) -> Bool {
        !sessionActive && store.object(forKey: storageKey) != nil
    }
}
