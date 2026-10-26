import Foundation

// MARK: - 展示ブースの電池・発熱(Kobe Calling スタッフ画面)
//
// 体験モードは来場者ごとに1分ほどを何時間も回す。iPhone はグラスへの出力・カメラ・ARKit で熱を持ち、
// iOS が処理を絞ると(serious 以上)フレームが落ちてアバターがカクつく — 来場者には「壊れた」に見える。
// スタッフが次の方を始める前に気づけるよう、電池と発熱の段階を出し、絞られる手前(fair)で注意を出す。
// 体験は止めない(判断はスタッフ)。運用の手順は Docs/KOBE_CALLING_CHECKLIST.md C。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。値は Core/Device/PowerMonitor から。
// スタッフ画面は日本語のみ(AGENTS.md §6)。

nonisolated struct BoothPowerStatus: Equatable {
    enum Level: Int, Comparable {
        case ok = 0, caution, danger
        static func < (a: Level, b: Level) -> Bool { a.rawValue < b.rawValue }
    }

    /// これ以下(給電なし)で注意。体験を数十回回すと20%台まで落ちる
    static let cautionBattery = 0.40
    /// これ以下(給電なし)で交代を促す(走行の警告 RunPowerWarning と同じ20%)
    static let dangerBattery = RunPowerWarning.lowBatteryThreshold

    let batteryLevel: Level
    let thermalLevel: Level
    /// チップに出す文言
    let batteryText: String
    let thermalText: String
    /// 注意・危険のときに出す一言(何をすればよいか)。平常なら nil
    let advice: String?

    var level: Level { max(batteryLevel, thermalLevel) }

    init(_ sample: PowerSample) {
        let charging = sample.charging == true

        if let battery = sample.batteryLevel {
            let percent = Int((battery * 100).rounded())
            batteryText = charging ? "電池 \(percent)%(給電中)" : "電池 \(percent)%"
            if charging { batteryLevel = .ok }
            else if battery <= Self.dangerBattery { batteryLevel = .danger }
            else if battery <= Self.cautionBattery { batteryLevel = .caution }
            else { batteryLevel = .ok }
        } else {
            batteryText = "電池 —"
            batteryLevel = .ok
        }

        switch sample.thermal {
        case .nominal:  thermalText = "温度 平常";   thermalLevel = .ok
        case .fair:     thermalText = "温度 やや高い"; thermalLevel = .caution
        case .serious:  thermalText = "温度 高い";   thermalLevel = .danger
        case .critical: thermalText = "温度 危険";   thermalLevel = .danger
        }

        advice = Self.advice(sample: sample, battery: batteryLevel, charging: charging)
    }

    /// 発熱を優先する(フレームが落ちるのは発熱が先で、電池は給電で戻せる)
    private static func advice(sample: PowerSample, battery: Level, charging: Bool) -> String? {
        switch sample.thermal {
        case .critical:
            return "iPhoneが非常に熱くなっています。体験を止め、予備のiPhoneへ交代してください。"
        case .serious:
            return "iPhoneが熱くなり、映像がカクつき始めます。この方で区切り、予備のiPhoneへ交代するか日陰で冷ましてください。"
        case .fair:
            if battery == .danger { return lowBatteryAdvice }
            return "iPhoneが温まってきました。次の方の前に少し間を空けるか、ケース・マウントを外して冷ましてください。"
        case .nominal:
            break
        }
        switch battery {
        case .danger:  return lowBatteryAdvice
        case .caution: return "電池が減ってきました。給電のハブ・ケーブルが挿さっているか確かめてください。"
        case .ok:      return nil
        }
    }

    private static let lowBatteryAdvice = "電池が少なくなっています。給電するか、予備のiPhoneへ交代してください。"
}
