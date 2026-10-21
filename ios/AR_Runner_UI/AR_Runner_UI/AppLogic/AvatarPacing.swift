import Foundation

// MARK: - アバターの走り方(タスク表 ARグラス「指定した速さで走る」)
//
// 既定: アバターは常に「走者+3m」に付いていく(遅れても離れず、色とオーラで知らせる — AGENTS.md §6)。
// 自走(既定 OFF・AR設定のスイッチ): アバターは目標ペースで走ったときの位置にいる。遅れると前へ離れ
// (9.5m まで — 走者を待って止まる10mには入らない)、速ければ近づく。Unity の StartSession の `selfPaced`。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum AvatarPacing {
    /// AR設定のスイッチ(既定 OFF)
    static let storageKey = "avatarSelfPaced"

    /// 次の走行を自走モードで始めるか。保存が無ければ OFF(既定の「走者+3m」)
    static func isSelfPaced(in store: UserDefaults) -> Bool {
        store.bool(forKey: storageKey)
    }
}
