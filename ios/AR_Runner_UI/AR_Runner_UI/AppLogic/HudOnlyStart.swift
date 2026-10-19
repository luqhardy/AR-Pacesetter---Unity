import Foundation

// MARK: - GPSが使えないときはHUDだけで始める(タスク表 ARグラス)
//
// 既定(OFF): 走り始めにGPSが使えなくてもアバターを出す(Unity は良好な測位を得る前は GPS ロストにしない)。
// ON(AR設定のスイッチ): 走行開始(START)の時点で GPS が使えなければ — 測位が届いていない・精度10m以上 —
// アバターを出さず、HUD とその下部の警告だけで走る。精度5m以内の測位でアバターが現れる。
// Unity の StartSession の `hudOnlyWithoutGps`。その間 Unity は GPSLost に `awaitingFix: true` を付けて送る。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum HudOnlyStart {
    /// AR設定のスイッチ(既定 OFF)
    static let storageKey = "hudOnlyWithoutGps"

    /// 次の走行で「GPSが使えなければHUDだけで始める」か。保存が無ければ OFF(従来どおりアバターを出す)
    static func isEnabled(in store: UserDefaults) -> Bool {
        store.bool(forKey: storageKey)
    }

    /// Unity の GPSLost イベントが「START からGPSを待っている(HUDのみ — アバターはまだ出ていない)」か。
    /// 付いていなければ通常のGPSロスト(アバターは慣性で走っている)
    static func isAwaitingFirstFix(gpsLostEvent event: [String: Any]) -> Bool {
        event["awaitingFix"] as? Bool ?? false
    }
}
