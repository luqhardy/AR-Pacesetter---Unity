import Foundation
#if canImport(HealthKit)
import HealthKit
#endif

/// デュアル・データ保存の HealthKit 側 (企画書 §4):
/// Unityからの SessionEnded 受信時に、走行をHKWorkout(ランニング)として
/// ヘルスケアへ書き込む。距離・消費カロリーのサンプルも添付する。
///
/// 権限: 初回保存時に書き込み許可(ワークアウト/距離/カロリー)を要求。
/// 拒否・シミュレータ・HealthKit非対応環境では静かにスキップする
/// (アプリ内JSON DBへの保存はUnity側で完了済みのため走行記録は失われない)。
final class HealthKitWorkoutSaver {

    static let shared = HealthKitWorkoutSaver()

#if canImport(HealthKit)
    private let store = HKHealthStore()

    private init() {}

    func saveWorkout(distanceKm: Double, elapsedSeconds: Double, calories: Double) {
        guard HKHealthStore.isHealthDataAvailable(),
              distanceKm > 0.01, elapsedSeconds > 1 else { return }

        let shareTypes: Set<HKSampleType> = [
            HKObjectType.workoutType(),
            HKQuantityType(.distanceWalkingRunning),
            HKQuantityType(.activeEnergyBurned),
        ]

        store.requestAuthorization(toShare: shareTypes, read: []) { [weak self] granted, error in
            guard granted, let self else {
                print("[HealthKitSaver] 書き込み許可なし — スキップ (\(error?.localizedDescription ?? "denied"))")
                return
            }
            self.performSave(distanceKm: distanceKm,
                             elapsedSeconds: elapsedSeconds,
                             calories: calories)
        }
    }

    private func performSave(distanceKm: Double, elapsedSeconds: Double, calories: Double) {
        let end = Date()
        let start = end.addingTimeInterval(-elapsedSeconds)

        let configuration = HKWorkoutConfiguration()
        configuration.activityType = .running
        configuration.locationType = .outdoor

        let builder = HKWorkoutBuilder(healthStore: store,
                                       configuration: configuration,
                                       device: .local())

        builder.beginCollection(withStart: start) { [weak self] began, error in
            guard began, self != nil else {
                print("[HealthKitSaver] beginCollection失敗: \(error?.localizedDescription ?? "-")")
                return
            }

            var samples: [HKSample] = [
                HKQuantitySample(
                    type: HKQuantityType(.distanceWalkingRunning),
                    quantity: HKQuantity(unit: .meter(), doubleValue: distanceKm * 1000),
                    start: start, end: end),
            ]
            if calories > 0 {
                samples.append(HKQuantitySample(
                    type: HKQuantityType(.activeEnergyBurned),
                    quantity: HKQuantity(unit: .kilocalorie(), doubleValue: calories),
                    start: start, end: end))
            }

            builder.add(samples) { _, _ in
                builder.endCollection(withEnd: end) { _, _ in
                    builder.finishWorkout { workout, error in
                        if workout != nil {
                            print("[HealthKitSaver] ワークアウト保存完了: \(String(format: "%.2f", distanceKm))km / \(Int(elapsedSeconds))s")
                        } else {
                            print("[HealthKitSaver] 保存失敗: \(error?.localizedDescription ?? "-")")
                        }
                    }
                }
            }
        }
    }
#else
    private init() {}
    func saveWorkout(distanceKm: Double, elapsedSeconds: Double, calories: Double) {}
#endif
}
