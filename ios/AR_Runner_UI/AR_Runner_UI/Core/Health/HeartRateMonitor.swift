import Foundation
import Combine
#if canImport(HealthKit)
import HealthKit
#endif

/// Apple Watch → HealthKit 経由のリアルタイム心拍モニター。
/// 取得できている間は latestBpm > 0 になり、シミュレーション値の代わりに使われる。
///
/// 必要設定 (Xcode側・初回のみ):
///   1. AR_Runner_UI ターゲット → Signing & Capabilities → + Capability → HealthKit
///   2. AR_Runner_UI.entitlements が同梱済み (com.apple.developer.healthkit)
/// シミュレータ/権限未許可時は latestBpm = 0 のまま(呼び出し側がフォールバック)。
final class HeartRateMonitor: ObservableObject {

    static let shared = HeartRateMonitor()

    @Published private(set) var latestBpm: Int = 0
    @Published private(set) var isAuthorized = false

#if canImport(HealthKit)
    private let store = HKHealthStore()
    private var activeQuery: HKAnchoredObjectQuery?

    private init() {}

    func start() {
        guard HKHealthStore.isHealthDataAvailable(),
              let hrType = HKObjectType.quantityType(forIdentifier: .heartRate) else {
            print("[HeartRateMonitor] HealthKit unavailable on this device.")
            return
        }

        store.requestAuthorization(toShare: nil, read: [hrType]) { [weak self] granted, error in
            guard let self else { return }
            DispatchQueue.main.async { self.isAuthorized = granted }

            if granted {
                self.beginStreaming(hrType)
            } else {
                print("[HeartRateMonitor] HealthKit authorization denied: \(error?.localizedDescription ?? "-")")
            }
        }
    }

    func stop() {
        if let query = activeQuery {
            store.stop(query)
            activeQuery = nil
        }
        latestBpm = 0
    }

    private func beginStreaming(_ hrType: HKQuantityType) {
        // 直近1分以降のサンプルから購読開始(古い履歴の混入を防ぐ)
        let predicate = HKQuery.predicateForSamples(
            withStart: Date().addingTimeInterval(-60), end: nil, options: [])

        let query = HKAnchoredObjectQuery(
            type: hrType, predicate: predicate,
            anchor: nil, limit: HKObjectQueryNoLimit
        ) { [weak self] _, samples, _, _, _ in
            self?.ingest(samples)
        }
        query.updateHandler = { [weak self] _, samples, _, _, _ in
            self?.ingest(samples)
        }

        store.execute(query)
        activeQuery = query
    }

    private func ingest(_ samples: [HKSample]?) {
        guard let quantitySamples = samples as? [HKQuantitySample],
              let newest = quantitySamples.max(by: { $0.startDate < $1.startDate }) else { return }

        let bpmUnit = HKUnit.count().unitDivided(by: .minute())
        let bpm = Int(newest.quantity.doubleValue(for: bpmUnit).rounded())

        DispatchQueue.main.async { [weak self] in
            self?.latestBpm = bpm
        }
    }
#else
    private init() {}
    func start() {}
    func stop() { latestBpm = 0 }
#endif
}
