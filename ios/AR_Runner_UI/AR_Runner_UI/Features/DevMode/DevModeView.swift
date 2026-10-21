import SwiftUI
import UIKit
import Combine

// MARK: - 開発者モード
//
// 第1期の成果物は「技術限界データのCSV」そのもの(基本設計書 §1.2)なのに、
// 書き出し先は Unity の persistentDataPath/RunLogs/ で、これまでアプリからは
// 存在すら見えていなかった。取り出すには Mac の Xcode でコンテナを
// ダウンロードするしかなく、トラックで走った直後に確認する手段が無かった。
//
// この画面で解決するのは3つ:
//   1. 走行ログCSVの一覧と共有(AirDrop / ファイル / メール など何でも)
//   2. 「今この端末で何が効いているか」の可視化 — M2Pが実測なのか、IMUの供給元、
//      グラスの画角、GPS判定の状態。実機でしか判らないことを1画面に集める
//   3. 実地テストの記録 — 1〜2 と端末情報を Docs/field-tests/TEMPLATE.md に貼れる形で書き出す
//
// 値はすべて Unity のスナップショット(DevDiagnostics)をそのまま出す。
// **ここで数字を作らない** — 未計測は -1 のまま見せる。

struct DevModeView: View {

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var external = ExternalDisplayManager.shared
    @Environment(\.dismiss) private var dismiss

    @State private var shareItem: ShareItem?
    @State private var autoRefresh = true
    /// 準備画面(S4)で ARグラスなしでも「次へ」を押せるようにする(既定 OFF — 仕様ではグラスは必須)
    @AppStorage(DeviceGate.allowRunWithoutGlassesKey) private var allowRunWithoutGlasses = false
    @State private var reportCopied = false

    private let accent = Color(red: 199.0 / 255.0, green: 242.0 / 255.0, blue: 25.0 / 255.0)
    private let cardColor = Color(red: 28.0 / 255.0, green: 28.0 / 255.0, blue: 30.0 / 255.0)
    private let secondaryText = Color.white.opacity(0.5)

