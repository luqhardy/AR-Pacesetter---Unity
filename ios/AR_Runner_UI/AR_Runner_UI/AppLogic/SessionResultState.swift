import Foundation

// MARK: - ラン結果(S7)に出せるものがあるか
//
// 結果は走行終了後に Unity から `SessionEnded` で届く。以前は届く前・届かなかったときに
// **作り物の数字**(シンクロ率87%・2.13km・11:24)を出し、前回の走行の結果が残っていれば
// それを今回の結果として出していた(2026-10-13 修正: 開始時に前回分を消し、無ければ「-」)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum SessionResultState: Equatable {
    /// 結果が届いている
    case ready
    /// 終了直後で、Unity の集計を待っている
    case waiting
    /// 待っても届かなかった(Unity が記録を作れなかった・未リンクのシミュレータ等)
    case unavailable

    /// これ以上待っても届かないとみなす秒数。通常は1秒以内に届く
    static let waitSeconds: TimeInterval = 8

    static func resolve(hasResult: Bool, secondsSinceEnd: TimeInterval) -> SessionResultState {
        if hasResult { return .ready }
        return secondsSinceEnd < waitSeconds ? .waiting : .unavailable
    }
}
