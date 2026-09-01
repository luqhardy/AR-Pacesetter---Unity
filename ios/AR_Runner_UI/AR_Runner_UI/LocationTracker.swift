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
        manager.distanceFilter = 2 // 2m毎に更新
    }

    func start() {
        totalDistanceKm = 0
        currentSpeedKmH = 0
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

            // 距離積算には精度不良・無効サンプルを使わない (要件定義 6.2: 精度半径ゲート)
            guard location.horizontalAccuracy >= 0,
                  location.horizontalAccuracy <= maxAcceptableAccuracyMeters else { continue }

            if let last = lastLocation {
                let delta = location.distance(from: last)
                // 静止ジッター(<0.5m)は無視、テレポート(>50m)はGPS飛びとして棄却
                if delta > 0.5 && delta < 50 {
                    totalDistanceKm += delta / 1000
                }
            }
            lastLocation = location

            if location.speed >= 0 {
                currentSpeedKmH = location.speed * 3.6
            }
        }

        // 実測が届いたタイミングでUnityへ送る(バックグラウンドでも動く経路)
        onNewFix?()
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        // 一時的な取得失敗は無視(次の更新を待つ)。Unity側がGPS FSMで補間する
        print("[LocationTracker] location error: \(error.localizedDescription)")
    }
}
