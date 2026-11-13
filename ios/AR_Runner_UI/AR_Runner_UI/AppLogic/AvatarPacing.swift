import Foundation

// MARK: - アバターの走り方(タスク表 ARグラス「指定した速さで走る」)
//
// 既定: アバターは常に「走者+3m」に付いていく(遅れても離れず、色とオーラで知らせる — AGENTS.md §6)。
// 自走(既定 OFF・AR設定のスイッチ): アバターは目標ペースで自分で走る。10m 離れるとその場で待ち、
// 3m まで近づくと走り出す。追い抜くと後ろから足音と呼吸で伝える(Docs/PACESETTER_BEHAVIOR_SPEC.md)。
// Unity の StartSession の `selfPaced`。
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
