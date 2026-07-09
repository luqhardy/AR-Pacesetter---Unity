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
    @Published private(set) var isAuthorized = false

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
        isTracking = true

        manager.requestWhenInUseAuthorization()
        manager.startUpdatingLocation()
    }

    func stop() {
        isTracking = false
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
            // 精度不良・無効サンプルは棄却 (要件定義 6.2: 精度半径ゲート)
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
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        // 一時的な取得失敗は無視(次の更新を待つ)。Unity側がGPS FSMで補間する
        print("[LocationTracker] location error: \(error.localizedDescription)")
    }
}
