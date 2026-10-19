import SwiftUI
import CoreMotion

struct HeadTrackingTestView: View {
    @StateObject private var probe = HeadTrackingProbe()
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        Form {
            Section {
                LabeledContent("接続状態", value: connectionLabel) // TODO: 文言の集約対象
                LabeledContent("モーション許可", value: authorizationLabel) // TODO: 文言の集約対象
                LabeledContent("AirPods motion 利用可能", value: availability(probe.headphoneAvailable)) // TODO: 文言の集約対象
                LabeledContent("iPhone motion 利用可能", value: availability(probe.phoneAvailable)) // TODO: 文言の集約対象
                if let reason = probe.reason { Text(reason).foregroundStyle(.orange) } // TODO: 文言の集約対象
            } header: {
                Text("計測状態") // TODO: 文言の集約対象
            }
            Section {
                Picker("AirPods 前方軸（仮の既定値 −Z）", selection: $probe.axis) { // TODO: 文言の集約対象
                    ForEach(HeadTrackingProbe.ForwardAxis.allCases) { axis in
                        Text(axis.rawValue).tag(axis) // TODO: 文言の集約対象
                    }
                }
                .disabled(probe.isRecording)
                Text("正面でゼロ合わせし、右を向いてプラスになるか確認してください。軸の反転だけでは回転の符号は反転しません。") // TODO: 文言の集約対象
                    .font(.caption)
                LabeledContent("頭のヨー（°）", value: value(probe.headYaw)) // TODO: 文言の集約対象
                LabeledContent("iPhone のヨー（°）", value: value(probe.phoneYaw)) // TODO: 文言の集約対象
                LabeledContent("首の角度（°・右がプラス）", value: value(probe.neckYaw)) // TODO: 文言の集約対象
                Text(probe.calibrated ? "ゼロ合わせ済み" : "ゼロ合わせが必要です") // TODO: 文言の集約対象
                Button("ゼロ合わせ") { probe.zero() } // TODO: 文言の集約対象
                    .disabled(probe.isRecording)
                Text("軸の変更・再接続後は再度ゼロ合わせしてください。記録中は軸と基準を変更できません。") // TODO: 文言の集約対象
                    .font(.caption)
            } header: {
                Text("水平姿勢") // TODO: 文言の集約対象
            }
            Section {
                LabeledContent("更新頻度（直近5秒・Hz）", value: value(probe.frequencyHz)) // TODO: 文言の集約対象
                LabeledContent("最大サンプル間隔（直近5秒・ms）", value: value(probe.maximumInterval.map { $0 * 1000 })) // TODO: 文言の集約対象
                LabeledContent("間隔の標準偏差（直近5秒・ms）", value: value(probe.intervalDeviation.map { $0 * 1000 })) // TODO: 文言の集約対象
                LabeledContent("受信遅延・参考値（ms）", value: value(probe.receiveDelay.map { $0 * 1000 })) // TODO: 文言の集約対象
                Text("参考値 = 受信時の systemUptime − サンプル timestamp。BLE伝送前の遅延は含まず、コールバック待ち時間の影響を受けます。") // TODO: 文言の集約対象
                    .font(.caption)
                LabeledContent("途切れ（間隔 > 1秒・画面を開いてから）", value: String(probe.interruptionCount)) // TODO: 文言の集約対象
                Text("途切れは次のサンプル受信時に確定します。ロック中の継続は保証されません。復帰後の間隔とCSVを確認してください。") // TODO: 文言の集約対象
                    .font(.caption)
            } header: {
                Text("配信品質") // TODO: 文言の集約対象
            }
            Section {
                Text(probe.isRecording ? "記録中" : "記録停止中") // TODO: 文言の集約対象
                LabeledContent("記録件数", value: String(probe.recordCount)) // TODO: 文言の集約対象
                Button("記録開始") { probe.startRecording() } // TODO: 文言の集約対象
                    .disabled(probe.isRecording)
                Button("記録停止") { probe.stopRecording() } // TODO: 文言の集約対象
                    .disabled(!probe.isRecording)
                if let url = probe.csvURL {
                    ShareLink(item: url) { Text("CSVを共有") } // TODO: 文言の集約対象
                        .disabled(probe.isRecording)
                } else {
                    Text("CSVを共有（記録後に利用可能）") // TODO: 文言の集約対象
                        .foregroundStyle(.secondary)
                }
                if let error = probe.recordingError { Text(error).foregroundStyle(.red) } // TODO: 文言の集約対象
                Text("保存先: Documents/HeadTracking。時刻は起動からの秒数、角度は度。無効な値は空欄です。画面を閉じると計測・記録を停止します。") // TODO: 文言の集約対象
                    .font(.caption)
            } header: {
                Text("CSV記録") // TODO: 文言の集約対象
            }
        }
        .navigationTitle("頭部姿勢の計測（試作）") // TODO: 文言の集約対象
        .navigationBarTitleDisplayMode(.inline)
        .onAppear { probe.start() }
        .onDisappear { probe.stop() }
        .onChange(of: scenePhase) { _, phase in
            // Do not stop on inactive/background: observing actual lock behavior is the experiment.
            if phase == .active { probe.start() }
        }
    }

    private var connectionLabel: String {
        guard let connected = probe.connected else { return "接続通知待ち" } // TODO: 文言の集約対象
        return connected ? "接続" : "切断" // TODO: 文言の集約対象
    }

    private var authorizationLabel: String {
        switch probe.authorization {
        case .notDetermined: return "未確認" // TODO: 文言の集約対象
        case .restricted: return "制限あり" // TODO: 文言の集約対象
        case .denied: return "拒否" // TODO: 文言の集約対象
        case .authorized: return "許可済み" // TODO: 文言の集約対象
        @unknown default: return "不明" // TODO: 文言の集約対象
        }
    }

    private func availability(_ available: Bool) -> String {
        available ? "利用可能" : "利用不可" // TODO: 文言の集約対象
    }

    private func value(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "—" } // TODO: 文言の集約対象
        return String(format: "%.2f", value)
    }
}
