import SwiftUI

// MARK: - 5. Stats Screen
struct StatsView: View {
    let onHistory: () -> Void
    let onBack: () -> Void
    let onFinish: () -> Void

    // Unityから届いた走行結果 (SessionEnded)。届くまで・届かなかったときは「-」。
    // 以前は作り物の数字(87%・2.13km・11:24)を出していた — 実際の結果と見分けがつかない
    @ObservedObject private var bridge = UnityBridge.shared

    /// 画面を開いた時刻 ≒ 走行終了。結果を待つ時間の基準
    @State private var shownAt = Date()
    @State private var now = Date()

    private var result: UnityBridge.SessionResult? { bridge.lastResult }

    private var resultState: SessionResultState {
        .resolve(hasResult: result != nil, secondsSinceEnd: now.timeIntervalSince(shownAt))
    }

    private var syncPercent: Int? {
        result.map { Int($0.averageSync.rounded()) }
    }

    private var rankBadgeText: String {
        switch resultState {
        case .ready:       return result.map { "GRADE \($0.grade)・\($0.rank)" } ?? ""
        case .waiting:     return "集計中…"
        case .unavailable: return "結果なし"
        }
    }

    private var distanceStr: String {
        result.map { String(format: "%.2f", $0.distanceKm) } ?? "-"
    }

    private var timeStr: String {
        guard let r = result else { return "-" }
        let total = Int(r.elapsedSeconds)
        return String(format: "%02d:%02d", total / 60, total % 60)
    }

    private var paceStr: String {
        guard let r = result, r.distanceKm > 0.01 else { return "-" }
        let minPerKm = (r.elapsedSeconds / 60.0) / r.distanceKm
        let minutes = Int(minPerKm)
        let seconds = Int((minPerKm - Double(minutes)) * 60.0)
        return String(format: "%d'%02d\"", minutes, seconds)
    }

    private var kcalStr: String {
        guard let r = result else { return "-" }
        // Unity側がオンボーディングの実体重で算出した値を優先。
        // 旧データ等で未提供(0)の場合のみ体重60kg想定で概算
        return r.calories > 0
            ? String(Int(r.calories.rounded()))
            : String(Int(r.distanceKm * 60.0 * 1.05))
    }

    /// 値が無いときは単位を出さない(「- km」にしない)
    private func unit(_ text: String, for value: String) -> String { value == "-" ? "" : text }

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
                                    .trim(from: 0.0, to: CGFloat(syncPercent ?? 0) / 100.0)
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
                                    Text(syncPercent.map { "\($0)%" } ?? "-")
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

                        if resultState == .unavailable {
                            Text("走行結果を受け取れませんでした。この走行は履歴に残っていない可能性があります")
                                .font(.system(size: 13))
                                .foregroundColor(.arGrayText)
                                .multilineTextAlignment(.center)
                                .padding(.horizontal, 32)
                                .padding(.top, 12)
                        }

                        Spacer().frame(height: 12)

                        // Stats grid
                        Grid(horizontalSpacing: 10, verticalSpacing: 10) {
                            GridRow {
                                StatMiniCard(title: "距離", value: distanceStr, unit: unit("km", for: distanceStr))
                                StatMiniCard(title: "タイム", value: timeStr, unit: "")
                            }
                            GridRow {
                                StatMiniCard(title: "平均ペース", value: paceStr, unit: unit("/km", for: paceStr))
                                StatMiniCard(title: "消費カロリー", value: kcalStr, unit: unit("kcal", for: kcalStr))
                            }
                        }
                        .padding(.horizontal, 24)

                        Spacer().frame(height: 24)

                        // Buttons
                        VStack(spacing: 10) {
                            ARButton("履歴を見る", icon: "clock.arrow.circlepath") { onHistory() }
                            ARButton("終了", style: .secondary) { onFinish() }
                        }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                    }
                }
            }
        }
        .task {
            // 待っても届かなければ「結果なし」に切り替える(届けばその時点で表示される)
            try? await Task.sleep(for: .seconds(SessionResultState.waitSeconds))
            now = Date()
        }
    }
}

// MARK: - 6. History Screen
struct HistoryView: View {
    let onBack: () -> Void
    /// ゴースト競走の開始(RunSettings.ghostDateIso設定後に呼ばれる)
    var onStartGhost: (() -> Void)? = nil

    // Unityのセッション保存(JSON DB)から取得した履歴(端末に保存した前回分を含む)。
    // 無いときは空の案内を出す — 以前は作り物の4件(6月15日 2.1km …)を出していた
    @ObservedObject private var bridge = UnityBridge.shared

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
                            emptyState
                        } else {
                            // 各記録に「この記録と競走」(ゴースト)ボタン付き
                            ForEach(bridge.history) { entry in
                                historyRow(
                                    date: entry.dateLabel,
                                    dist: String(format: "%.1fkm", entry.distanceKm),
                                    time: entry.timeLabel,
                                    sync: "\(Int(entry.averageSync.rounded()))%",
                                    highlightSync: entry.averageSync >= 90,
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

    /// 記録が無いとき。Unity は走行画面で初めて起動するので、一度も履歴を受け取っていない
    /// (端末に保存分も無い)間の空は「記録が無い」とは限らない — 文言を分ける
    private var emptyState: some View {
        VStack(spacing: 8) {
            Image(systemName: "figure.run")
                .font(.system(size: 28))
                .foregroundColor(.arGrayText)
            Text(bridge.historyLoaded ? "まだ記録がありません" : "記録をまだ読み込んでいません")
                .font(.system(size: 16, weight: .semibold))
                .foregroundColor(.white)
            Text(bridge.historyLoaded
                 ? "走り終えると、ここに記録が残ります"
                 : "ランニングを一度始めると、これまでの記録が表示されます")
                .font(.system(size: 13))
                .foregroundColor(.arGrayText)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 48)
        .padding(.horizontal, 20)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color.arBorder, lineWidth: 1)
        )
    }

    // 履歴1件分の行。ghostAction があると「この記録と競走」ボタンを表示
    @ViewBuilder
    private func historyRow(date: String, dist: String, time: String, sync: String,
                            highlightSync: Bool, ghostAction: (() -> Void)?) -> some View {
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
                            .foregroundColor(highlightSync ? .arYellow : .arGrayText)
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
