import Foundation
import Combine

/// 走行設定の共有ストア。
/// RunningSettingsView で入力した値を RunningView / UnityBridge へ引き渡す。
final class RunSettings: ObservableObject {

    static let shared = RunSettings()

    @Published var paceKmH: Double = 8.0
    @Published var distanceKm: Double = 10.0
    @Published var timeSeconds: Int = 3600

    private init() {}

    /// ペース表示文字列 (km/h → 分'秒"/km)
    var paceMinPerKmString: String {
        guard paceKmH > 0 else { return "--'--\"" }
        let minPerKm = 60.0 / paceKmH
        let minutes = Int(minPerKm)
        let seconds = Int((minPerKm - Double(minutes)) * 60.0)
        return String(format: "%d'%02d\"", minutes, seconds)
    }
}
