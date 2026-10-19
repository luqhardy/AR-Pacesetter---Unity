import Foundation
import UIKit

// MARK: - 電池・発熱の記録
//
// 走行の開始・終了の電池と、走行中に一番高かった発熱の段階を残す(§10 連続稼働60分・電池30%以上)。
// 判断と計算は AppLogic/RunPowerRecord(テスト対象)。ここは iOS から値を読んで渡すだけ。
// 直近の走行の記録は端末に保存し、アプリを再起動しても開発者モードの書き出しに載る。
final class PowerMonitor {

    static let shared = PowerMonitor()

    /// 直近の走行(走行中なら end は nil)
    private(set) var lastRun: RunPowerRecord?

    private static let storageKey = "lastRunPower"
    private var thermalObserver: NSObjectProtocol?

    private init() {
        if let data = UserDefaults.standard.data(forKey: Self.storageKey),
           let saved = try? JSONDecoder().decode(RunPowerRecord.self, from: data) {
            lastRun = saved
        }
    }

    /// 今の電池・発熱
    func sample() -> PowerSample {
        let device = UIDevice.current
        // 電池の監視を有効にしないと batteryLevel は -1 のまま
        device.isBatteryMonitoringEnabled = true
        let level = device.batteryLevel
        return PowerSample(
            at: Date(),
            batteryLevel: level >= 0 ? Double(level) : nil,
            thermal: ThermalLevel(rawValue: ProcessInfo.processInfo.thermalState.rawValue) ?? .nominal,
            lowPowerMode: ProcessInfo.processInfo.isLowPowerModeEnabled
        )
    }

    /// 走行開始(ARSessionManager.start)
    func runStarted() {
        lastRun = RunPowerRecord(start: sample())
        save()
        // 走行中の発熱の上がり下がりを追う(一番高かった段階を残す)
        if thermalObserver == nil {
            thermalObserver = NotificationCenter.default.addObserver(
                forName: ProcessInfo.thermalStateDidChangeNotification, object: nil, queue: .main
            ) { [weak self] _ in
                guard let self, var run = self.lastRun, run.end == nil else { return }
                run.observe(thermal: ThermalLevel(rawValue: ProcessInfo.processInfo.thermalState.rawValue) ?? .nominal)
                self.lastRun = run
                self.save()
            }
        }
    }

    /// 走行終了(ARSessionManager.end / endLocally)。走行中でなければ何もしない
    func runEnded() {
        guard var run = lastRun, run.end == nil else { return }
        run.finish(with: sample())
        lastRun = run
        save()
    }

    private func save() {
        if let data = try? JSONEncoder().encode(lastRun) {
            UserDefaults.standard.set(data, forKey: Self.storageKey)
        }
    }
}
