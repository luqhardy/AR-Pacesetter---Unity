import SwiftUI

// MARK: - 5. Stats Screen
struct StatsView: View {
    let onHistory: () -> Void
    let onBack: () -> Void

    // Unityから届いた走行結果 (SessionEnded)。無ければモック値で表示(UI単体開発用)
    @ObservedObject private var bridge = UnityBridge.shared

    private var result: UnityBridge.SessionResult? { bridge.lastResult }

    private var syncPercent: Int {
        result.map { Int($0.averageSync.rounded()) } ?? 87
    }

    private var rankBadgeText: String {
        result.map { "GRADE \($0.grade)・\($0.rank)" } ?? "TARGET ACHIEVED"
    }

    private var distanceStr: String {
        result.map { String(format: "%.2f", $0.distanceKm) } ?? "2.13"
    }

    private var timeStr: String {
        guard let r = result else { return "11:24" }
        let total = Int(r.elapsedSeconds)
        return String(format: "%02d:%02d", total / 60, total % 60)
    }

    private var paceStr: String {
        guard let r = result, r.distanceKm > 0.01 else { return "5'21\"" }
        let minPerKm = (r.elapsedSeconds / 60.0) / r.distanceKm
        let minutes = Int(minPerKm)
        let seconds = Int((minPerKm - Double(minutes)) * 60.0)
        return String(format: "%d'%02d\"", minutes, seconds)
    }

    private var kcalStr: String {
        // 概算: 体重60kg想定 × 距離(km) × 1.05 (ランニングの標準推定式)
        result.map { String(Int($0.distanceKm * 60.0 * 1.05)) } ?? "164"
    }

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar row: back button left-aligned
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                // Scrollable content below nav bar
                ScrollView(showsIndicators: false) {
                    VStack(spacing: 0) {
                        // Header
                        VStack(alignment: .leading, spacing: 4) {
                            ARLabel(text: "ACTIVITY")
                            Text("ナイスラン！")
                                .font(.system(size: 30, weight: .bold))
                                .foregroundColor(.white)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 28)

                        Spacer().frame(height: 24)

                        // Sync ring card
                        VStack(spacing: 20) {
                            ZStack {
                                Circle()
                                    .stroke(Color.arBorder, lineWidth: 7)
                                    .frame(width: 172, height: 172)

                                Circle()
                                    .trim(from: 0.0, to: CGFloat(syncPercent) / 100.0)
                                    .stroke(
                                        AngularGradient(
                                            colors: [Color.arYellow.opacity(0.5), Color.arYellow, Color.arYellow.opacity(0.5)],
                                            center: .center
                                        ),
                                        style: StrokeStyle(lineWidth: 10, lineCap: .round)
                                    )
                                    .frame(width: 172, height: 172)
                                    .rotationEffect(.degrees(-90))

                                VStack(spacing: 3) {
                                    Text("シンクロ率")
                                        .font(.system(size: 11))
                                        .foregroundColor(.arGrayText)
                                    Text("\(syncPercent)%")
                                        .font(.system(size: 40, weight: .bold))
                                        .foregroundColor(.white)
                                    Text(rankBadgeText)
                                        .font(.system(size: 9, weight: .bold))
                                        .foregroundColor(.arYellow)
                                        .tracking(1)
                                        .padding(.horizontal, 7)
                                        .padding(.vertical, 3)
                                        .background(Color.arYellowDim)
                                        .clipShape(Capsule())
                                }
                            }
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 28)
                        .background(Color.arCard)
                        .clipShape(RoundedRectangle(cornerRadius: 24))
                        .overlay(
                            RoundedRectangle(cornerRadius: 24)
                                .stroke(Color.arBorder, lineWidth: 1)
                        )
                        .padding(.horizontal, 24)

                        Spacer().frame(height: 12)

                        // Stats grid
                        Grid(horizontalSpacing: 10, verticalSpacing: 10) {
                            GridRow {
                                StatMiniCard(title: "距離", value: distanceStr, unit: "km")
                                StatMiniCard(title: "タイム", value: timeStr, unit: "")
                            }
                            GridRow {
                                StatMiniCard(title: "平均ペース", value: paceStr, unit: "/km")
                                StatMiniCard(title: "消費カロリー", value: kcalStr, unit: "kcal")
                            }
                        }
                        .padding(.horizontal, 24)

                        Spacer().frame(height: 24)

                        // Buttons
                        VStack(spacing: 10) {
                            ARButton("履歴を見る", icon: "clock.arrow.circlepath") { onHistory() }
                            ARButton("終了", style: .secondary) { onBack() }
                        }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                    }
                }
            }
        }
    }
}

