import Foundation

// MARK: - 設定をグラス(Unity)へ反映できたか(タスク表 S9 設定メニュー 異常系)
//
// タスク表「接続中の送信失敗時: 『ARグラスへの設定反映に失敗しました』のトースト表示と再送信(リトライ)」。
// AR設定から送る設定はアバターの選択(Unity の SelectVrmAvatar)で、Unity は成否にかかわらず必ず
// VrmImportResult を返す。返ってこない = 届いていない、として知らせ、同じ選択を送り直せるようにする。
//
// Unity は走行画面でだけ動き、走行の後は止めてある。止まったままでは応答が無いので、
// 起動済みなら応答までの間だけ動かす(結果画面の保存の再試行と同じ)。一度も起動していなければ
// 送れないので、次に走り始めたときに反映する(UnityBridge が起動まで溜める)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum SettingDeliveryState: Equatable {
    /// 送っていない・反映済み
    case idle
    /// Unity がまだ起動していない — 次に走り始めたときに反映する
    case queued
    /// 応答を待っている
    case sending
    /// 待っても応答が無い — トーストを出して再送信できるようにする
    case failed

    /// 応答を待つ秒数。Unity は通常1フレームで返す
    static let responseTimeoutSeconds: TimeInterval = 5

    /// 送ったときの状態。一度も起動していなければ溜めるだけ
    static func onSend(unityLaunched: Bool, unityReady: Bool) -> SettingDeliveryState {
        unityLaunched && unityReady ? .sending : .queued
    }

    /// 応答を待つ間だけ Unity を動かす必要があるか(起動済みで、走行中ではない = 止めてある)
    static func needsTemporaryResume(unityLaunched: Bool, unityReady: Bool, sessionActive: Bool) -> Bool {
        unityLaunched && unityReady && !sessionActive
    }
}
