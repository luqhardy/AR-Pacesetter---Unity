import Foundation

// MARK: - トラックのレーン(Docs/PACESETTER_BEHAVIOR_SPEC.md R-40〜R-43)
//
// レーンはルート選択(S8)のルートとして並べる。レーンごとに1周の長さと形(座標)を別々に持つ。
// 最初に作るのは第1レーンだけ。第2レーン以降は `all` に足す — 長さは規格の確認待ち(U-03)で、ここでは決めない。
// トラックの場所はどこでも形が同じなので、座標は持たず線は引かない。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated struct TrackRoute: Equatable {
    let id: String
    let lapLengthMeters: Double
    /// 形(座標)。トラックの位置が分かるまで空
    let coordinates: [RouteCoordinate]
}

nonisolated enum TrackRoutes {
    static let lane1 = TrackRoute(id: "track400_lane1", lapLengthMeters: 400, coordinates: [])

    /// ルート選択に並べるレーン
    static let all: [TrackRoute] = [lane1]
}

/// 周回数と「あと◯m」(R-43)。選んだレーンの1周で計算する
nonisolated struct LapProgress: Equatable {
    /// 走り終えた周
    let completedLaps: Int
    /// 今の周の残り(m)
    let remainingMeters: Int

    /// 今走っている周(1始まり)
    var currentLap: Int { completedLaps + 1 }

    /// 1周の長さが無い(トラックではない)・距離が数でないときは nil
    init?(distanceMeters: Double, lapLengthMeters: Double?) {
        guard let lapLengthMeters, lapLengthMeters > 0, lapLengthMeters.isFinite,
              distanceMeters.isFinite, distanceMeters >= 0 else { return nil }
        let laps = (distanceMeters / lapLengthMeters).rounded(.down)
        completedLaps = Int(laps)
        remainingMeters = Int((lapLengthMeters - (distanceMeters - laps * lapLengthMeters)).rounded(.up))
    }
}
