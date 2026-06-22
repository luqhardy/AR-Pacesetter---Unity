import SwiftUI

// MARK: - 5. Stats Screen
struct StatsView: View {
    let onHistory: () -> Void
    let onBack: () -> Void

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
                                    .trim(from: 0.0, to: 0.87)
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
                                    Text("87%")
                                        .font(.system(size: 40, weight: .bold))
                                        .foregroundColor(.white)
                                    Text("TARGET ACHIEVED")
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
                                StatMiniCard(title: "距離", value: "2.13", unit: "km")
                                StatMiniCard(title: "タイム", value: "11:24", unit: "")
                            }
                            GridRow {
                                StatMiniCard(title: "平均ペース", value: "5'21\"", unit: "/km")
                                StatMiniCard(title: "消費カロリー", value: "164", unit: "kcal")
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

    let historyItems: [(date: String, dist: String, time: String, sync: String)] = [
        ("6月15日", "2.1km", "11:24", "87%"),
        ("6月12日", "5.0km", "26:40", "92%"),
        ("6月08日", "3.4km", "18:15", "79%"),
        ("6月03日", "2.1km", "11:55", "84%"),
    ]

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
                        ForEach(historyItems, id: \.date) { item in
                            HStack(spacing: 16) {
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(item.date)
                                        .font(.system(size: 16, weight: .bold))
                                        .foregroundColor(.white)
                                    HStack(spacing: 10) {
                                        Label(item.time, systemImage: "clock")
                                            .font(.system(size: 12))
                                            .foregroundColor(.arGrayText)
                                        Label(item.sync, systemImage: "waveform.path.ecg")
                                            .font(.system(size: 12))
                                            .foregroundColor(
                                                item.sync >= "90%" ? .arYellow : .arGrayText
                                            )
                                    }
                                }
                                Spacer()
                                Text(item.dist)
                                    .font(.system(size: 20, weight: .bold))
                                    .foregroundColor(.arYellow)
                            }
                            .padding(.horizontal, 20)
                            .padding(.vertical, 16)
                            .background(Color.arCard)
                            .clipShape(RoundedRectangle(cornerRadius: 16))
                            .overlay(
                                RoundedRectangle(cornerRadius: 16)
                                    .stroke(Color.arBorder, lineWidth: 1)
                            )
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 52)
                }
            }
        }
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
