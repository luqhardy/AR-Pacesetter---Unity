import Foundation

// MARK: - 走行履歴(Unity の HistoryData)
//
// Unity のセッション保存(1走行1JSON)から届く履歴の1件。ホームの「前回の記録」・履歴画面・
// ルート選択の過去ルートカードが使う。
//
// 端末にも保存する(`RunHistoryCache`): Unity は走行画面で初めて起動するので、起動直後の
// ホームや履歴画面では Unity に問い合わせても返事が来ない。以前はその間、履歴画面が
// **作り物の4件**(6月15日 2.1km …)を出していた(2026-10-13 修正)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// 経路の1点(緯度・経度)
nonisolated struct RouteCoordinate: Codable, Equatable {
    let latitude: Double
    let longitude: Double
}

nonisolated struct RunHistoryEntry: Codable, Equatable, Identifiable {
    let dateIso: String      // ISO8601 (Unity側 DateTime "o" 形式)
    let distanceKm: Double
    let elapsedSeconds: Double
    let averageSync: Double  // %
    let grade: String
    /// 走った経路(古い順・間引き済み)。2026-10-13 より前の記録は空
    let route: [RouteCoordinate]

    var id: String { dateIso }

    init(dateIso: String, distanceKm: Double, elapsedSeconds: Double,
         averageSync: Double, grade: String, route: [RouteCoordinate] = []) {
        self.dateIso = dateIso
        self.distanceKm = distanceKm
        self.elapsedSeconds = elapsedSeconds
        self.averageSync = averageSync
        self.grade = grade
        self.route = route
    }

    /// 地図に線を引けるか(2点以上)
    var hasRoute: Bool { route.count >= 2 }

    /// "6月15日" 形式の表示用日付
    var dateLabel: String {
        let isoDay = String(dateIso.prefix(10)) // yyyy-MM-dd
        let parser = DateFormatter()
        parser.locale = Locale(identifier: "en_US_POSIX")
        parser.dateFormat = "yyyy-MM-dd"
        guard let date = parser.date(from: isoDay) else { return isoDay }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ja_JP")
        formatter.dateFormat = "M月d日"
        return formatter.string(from: date)
    }

    /// "mm:ss" 形式の所要時間
    var timeLabel: String {
        let total = Int(elapsedSeconds)
        return String(format: "%02d:%02d", total / 60, total % 60)
    }

    /// 平均ペース "5'21\""。距離がほぼ0なら nil。
    /// 秒で丸めてから分に分ける(分の小数から秒を出すと 5'21" が浮動小数の誤差で 5'20" になる)
    var paceLabel: String? {
        guard distanceKm > 0.01 else { return nil }
        let secondsPerKm = Int((elapsedSeconds / distanceKm).rounded())
        return String(format: "%d'%02d\"", secondsPerKm / 60, secondsPerKm % 60)
    }

    // MARK: Unity の JSON から

    /// HistoryData の `sessions` を読む。日付の無い行は捨てる(識別子にならない)
    static func parse(sessions: [[String: Any]]) -> [RunHistoryEntry] {
        sessions.compactMap { s -> RunHistoryEntry? in
            guard let dateIso = s["dateIso"] as? String, !dateIso.isEmpty else { return nil }
            return RunHistoryEntry(
                dateIso: dateIso,
                distanceKm: number(s["distanceKm"]) ?? 0,
                elapsedSeconds: number(s["elapsedSeconds"]) ?? 0,
                averageSync: number(s["averageSync"]) ?? 0,
                grade: s["grade"] as? String ?? "-",
                route: parseRoute(s["route"])
            )
        }
    }

    /// `[[緯度,経度],...]`。壊れた点・範囲外の点は飛ばす
    static func parseRoute(_ value: Any?) -> [RouteCoordinate] {
        guard let points = value as? [Any] else { return [] }
        return points.compactMap { point -> RouteCoordinate? in
            guard let pair = point as? [Any], pair.count == 2,
                  let lat = number(pair[0]), let lon = number(pair[1]),
                  (-90.0...90.0).contains(lat), (-180.0...180.0).contains(lon)
            else { return nil }
            return RouteCoordinate(latitude: lat, longitude: lon)
        }
    }

    private static func number(_ value: Any?) -> Double? {
        if let d = value as? Double { return d }
        if let i = value as? Int { return Double(i) }
        if let n = value as? NSNumber { return n.doubleValue }
        return nil
    }
}

// MARK: - 過去ルートの並べ方

nonisolated enum PastRouteOrdering {
    /// ルート選択に出す過去の走行: 経路のあるものだけを、目標距離に近い順(同じ近さなら新しい順)。
    /// `history` は Unity から届いた順(新しい順)のまま渡す
    static func candidates(from history: [RunHistoryEntry], targetDistanceKm: Double) -> [RunHistoryEntry] {
        history.enumerated()
            .filter { $0.element.hasRoute }
            .sorted { a, b in
                let da = abs(a.element.distanceKm - targetDistanceKm)
                let db = abs(b.element.distanceKm - targetDistanceKm)
                return da != db ? da < db : a.offset < b.offset
            }
            .map { $0.element }
    }
}

// MARK: - 地図の表示範囲

/// 経路がちょうど収まる範囲(中心と幅・度)。余白込み
nonisolated struct RouteBounds: Equatable {
    let centerLatitude: Double
    let centerLongitude: Double
    let latitudeSpan: Double
    let longitudeSpan: Double

    /// - Parameter padding: 幅に掛ける余白の倍率
    /// - Parameter minimumSpan: 1点・直線でも潰れないための最小幅(度。0.002 ≒ 220m)
    init?(route: [RouteCoordinate], padding: Double = 1.3, minimumSpan: Double = 0.002) {
        guard let first = route.first else { return nil }
        var minLat = first.latitude, maxLat = first.latitude
        var minLon = first.longitude, maxLon = first.longitude
        for p in route {
            minLat = min(minLat, p.latitude); maxLat = max(maxLat, p.latitude)
            minLon = min(minLon, p.longitude); maxLon = max(maxLon, p.longitude)
        }
        centerLatitude = (minLat + maxLat) / 2
        centerLongitude = (minLon + maxLon) / 2
        latitudeSpan = max((maxLat - minLat) * padding, minimumSpan)
        longitudeSpan = max((maxLon - minLon) * padding, minimumSpan)
    }
}

// MARK: - 端末への保存

/// 最後に受け取った履歴。Unity が起動する前でも履歴を出すため
nonisolated struct RunHistoryCache: Codable, Equatable {
    var entries: [RunHistoryEntry]
    var receivedAt: Date
}