    /// 1秒ごとの自動更新(走行中の値が動くのを見たいため)
    private let tick = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    reportSection
                    logSection
                    visibilitySection
                    indoorTestSection
                    statsSection
                }
                .padding(.horizontal, 20)
                .padding(.vertical, 12)
            }
            .background(Color.black.ignoresSafeArea())
            .navigationTitle("開発者モード")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("閉じる") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button { refresh() } label: { Image(systemName: "arrow.clockwise") }
                }
            }
        }
        .preferredColorScheme(.dark)
        .onAppear { refresh() }
        .onReceive(tick) { _ in if autoRefresh { bridge.requestDiagnostics() } }
        .sheet(item: $shareItem) { item in
            ActivityView(items: item.items)
        }
    }

    private func refresh() {
        bridge.requestDiagnostics()
        bridge.requestLogFiles()
    }

    // MARK: - 実地テストの書き出し

    /// T1〜T9 の記録用。端末・グラス・M2P・電池と発熱(§10)・アバター非表示の履歴・Unity の状態を Markdown にする
    /// (Docs/field-tests/TEMPLATE.md の「開発者モードからの書き出し」に貼る)。CSV は下の一覧から別に共有する
    private var reportSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionHeader("実地テストの書き出し",
                          subtitle: "端末・M2P・電池と発熱・非表示の履歴・状態を TEMPLATE.md に貼れる形で")
            HStack(spacing: 10) {
                Button {
                    UIPasteboard.general.string = makeReport()
                    reportCopied = true
                    DispatchQueue.main.asyncAfter(deadline: .now() + 2) { reportCopied = false }
                } label: {
                    Label(reportCopied ? "コピーしました" : "レポートをコピー",
                          systemImage: reportCopied ? "checkmark" : "doc.on.doc")
                        .font(.system(size: 14, weight: .semibold))
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 11)
                        .background(accent, in: RoundedRectangle(cornerRadius: 12))
                        .foregroundStyle(.black)
                }
                Button { shareItem = ShareItem(items: [makeReport()]) } label: {
                    Image(systemName: "square.and.arrow.up")
                        .font(.system(size: 15, weight: .semibold))
                        .frame(width: 48)
                        .padding(.vertical, 11)
                        .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
                        .foregroundStyle(.white)
                }
            }
        }
    }

    private func makeReport() -> String {
        let info = Bundle.main.infoDictionary
        let version = info?["CFBundleShortVersionString"] as? String ?? "?"
        let build = info?["CFBundleVersion"] as? String ?? "?"
        return FieldTestReport(
            generatedAt: Date(),
            device: .init(model: Self.deviceModel,
                          systemVersion: UIDevice.current.systemVersion,
                          appVersion: "\(version) (\(build))"),
            glassesStatus: glassesStatusText,
            latency: .init(lastMs: bridge.motionToPhotonMs,
                           maxMs: bridge.motionToPhotonMaxMs,
                           overBudgetRatio: bridge.motionToPhotonOverBudgetRatio,
                           sampleCount: bridge.motionToPhotonSampleCount),
            visibilityEvents: bridge.visibilityLog.events,
            diagnostics: bridge.diagnostics.map { (key: $0.key, value: $0.value) },
            power: PowerMonitor.shared.lastRun,
            currentPower: PowerMonitor.shared.sample()
        ).markdown()
    }

    /// 準備画面と同じ判定(開発者モードは日本語のみなので文言はここで持つ)
    private var glassesStatusText: String {
        switch GlassRowStatus.resolve(glassesConnected: external.isGlassesConnected,
                                      mirroring: external.isMirroring,
                                      manualConnect: false) {
        case .connected: return "接続済み"
        case .mirroring: return "ミラーリング中(グラス専用の画面が無い)"
        case .manual: return "手動"
        case .notDetected: return "未接続"
        }
    }

    /// 機種の識別子(例 "iPhone16,2")。UIDevice.model は "iPhone" しか返さない
    private static var deviceModel: String {
        var info = utsname()
        uname(&info)
        return withUnsafeBytes(of: &info.machine) { raw in
            String(decoding: raw.prefix(while: { $0 != 0 }), as: UTF8.self)
        }
    }

    // MARK: - 走行ログCSV

    private var logSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionHeader("走行ログ CSV (F-11)",
                          subtitle: "100Hz・§5.2の9列。タップで共有")

            if bridge.logFiles.isEmpty {
                Text(bridge.logDirectory.isEmpty
                     ? "Unity未起動、またはまだ一度も走行していません"
                     : "ログがありません (\(bridge.logDirectory))")
                    .font(.system(size: 13))
                    .foregroundStyle(secondaryText)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            } else {
                VStack(spacing: 0) {
                    ForEach(bridge.logFiles) { file in
                        Button { shareItem = ShareItem(items: [file.url]) } label: {
                            HStack(spacing: 12) {
                                Image(systemName: "doc.text")
                                    .foregroundStyle(accent)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(file.name)
                                        .font(.system(size: 14, weight: .medium))
                                        .foregroundStyle(.white)
                                    Text("\(file.modifiedLabel) ・ \(file.sizeLabel)")
                                        .font(.system(size: 12))
                                        .foregroundStyle(secondaryText)
                                }
                                Spacer()
                                Image(systemName: "square.and.arrow.up")
                                    .foregroundStyle(secondaryText)
                            }
                            .padding(.horizontal, 14)
                            .padding(.vertical, 12)
                        }
                        if file.id != bridge.logFiles.last?.id {
                            Divider().background(Color.white.opacity(0.08)).padding(.leading, 44)
                        }
                    }
                }
                .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    // MARK: - アバター非表示の履歴

    /// 走行中にアバターが消えた区間(新しい順)。走っている本人は紫のバナーを読めないので、
    /// 走行後にここで「いつ・何秒・なぜ」を確かめる。アプリを再起動しても残る(最大100件)
    private var visibilitySection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                sectionHeader("アバター非表示の履歴",
                              subtitle: "消えた時刻・見えなかった秒数・理由(新しい順)")
                Spacer()
                if !bridge.visibilityLog.events.isEmpty {
                    Button("消去") { bridge.clearVisibilityLog() }
                        .font(.system(size: 13, weight: .semibold))
                        .tint(accent)
                }
            }

            if bridge.visibilityLog.events.isEmpty {
                Text("まだ一度も消えていません")
                    .font(.system(size: 13))
                    .foregroundStyle(secondaryText)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            } else {
                VStack(spacing: 0) {
                    ForEach(bridge.visibilityLog.events) { event in
                        VStack(alignment: .leading, spacing: 3) {
                            HStack(spacing: 8) {
                                Text(Self.timeFormatter.string(from: event.hiddenAt))
                                    .font(.system(size: 12, weight: .semibold, design: .monospaced))
                                    .foregroundStyle(.white)
                                Text(event.durationSeconds.map { String(format: "%.1f秒", $0) } ?? "継続中")
                                    .font(.system(size: 12, design: .monospaced))
                                    .foregroundStyle(event.shownAt == nil ? Color.orange : secondaryText)
                            }
                            Text(event.reason)
                                .font(.system(size: 12))
                                .foregroundStyle(.white.opacity(0.85))
                                .fixedSize(horizontal: false, vertical: true)
                                .textSelection(.enabled)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        if event.id != bridge.visibilityLog.events.last?.id {
                            Divider().background(Color.white.opacity(0.08)).padding(.leading, 14)
                        }
                    }
                }
                .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    private static let timeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "MM/dd HH:mm:ss"
        return f
    }()

    // MARK: - 屋内テスト

    /// Unity が報告している GPSロスト判定(F-09/F-10)の状態。応答が無ければ nil。
    /// **この画面では状態を持たない** — 表示は常に Unity の実際の値(Diagnostics の
    /// `gps.autoLostHandling`)から作る。体験モードは開始時にOFF・終了時に元へ戻すので、
    /// 画面側で覚えた値はすぐに実態とずれる
    private var gpsLostHandlingOn: Bool? {
        GpsLostHandlingReading.parse(
            bridge.diagnostics.first(where: { $0.key == GpsLostHandlingReading.diagnosticsKey })?.value)
    }

    private var indoorTestSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            sectionHeader("屋内テスト",
                          subtitle: "GPSロスト判定(F-09 慣性 / F-10 フェードアウト)")

            VStack(alignment: .leading, spacing: 8) {
                Toggle(isOn: Binding(
                    get: { gpsLostHandlingOn ?? true },
                    set: { on in
                        bridge.setGpsLostHandling(enabled: on)
                        bridge.requestDiagnostics()   // Unity の実際の値で表示を更新する
                    }
                )) {
                    Text("GPSロストでアバターを消す")
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(.white)
                }
                .tint(accent)
                .disabled(gpsLostHandlingOn == nil)

                // 屋内では一度良い測位を掴んだ後に精度が10m超へ落ち、5秒でアバターが消える。
                // 仕様(F-10)どおりの安全動作なので既定はON。屋内の動作確認のときだけ切る
                Text(gpsLostHandlingOn == false
                     ? "OFF — 屋内テスト用。トラックでは必ずONに戻すこと(GPSが切れても警告が出ない)。アプリを再起動するとONに戻ります"
                     : "ON(仕様どおり)。屋内で走ってアバターが消えるときだけOFFにする")
                    .font(.system(size: 12))
                    .foregroundStyle(gpsLostHandlingOn == false ? Color.orange : secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .background(cardColor, in: RoundedRectangle(cornerRadius: 12))

            // タスク表 S4: ARグラスは必須(未接続では準備画面から先へ進めない)。
            // iPhone の画面だけで動作を確かめるときだけ外す
            VStack(alignment: .leading, spacing: 8) {
                Toggle(isOn: $allowRunWithoutGlasses) {
                    Text("ARグラスなしで走行を始める")
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(.white)
                }
                .tint(accent)
                Text(allowRunWithoutGlasses
                     ? "ON — 準備画面でグラスが未接続でも「次へ」を押せます(iPhone の画面で走る)。走者に渡す前に OFF に戻すこと"
                     : "OFF(仕様どおり)。グラスがつながるまで準備画面から先へ進めない")
                    .font(.system(size: 12))
                    .foregroundStyle(allowRunWithoutGlasses ? Color.orange : secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    // MARK: - 状態スナップショット

    private var statsSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                sectionHeader("状態", subtitle: "Unityのスナップショット (未計測は -1)")
                Spacer()
                Toggle("", isOn: $autoRefresh).labelsHidden().tint(accent)
            }

            if bridge.diagnostics.isEmpty {
                Text("Unityから応答がありません(UnityFramework未リンクの可能性)")
                    .font(.system(size: 13))
                    .foregroundStyle(secondaryText)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            } else {
                VStack(spacing: 0) {
                    ForEach(bridge.diagnostics) { row in
                        HStack(alignment: .top, spacing: 12) {
                            Text(row.key)
                                .font(.system(size: 12, weight: .medium, design: .monospaced))
                                .foregroundStyle(secondaryText)
                                .frame(width: 130, alignment: .leading)
                            Text(row.value)
                                .font(.system(size: 12, design: .monospaced))
                                .foregroundStyle(.white)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .textSelection(.enabled)
                        }
                        .padding(.horizontal, 14)
                        .padding(.vertical, 7)
                    }
                }
                .padding(.vertical, 5)
                .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    private func sectionHeader(_ title: String, subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .font(.system(size: 16, weight: .bold, design: .rounded))
                .foregroundStyle(.white)
            Text(subtitle)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(secondaryText)
        }
    }
}

// MARK: - 共有シート

private struct ShareItem: Identifiable {
    let id = UUID()
    /// ファイルURL(CSV)または文字列(レポート)
    let items: [Any]
}

/// UIActivityViewController の薄いラッパー。ファイルURLをそのまま渡せば
/// AirDrop / ファイルへ保存 / メール等すべてが使える。
private struct ActivityView: UIViewControllerRepresentable {
    let items: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}

#Preview {
    DevModeView()
}
