import Foundation

// MARK: - ルート選択(S8)の候補
//
// タスク表 S8:
//   - 候補は横スクロールで目標距離に近い順(過去のルート — PastRouteOrdering)
//   - 候補が1件も無いときは、既定のルート(直線距離等)を自動で作って出す
//   - 最初の候補を選んだ状態から始める(「戻る」で S5 へ戻ったら選択は捨て、次に来たらやり直す)
//   - 「詳細を見る」で全体像・距離・推定時間・ペースを出す
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated struct RouteCandidate: Identifiable, Equatable {
    enum Kind: Equatable {
        /// 過去に走ったルート
        case past
        /// 候補が無いときに作った既定のルート(現在地から北へ行って戻る往復)
        case generated
    }

    let id: String
    let kind: Kind
    /// 過去のルートの日付(ISO8601)。作ったルートは nil
    let dateIso: String?
    let distanceKm: Double
    /// 地図に引く線(古い順)。現在地が分からないときの既定ルートは空
    let coordinates: [RouteCoordinate]

    var hasLine: Bool { coordinates.count >= 2 }

    /// 目標ペースで走ったときの推定時間(秒)。ペースが無い・距離が無いときは nil
    func estimatedSeconds(paceKmH: Double) -> Int? {
        guard paceKmH.isFinite, paceKmH > 0, distanceKm.isFinite, distanceKm > 0 else { return nil }
        return Int((distanceKm / paceKmH * 3600).rounded())
    }
}

nonisolated enum RouteCandidates {
    /// 既定のルートの id(選択状態の比較に使う)
    static let generatedID = "generated"

    /// 候補の一覧。過去のルート(経路のあるもの)を目標距離に近い順に。1件も無ければ既定のルートを1件
    static func build(history: [RunHistoryEntry], targetDistanceKm: Double,
                      currentLocation: RouteCoordinate?) -> [RouteCandidate] {
        let past = PastRouteOrdering.candidates(from: history, targetDistanceKm: targetDistanceKm).map {
            RouteCandidate(id: $0.id, kind: .past, dateIso: $0.dateIso, distanceKm: $0.distanceKm, coordinates: $0.route)
        }
        if !past.isEmpty { return past }
        return [RouteCandidate(id: generatedID, kind: .generated, dateIso: nil, distanceKm: targetDistanceKm,
                               coordinates: currentLocation.map { outAndBack(from: $0, distanceKm: targetDistanceKm) } ?? [])]
    }

    /// 最初に選んでおく候補(一覧の先頭)。選んでいたものがまだあればそれを保つ
    static func initialSelection(_ candidates: [RouteCandidate], keeping current: String?) -> String? {
        if let current, candidates.contains(where: { $0.id == current }) { return current }
        return candidates.first?.id
    }

    /// 現在地から北へ距離の半分だけ進んで戻る往復(直線)。往復で目標距離になる
    static func outAndBack(from start: RouteCoordinate, distanceKm: Double) -> [RouteCoordinate] {
        guard distanceKm.isFinite, distanceKm > 0,
              start.latitude.isFinite, start.longitude.isFinite else { return [] }
        // 緯度1度 ≒ 111.32km(北へ進むので経度は変わらない)。極を越えないよう 89° で止める
        let turnLatitude = min(89.0, start.latitude + (distanceKm / 2) / 111.32)
        let turn = RouteCoordinate(latitude: turnLatitude, longitude: start.longitude)
        return [start, turn, start]
    }
}