// MARK: - 6. History Screen
struct HistoryView: View {
    let onBack: () -> Void
    /// ゴースト競走の開始(RunSettings.ghostDateIso設定後に呼ばれる)
    var onStartGhost: (() -> Void)? = nil

    // Unityのセッション保存(JSON DB)から取得した履歴。未着時はモック表示
    @ObservedObject private var bridge = UnityBridge.shared

    private let mockItems: [(date: String, dist: String, time: String, sync: String)] = [
        ("6月15日", "2.1km", "11:24", "87%"),
        ("6月12日", "5.0km", "26:40", "92%"),
        ("6月08日", "3.4km", "18:15", "79%"),
        ("6月03日", "2.1km", "11:55", "84%"),
    ]

    private var historyItems: [(date: String, dist: String, time: String, sync: String)] {
        guard !bridge.history.isEmpty else { return mockItems }
        return bridge.history.map { entry in
            (date: entry.dateLabel,
             dist: String(format: "%.1fkm", entry.distanceKm),
             time: entry.timeLabel,
             sync: "\(Int(entry.averageSync.rounded()))%")
        }
    }

    private func startGhostRace(with entry: UnityBridge.HistoryEntry) {
        RunSettings.shared.ghostDateIso = entry.dateIso
        onStartGhost?()
    }

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar row
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: "HISTORY")
                    Text("ランニング履歴")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 20)

                ScrollView(showsIndicators: false) {
                    VStack(spacing: 10) {
                        if bridge.history.isEmpty {
                            // モック表示(Unity未接続のUI開発時)— ゴーストボタン無し
                            ForEach(historyItems, id: \.date) { item in
                                historyRow(date: item.date, dist: item.dist,
                                           time: item.time, sync: item.sync, ghostAction: nil)
                            }
                        } else {
                            // 実データ: 各記録に「この記録と競走」(ゴースト)ボタン付き
                            ForEach(bridge.history) { entry in
                                historyRow(
                                    date: entry.dateLabel,
                                    dist: String(format: "%.1fkm", entry.distanceKm),
                                    time: entry.timeLabel,
                                    sync: "\(Int(entry.averageSync.rounded()))%",
                                    ghostAction: { startGhostRace(with: entry) }
                                )
                            }
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 52)
                }
            }
            .onAppear {
                // Unityのセッション保存(JSON DB)から履歴を取得
                bridge.requestHistory()
            }
        }
    }

    // 履歴1件分の行。ghostAction があると「この記録と競走」ボタンを表示
    @ViewBuilder
    private func historyRow(date: String, dist: String, time: String, sync: String,
                            ghostAction: (() -> Void)?) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: 16) {
                VStack(alignment: .leading, spacing: 5) {
                    Text(date)
                        .font(.system(size: 16, weight: .bold))
                        .foregroundColor(.white)
                    HStack(spacing: 10) {
                        Label(time, systemImage: "clock")
                            .font(.system(size: 12))
                            .foregroundColor(.arGrayText)
                        Label(sync, systemImage: "waveform.path.ecg")
                            .font(.system(size: 12))
                            .foregroundColor(sync >= "90%" ? .arYellow : .arGrayText)
                    }
                }
                Spacer()
                Text(dist)
                    .font(.system(size: 20, weight: .bold))
                    .foregroundColor(.arYellow)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 16)

            if let ghostAction {
                Button(action: ghostAction) {
                    HStack(spacing: 6) {
                        Image(systemName: "figure.run.circle")
                            .font(.system(size: 13))
                        Text("この記録と競走（ゴースト）")
                            .font(.system(size: 13, weight: .semibold))
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
                    .background(Color.arYellowDim)
                    .foregroundColor(.arYellow)
                }
            }
        }
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color.arBorder, lineWidth: 1)
        )
    }
}

// MARK: - Stat Mini Card
struct StatMiniCard: View {
    let title: String
    let value: String
    let unit: String

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title)
                .font(.system(size: 11))
                .foregroundColor(.arGrayText)
            HStack(alignment: .firstTextBaseline, spacing: 3) {
                Text(value)
                    .font(.system(size: 24, weight: .bold))
                    .foregroundColor(.white)
                if !unit.isEmpty {
                    Text(unit)
                        .font(.system(size: 12))
                        .foregroundColor(.arGrayText)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 16)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color.arBorder, lineWidth: 1)
        )
    }
}
