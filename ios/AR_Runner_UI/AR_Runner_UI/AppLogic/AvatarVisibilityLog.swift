import Foundation

// MARK: - アバター非表示の履歴
//
// 走行中にアバターが消えた理由は、Unity(AvatarVisibilityDiagnostics)が走行画面の紫のバナーで出す。
// しかし走っている本人はバナーを読めず、「たまに消える」としか報告できなかった(2026-10)。
// 消えた/戻った時刻・継続時間・理由を残し、走行後に開発者モードで読めるようにする。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// アバターが見えなかった1区間。`shownAt` が nil なら今も見えていない
nonisolated struct AvatarVisibilityEvent: Codable, Equatable, Identifiable {
    let id: UUID
    let hiddenAt: Date
    var shownAt: Date?
    let reason: String

    /// 見えなかった秒数。まだ見えていなければ nil
    var durationSeconds: Double? { shownAt.map { $0.timeIntervalSince(hiddenAt) } }
}

nonisolated struct AvatarVisibilityLog: Codable, Equatable {
    /// 新しい順
    private(set) var events: [AvatarVisibilityEvent] = []

    /// 保持する件数の上限(古いものから捨てる)
    static let maxEvents = 100

    /// 今アバターが見えていない区間(あれば)
    var openEvent: AvatarVisibilityEvent? { events.first.flatMap { $0.shownAt == nil ? $0 : nil } }

    /// Unity からの可視状態の報告を記録する。
    /// - 見えなくなった → 1件追加
    /// - 見えるようになった → 開いている区間を閉じる
    /// - 見えないまま理由だけ変わった(例: GPSロストの慣性 → フェード → スタンバイ)→ 前の区間を閉じて新しく始める
    /// - Returns: 記録が変わったか(保存の要否)
    @discardableResult
    mutating func record(visible: Bool, reason: String, at date: Date) -> Bool {
        if visible {
            guard var open = openEvent else { return false }
            open.shownAt = date
            events[0] = open
            return true
        }

        if let open = openEvent {
            if open.reason == reason { return false }   // 同じ理由の再送は1件にまとめる
            var closed = open
            closed.shownAt = date
            events[0] = closed
        }
        events.insert(AvatarVisibilityEvent(id: UUID(), hiddenAt: date, shownAt: nil, reason: reason), at: 0)
        if events.count > Self.maxEvents { events.removeLast(events.count - Self.maxEvents) }
        return true
    }

    mutating func clear() { events.removeAll() }
}
