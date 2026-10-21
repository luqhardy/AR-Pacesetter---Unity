import Foundation

// MARK: - 選んだルートからの逸脱の案内(HANDOVER §5「ルート同期」・企画書 4.3 サイレントルート復帰)
//
// S8 で選んだ過去のルートを Unity へ渡し、外れたらアバターが走者の後ろへ回ってルートの方を向いて案内する。
// **既定 OFF**(S8 のスイッチで ON)。トラックの GPS 誤差で何度も反応しないよう、Unity 側は
// 15m 以上(精度が悪いときは精度の2倍)外れて5秒続いたときだけ案内する(RouteDeviationGate)。
//
// 候補が無いときに作る往復ルート(RouteCandidates.outAndBack)は渡さない — 実際に走るコースではなく、
// 渡すと走り出してすぐ「外れた」になる。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum RouteGuidance {
    /// S8 のスイッチ(既定 OFF)
    static let storageKey = "routeGuidanceEnabled"

    /// このルートで案内を使えるか: 過去に走ったルートで、線が引けること
    static func isAvailable(for route: RouteCandidate?) -> Bool {
        guard let route else { return false }
        return route.kind == .past && route.hasLine
    }

    /// Unity の StartSession に載せる平らな [緯度0, 経度0, 緯度1, 経度1, ...]。使わないときは nil
    static func payload(for route: RouteCandidate?, enabled: Bool) -> [Double]? {
        guard enabled, isAvailable(for: route), let route else { return nil }
        return route.coordinates.flatMap { [$0.latitude, $0.longitude] }
    }
}
