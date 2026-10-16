import CoreLocation

// MARK: - 位置情報の状態(画面共通)
// 判断は AppLogic/LocationProblem(テスト対象)。ここは CoreLocation の値をそれに渡す形へ変えるだけ。

extension LocationAuthorization {
    init(_ status: CLAuthorizationStatus) {
        switch status {
        case .notDetermined: self = .notDetermined
        case .restricted: self = .restricted
        case .denied: self = .denied
        case .authorizedAlways, .authorizedWhenInUse: self = .authorized
        @unknown default: self = .notDetermined
        }
    }
}

enum LocationServices {
    /// iPhone 全体の位置情報サービスがオンか。
    /// メインスレッドで呼ぶと UI が固まることがある(Apple の注意)ため、裏で調べて結果だけ返す
    static func isEnabled() async -> Bool {
        await Task.detached { CLLocationManager.locationServicesEnabled() }.value
    }
}
