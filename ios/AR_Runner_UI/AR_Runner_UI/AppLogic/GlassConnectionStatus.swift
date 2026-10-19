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

// MARK: - 準備画面(S4)の「次へ」とエラー表示

/// ARグラスの接続で起きていること(タスク表 S4 異常系)。問題が無ければ nil
nonisolated enum GlassConnectionProblem: Equatable {
    /// 挿さっていない
    case notDetected
    /// 挿さっているが、iOS がアプリ専用の画面を作らずミラーリングしている(不完全な接続)
    case incomplete
    /// この画面でつながった直後に切れた
    case droppedAfterConnect
    /// 走行中に切れて準備画面へ戻ってきた(§8.3。記録は続いている)
    case lostDuringRun

    /// 実接続・手動の接続なら問題なし。それ以外は、走行中に切れた > この画面で切れた > ミラーリング > 未検出 の順に出す
    static func resolve(status: GlassRowStatus, droppedHere: Bool, returnedFromRun: Bool) -> GlassConnectionProblem? {
        switch status {
        case .connected, .manual:
            return nil
        case .mirroring, .notDetected:
            if returnedFromRun { return .lostDuringRun }
            if droppedHere { return .droppedAfterConnect }
            return status == .mirroring ? .incomplete : .notDetected
        }
    }
}

/// 準備画面の「次へ」を押せるか。**ARグラスは必須**(タスク表 S4)— 未接続・ミラーリングでは進めない。
/// Apple Watch・AirPods は任意なので、失敗していても進める(ここでは見ない)
nonisolated enum DeviceGate {
    /// 開発者モード「ARグラスなしで走行を始める」(既定 OFF)。屋内・iPhone 画面だけで動作を確かめるとき用
    static let allowRunWithoutGlassesKey = "devAllowRunWithoutGlasses"

    static func canProceed(glass: GlassRowStatus, allowRunWithoutGlasses: Bool) -> Bool {
        switch glass {
        case .connected, .manual: return true
        case .mirroring, .notDetected: return allowRunWithoutGlasses
        }
    }
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
