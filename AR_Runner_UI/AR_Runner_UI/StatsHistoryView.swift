import SwiftUI

// MARK: - 5. Stats Screen (走行データ表示)
struct StatsView: View {
    let onHistory: () -> Void
    let onBack: () -> Void // FIXED: Added this parameter

    var body: some View {
        ARScreen {
            ZStack(alignment: .topLeading) { // Wrapped in ZStack for the back button
                
                VStack(spacing: 0) {
                    Spacer().frame(height: 40)
                    
                    // Header
                    VStack(alignment: .leading, spacing: 4) {
                        Text("ACTIVITY")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(.arYellow)
                            .tracking(2)
                        Text("ナイスラン！")
                            .font(.system(size: 28, weight: .bold))
                            .foregroundColor(.white)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 28)
                    
                    Spacer().frame(height: 24)
                    
                    // Main Ring Card
                    VStack(spacing: 16) {
                        ZStack {
                            Circle()
                                .stroke(Color.arBorder, lineWidth: 6)
                                .frame(width: 180, height: 180)
                            
                            Circle()
                                .trim(from: 0.0, to: 0.87)
                                .stroke(
                                    AngularGradient(colors: [.arYellow, .yellow, .arYellow], center: .center),
                                    style: StrokeStyle(lineWidth: 10, lineCap: .round)
                                )
                                .frame(width: 180, height: 180)
                                .rotationEffect(.degrees(-90))
                            
                            VStack(spacing: 2) {
                                Text("シンクロ率")
                                    .font(.system(size: 12))
                                    .foregroundColor(.arGrayText)
                                Text("87%")
                                    .font(.system(size: 38, weight: .bold))
                                    .foregroundColor(.white)
                                Text("TARGET ACHIEVED")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(.arYellow)
                                    .padding(.horizontal, 6)
                                    .padding(.vertical, 2)
                                    .background(Color.arYellow.opacity(0.15))
                                    .clipShape(Capsule())
                            }
                        }
                        .frame(height: 200)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 24)
                    .background(Color.arCard)
                    .clipShape(RoundedRectangle(cornerRadius: 24))
                    .overlay(RoundedRectangle(cornerRadius: 24).stroke(Color.arBorder, lineWidth: 1))
                    .padding(.horizontal, 24)
                    
                    Spacer().frame(height: 16)
                    
                    // Stats Grid
                    Grid(horizontalSpacing: 12, verticalSpacing: 12) {
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
                    
                    Spacer()
                    
                    // Buttons
                    VStack(spacing: 12) {
                        ARButton("履歴を見る", icon: "clock.arrow.circlepath") {
                            onHistory()
                        }
                        ARButton("終了", style: .secondary) {
                            onBack() // Or wherever you want the close button to take them
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 48)
                }
                
                // FLOATING BACK BUTTON
                ARBackButton(action: onBack)
                    .padding(.leading, 24)
                    .padding(.top, 56)
            }
        }
    }
}

// MARK: - 6. History Screen (履歴一覧)
struct HistoryView: View {
    let onBack: () -> Void // Already has it, but making sure it calls ARBackButton below
    
    let historyItems = [
        ("6月15日", "2.1km", "11:24", "87%"),
        ("6月12日", "5.0km", "26:40", "92%"),
        ("6月08日", "3.4km", "18:15", "79%"),
        ("6月03日", "2.1km", "11:55", "84%")
    ]

    var body: some View {
        ARScreen {
            ZStack(alignment: .topLeading) {
                
                VStack(spacing: 0) {
                    Spacer().frame(height: 40)
                    
                    // Header
                    VStack(alignment: .leading, spacing: 4) {
                        Text("HISTORY")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(.arYellow)
                            .tracking(2)
                        Text("ランニング履歴")
                            .font(.system(size: 28, weight: .bold))
                            .foregroundColor(.white)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 28)
                    
                    Spacer().frame(height: 24)
                    
                    // History List
                    ScrollView {
                        VStack(spacing: 12) {
                            ForEach(historyItems, id: \.0) { item in
                                HStack {
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text(item.0)
                                            .font(.system(size: 16, weight: .bold))
                                            .foregroundColor(.white)
                                        Text("タイム: \(item.2)  |  シンクロ: \(item.3)")
                                            .font(.system(size: 13))
                                            .foregroundColor(.arGrayText)
                                    }
                                    Spacer()
                                    Text(item.1)
                                        .font(.system(size: 20, weight: .bold))
                                        .foregroundColor(.arYellow)
                                }
                                .padding(.horizontal, 20)
                                .padding(.vertical, 16)
                                .background(Color.arCard)
                                .clipShape(RoundedRectangle(cornerRadius: 16))
                                .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.arBorder, lineWidth: 1))
                            }
                        }
                        .padding(.horizontal, 24)
                    }
                }
                .padding(.top, 60) // Push content down to clear the back button
                
                // FLOATING BACK BUTTON
                ARBackButton(action: onBack)
                    .padding(.leading, 24)
                    .padding(.top, 56)
            }
        }
    }
}

// MARK: - Supporting Component
struct StatMiniCard: View {
    let title: String
    let value: String
    let unit: String
    
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.system(size: 12))
                .foregroundColor(.arGrayText)
            HStack(alignment: .firstTextBaseline, spacing: 2) {
                Text(value)
                    .font(.system(size: 22, weight: .bold))
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
        .padding(.vertical, 14)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.arBorder, lineWidth: 1))
    }
}
