import SwiftUI
import UIKit

// MARK: - 開発者モード
//
// 第1期の成果物は「技術限界データのCSV」そのもの(基本設計書 §1.2)なのに、
// 書き出し先は Unity の persistentDataPath/RunLogs/ で、これまでアプリからは
// 存在すら見えていなかった。取り出すには Mac の Xcode でコンテナを
// ダウンロードするしかなく、トラックで走った直後に確認する手段が無かった。
//
// この画面で解決するのは2つ:
//   1. 走行ログCSVの一覧と共有(AirDrop / ファイル / メール など何でも)
//   2. 「今この端末で何が効いているか」の可視化 — M2Pが実測なのか、IMUの供給元、
//      グラスの画角、GPS判定の状態。実機でしか判らないことを1画面に集める
//
// 値はすべて Unity のスナップショット(DevDiagnostics)をそのまま出す。
// **ここで数字を作らない** — 未計測は -1 のまま見せる。

struct DevModeView: View {

    @ObservedObject private var bridge = UnityBridge.shared
    @Environment(\.dismiss) private var dismiss

    @State private var shareItem: ShareItem?
    @State private var autoRefresh = true

    private let accent = Color(hex: "#c7f219")
    private let cardColor = Color(hex: "#1C1C1E")
    private let secondaryText = Color.white.opacity(0.5)

    /// 1秒ごとの自動更新(走行中の値が動くのを見たいため)
    private let tick = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    logSection
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
            ActivityView(items: [item.url])
        }
    }

    private func refresh() {
        bridge.requestDiagnostics()
        bridge.requestLogFiles()
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
                        Button { shareItem = ShareItem(url: file.url) } label: {
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
    let url: URL
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
