import Foundation
import Combine

/// 走行設定の共有ストア。
/// RunningSettingsView で入力した値を RunningView / UnityBridge へ引き渡す。
/// 確定値(`confirm`)は端末にも保存し、アプリを閉じても次の起動で同じ値から始める
/// (保存の形・範囲の補正は AppLogic/RunTargetSettings.swift — テスト対象)
final class RunSettings: ObservableObject {

    static let shared = RunSettings()

    @Published private(set) var paceKmH: Double
    @Published private(set) var distanceKm: Double
    @Published private(set) var timeSeconds: Int

    /// ゴースト競走 (企画書§3): 履歴画面で選んだ過去セッションのdateIso。
    /// 設定されていると次の走行はゴーストモードで開始される(1回で自動クリア)。端末には保存しない
    @Published var ghostDateIso: String? = nil

    /// S8 で選んだルート(「ルートを外れたら案内する」が ON・過去の走行の経路のときだけ)。
    /// Unity の StartSession の routeLatLon([緯度, 経度, ...])。次の1走行だけ — 端末には保存しない
    @Published var guidanceRoute: [Double]? = nil

    private init() {
        let saved = RunTargetSettings.load(from: .standard)
        paceKmH = saved.paceKmH
        distanceKm = saved.distanceKm
        timeSeconds = saved.timeSeconds
    }

    /// 「次へ」で確定した3値を反映して保存する。入力途中の値はここを通らない
    func confirm(paceKmH: Double, distanceKm: Double, timeSeconds: Int) {
        let settings = RunTargetSettings(paceKmH: paceKmH, distanceKm: distanceKm,
                                         timeSeconds: timeSeconds).sanitized()
        self.paceKmH = settings.paceKmH
        self.distanceKm = settings.distanceKm
        self.timeSeconds = settings.timeSeconds
        settings.save(to: .standard)
    }

    /// ペース表示文字列 (km/h → 分'秒"/km)
    var paceMinPerKmString: String {
        guard paceKmH > 0 else { return "--'--\"" }
        let minPerKm = 60.0 / paceKmH
        let minutes = Int(minPerKm)
        let seconds = Int((minPerKm - Double(minutes)) * 60.0)
        return String(format: "%d'%02d\"", minutes, seconds)
    }
}
