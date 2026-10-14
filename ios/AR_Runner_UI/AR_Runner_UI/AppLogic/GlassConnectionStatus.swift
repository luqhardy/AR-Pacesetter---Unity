import Foundation

// MARK: - ARグラスの接続状態(準備画面・開発者モードの表示用)
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// 準備画面のARグラス行の状態。**実接続(iOS がグラス専用の画面を作った)だけを「接続済み」とする**。
/// 以前はタップから1.5秒後に、何も挿さっていなくても「接続済み」になり、
/// グラスのミラーリングを隠していた(2026-10-07 実機)
nonisolated enum GlassRowStatus: Equatable {
    case connected    // 専用の画面にARビューを出している
    case mirroring    // 挿さっているが iOS がミラーリングしている
    case manual       // 検出は無いが、手動でグラス用の表示にした
    case notDetected

    /// 実際の状態の優先順: 実接続 > ミラーリング > 手動 > 未検出
    static func resolve(glassesConnected: Bool, mirroring: Bool, manualConnect: Bool) -> GlassRowStatus {
        if glassesConnected { return .connected }
        if mirroring { return .mirroring }
        if manualConnect { return .manual }
        return .notDetected
    }

    /// 手動の接続を押せるか(実接続も手動接続もしていないとき)
    var canConnectManually: Bool { self == .notDetected || self == .mirroring }
}

// MARK: - 開発者モード「屋内テスト」

/// Unity の Diagnostics の `gps.autoLostHandling` の値("ON(仕様どおり)" / "OFF(手動で無効化中)")を読む。
/// スイッチは画面側で状態を持たず、常にこの値から表示を作る(体験モードの切替と食い違わない)
nonisolated enum GpsLostHandlingReading {
    static let diagnosticsKey = "gps.autoLostHandling"

    /// ON なら true、OFF なら false、読めなければ nil(Unity 未応答)
    static func parse(_ value: String?) -> Bool? {
        guard let value else { return nil }
        if value.hasPrefix("ON") { return true }
        if value.hasPrefix("OFF") { return false }
        return nil
    }
}
