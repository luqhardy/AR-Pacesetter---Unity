import Foundation

// MARK: - 位置情報が使えない理由
//
// タスク表(位置情報許可の異常系)は「拒否」「制限(ペアレンタルコントロール・MDM)」「位置情報サービス自体がオフ」で
// 案内を分ける。以前はどれも「位置情報が許可されていません」の1種類で、制限されている人に
// 「設定から許可してください」と案内していた(本人には変えられない)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。CoreLocation の値は画面側で変換して渡す。

/// アプリへの位置情報の許可(CLAuthorizationStatus を Foundation だけで扱うための写し)
nonisolated enum LocationAuthorization: Equatable {
    case notDetermined, restricted, denied, authorized
}

nonisolated enum LocationProblem: Equatable {
    /// iPhone 全体の位置情報サービスがオフ(設定 → プライバシーとセキュリティ → 位置情報サービス)
    case servicesOff
    /// 制限されている(ペアレンタルコントロール・管理プロファイル)— 本人には変えられない
    case restricted
    /// このアプリに許可していない — 設定アプリで変えられる
    case denied

    /// 本人が設定アプリで直せるか(「設定を開く」を出すか)
    var userCanFix: Bool { self != .restricted }

    /// 案内が要るか。未選択はこれから許可を求めるので問題ではない
    static func resolve(servicesEnabled: Bool, authorization: LocationAuthorization) -> LocationProblem? {
        // サービス自体がオフだと、アプリへの許可の状態に関係なく測位できない
        guard servicesEnabled else { return .servicesOff }
        switch authorization {
        case .restricted: return .restricted
        case .denied: return .denied
        case .notDetermined, .authorized: return nil
        }
    }
}
