import Foundation
import CoreLocation
import Combine

/// CoreLocationによる実測距離・速度トラッカー。
/// 走行中の距離が取れている間はシミュレーション値の代わりにこちらが使われる。
/// シミュレータでも Features → Location → City Run で動作確認できる。
final class LocationTracker: NSObject, ObservableObject, CLLocationManagerDelegate {

    static let shared = LocationTracker()

    @Published private(set) var totalDistanceKm: Double = 0
    @Published private(set) var currentSpeedKmH: Double = 0
    @Published private(set) var hasValidSpeedMeasurement = false
    /// Whether the newest raw fix passed the accuracy gate and is allowed to
    /// refresh distance/speed freshness in Unity.
    @Published private(set) var latestFixAcceptedForMetrics = false

    // 生の測位サンプル(精度不良でも記録)。UnityのGPSロスト判定(§8.1)と
    // 走行ログCSVのGPS列(§5.2)へ供給する。accuracyは負値=無効(CoreLocation準拠)
    @Published private(set) var latestLatitude: Double = 0
    @Published private(set) var latestLongitude: Double = 0
    @Published private(set) var latestAccuracyMeters: Double = -1
    @Published private(set) var isAuthorized = false

    /// 直近の測位サンプルのタイムスタンプ(CoreLocation発行時刻)。
    /// 「前回送った時刻と違うか」で新鮮さを判定するために使う —
    /// 同じfixを再送するとUnityのGPSロスト判定(§8.1)が永久に成立しなくなる
    @Published private(set) var latestFixDate: Date?

    /// 新しい測位サンプルを受信するたびに呼ばれる。
    /// タイマーではなくこれで駆動することで、画面ロック中(タイマー停止)でも
    /// Unityへメトリクスを送り続けられる
    var onNewFix: (() -> Void)?

    private let manager = CLLocationManager()
    private var lastLocation: CLLocation?
    private var isTracking = false

    /// GPS精度がこの値(m)より悪いサンプルは距離に加算しない
    private let maxAcceptableAccuracyMeters: Double = 20

    private override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBestForNavigation
        manager.activityType = .fitness
        // distanceFilter は None にする(以前は 2m)。
        // UnityのGPSロスト判定(F-09 §8.1)は「更新が1.5秒途絶えたらロスト」で、
        // 2m移動しないと測位が届かない設定だと、**1.33m/s(4.8km/h)より遅い歩行や
        // 立ち止まり**で必ず「途絶」になる。5秒後にフェードアウト→スタンバイで
        // アバターが消え、屋内の歩行検証では「壁があると消える」に見えていた。
        // None なら CoreLocation は静止中でも約1Hzで測位を返し、途絶=本当の信号断だけになる
        manager.distanceFilter = kCLDistanceFilterNone
    }

    func start() {
        totalDistanceKm = 0
        currentSpeedKmH = 0
        hasValidSpeedMeasurement = false
        latestFixAcceptedForMetrics = false
        lastLocation = nil
        latestFixDate = nil
        isTracking = true

        manager.requestWhenInUseAuthorization()

        // バックグラウンド走行: 画面ロック中も計測継続
        // (Info.plist の UIBackgroundModes: location が前提。無い構成で
        //  allowsBackgroundLocationUpdates を立てると例外で落ちるためガード)
        let backgroundModes = Bundle.main.object(forInfoDictionaryKey: "UIBackgroundModes") as? [String] ?? []
        if backgroundModes.contains("location") {
            manager.allowsBackgroundLocationUpdates = true
            manager.pausesLocationUpdatesAutomatically = false
            manager.showsBackgroundLocationIndicator = true
        } else {
            print("[LocationTracker] UIBackgroundModes(location)が無いため前面時のみ計測します")
        }

        manager.startUpdatingLocation()
    }

    func stop() {
        isTracking = false
        onNewFix = nil
        manager.allowsBackgroundLocationUpdates = false
        manager.stopUpdatingLocation()
    }

    // MARK: CLLocationManagerDelegate

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        isAuthorized = status == .authorizedWhenInUse || status == .authorizedAlways
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard isTracking else { return }

        for location in locations {
            // 生サンプルは精度が悪くても記録する。UnityのGPSロスト判定(§8.1)は
            // 「精度10m以上への悪化」を検知する必要があるため、ここで捨てない
            latestLatitude = location.coordinate.latitude
            latestLongitude = location.coordinate.longitude
            latestAccuracyMeters = location.horizontalAccuracy
            latestFixDate = location.timestamp
            // Each raw fix gets an atomic validity result. A poor-accuracy fix
            // must not inherit the previous good fix's speed-valid state.
            latestFixAcceptedForMetrics = false
            hasValidSpeedMeasurement = false

            // 距離積算には精度不良・無効サンプルを使わない (要件定義 6.2: 精度半径ゲート)
            guard location.horizontalAccuracy >= 0,
                  location.horizontalAccuracy <= maxAcceptableAccuracyMeters else { continue }
            latestFixAcceptedForMetrics = true

            if let last = lastLocation,
               Self.shouldAccumulate(distanceMeters: location.distance(from: last),
                                     elapsedSeconds: location.timestamp.timeIntervalSince(last.timestamp)) {
                totalDistanceKm += location.distance(from: last) / 1000
            }
            lastLocation = location

            if location.speed >= 0 {
                currentSpeedKmH = location.speed * 3.6
                hasValidSpeedMeasurement = true
            }
        }

        // 実測が届いたタイミングでUnityへ送る(バックグラウンドでも動く経路)
        onNewFix?()
    }

    /// 走者として物理的にあり得る上限速度(m/s)。これを超える移動はGPSの飛びとして棄却する
    static let maxPlausibleSpeedMetersPerSecond: Double = 12.0 // 43km/h

    /// 2点間の移動を距離に積むか。
    ///
    /// 以前は「50m以上はテレポート」と**距離だけ**で棄却していた。GPSが途切れた後
    /// (トンネル・スタンド下など)の次の良好な測位は、走っていた分だけ離れているのが正常で、
    /// 3.3m/sなら約15秒の途絶で50mを超え、**その区間の距離が丸ごと失われていた**。
    /// 経過時間で割った速度で判定すれば、途絶後の移動は残り、本物の飛びだけを落とせる。
    static func shouldAccumulate(distanceMeters: Double, elapsedSeconds: Double) -> Bool {
        guard distanceMeters > 0.5 else { return false }   // 静止ジッター
        guard elapsedSeconds > 0 else { return false }      // 同時刻・逆行のサンプル
        return distanceMeters / elapsedSeconds <= maxPlausibleSpeedMetersPerSecond
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        // 一時的な取得失敗は無視(次の更新を待つ)。Unity側がGPS FSMで補間する
        print("[LocationTracker] location error: \(error.localizedDescription)")
    }
}
