import SwiftUI

// MARK: - 1. Connect Screen
struct ConnectView: View {
    @Binding var isConnected: Bool
    let onNext: () -> Void
    @State private var scanning = false
    @State private var pulse = false

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                Spacer()

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: "XREAL SETUP")
                    Text("グラスを接続\nしましょう")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                        .lineSpacing(2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)

                Spacer().frame(height: 44)

                // Glasses illustration
                ZStack {
                    // Fixed Glow rings to prevent compiler timeout
                    ForEach(0..<3, id: \.self) { i in
                        let baseOpacity = 0.15 - (Double(i) * 0.04)
                        let size = CGFloat(160 + (i * 50))
                        let delay = Double(i) * 0.4
                        
                        Circle()
                            .stroke(Color.arYellow.opacity(pulse ? 0.0 : baseOpacity), lineWidth: 1)
                            .frame(width: size, height: size)
                            .scaleEffect(pulse ? 1.3 : 1.0)
                            .animation(
                                .easeOut(duration: 1.5)
                                .repeatForever(autoreverses: false)
                                .delay(delay),
                                value: pulse
                            )
                    }

                    ZStack {
                        RoundedRectangle(cornerRadius: 28)
                            .fill(Color.arCard)
                            .frame(width: 136, height: 136)
                            .overlay(
                                RoundedRectangle(cornerRadius: 28)
                                    .stroke(
                                        isConnected ? Color.arYellow.opacity(0.6) : Color.arBorder,
                                        lineWidth: 1
                                    )
                            )

                        Image(systemName: "eyeglasses")
                            .font(.system(size: 60))
                            .foregroundColor(isConnected ? .arYellow : .white.opacity(0.85))
                    }
                }
                .frame(height: 220)
                .onAppear { pulse = true }

                Spacer().frame(height: 28)

                // Status pill
                HStack(spacing: 7) {
                    Circle()
                        .fill(isConnected ? Color.arYellow : scanning ? Color.orange : Color.arBorder)
                        .frame(width: 7, height: 7)
                        .scaleEffect(scanning ? 1.3 : 1.0)
                        .animation(.easeInOut(duration: 0.7).repeatForever(autoreverses: true), value: scanning)

                    Text(isConnected ? "XREAL One 接続済み" : scanning ? "スキャン中..." : "デバイスを検索しています")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(isConnected ? .arYellow : scanning ? .orange : .arGrayText)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 9)
                .background(Color.arCard)
                .clipShape(Capsule())
                .overlay(Capsule().stroke(Color.arBorder, lineWidth: 1))

                Spacer()

                VStack(spacing: 10) {
                    if isConnected {
                        ARButton("次へ", icon: "arrow.right") { onNext() }
                    } else {
                        ARButton("接続する", icon: "bolt.fill") {
                            scanning = true
                            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                                scanning = false
                                isConnected = true
                            }
                        }
                        ARButton("スキップ", style: .secondary) { onNext() }
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 52)
            }
        }
    }
}

// MARK: - 2. Onboarding Screen
struct OnboardingView: View {
    let onNext: () -> Void
    let onBack: () -> Void
    @State private var page = 0

    let pages: [(eyebrow: String, title: String, body: String)] = [
        ("WELCOME", "ランニングの\n未来へ", "ARグラスをつけて走るだけ。\nアバターがあなたのペースをリードします。"),
        ("AVATAR", "3m先に\n相棒。", "視線移動ゼロ。数値を見なくていい。\n感覚だけで最適なペースへ。"),
        ("SYNC", "全デバイスが\n連携する。", "Apple Watch・イヤホン・ARグラスが\n一体となってゾーンへ導きます。"),
    ]

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {


                // Hero
                ZStack(alignment: .bottom) {
                    LinearGradient(
                        colors: [Color.arCard, Color.arBG],
                        startPoint: .top, endPoint: .bottom
                    )
                    .frame(height: 300)
                    .clipShape(RoundedRectangle(cornerRadius: 32))
                    .padding(.horizontal, 20)

                    Ellipse()
                        .fill(Color.arYellow.opacity(0.07))
                        .frame(width: 260, height: 80)
                        .blur(radius: 24)
                        .offset(y: -20)

                    Image(systemName: "figure.run")
                        .font(.system(size: 100))
                        .foregroundColor(.white.opacity(0.9))
                        .offset(y: -40)

                    LinearGradient(
                        colors: [.clear, Color.arBG.opacity(0.65)],
                        startPoint: .top, endPoint: .bottom
                    )
                    .frame(height: 300)
                    .padding(.horizontal, 20)
                }

                Spacer().frame(height: 28)

                // Text pages
                TabView(selection: $page) {
                    ForEach(pages.indices, id: \.self) { i in
                        VStack(alignment: .leading, spacing: 10) {
                            ARLabel(text: pages[i].eyebrow)
                            Text(pages[i].title)
                                .font(.system(size: 28, weight: .bold))
                                .foregroundColor(.white)
                                .lineSpacing(2)
                            Text(pages[i].body)
                                .font(.system(size: 15))
                                .foregroundColor(.arGrayText)
                                .lineSpacing(5)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 28)
                        .tag(i)
                    }
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
                .frame(height: 155)

                // Progress dots
                HStack(spacing: 5) {
                    ForEach(pages.indices, id: \.self) { i in
                        Capsule()
                            .fill(i == page ? Color.arYellow : Color.arBorder)
                            .frame(width: i == page ? 22 : 6, height: 6)
                            .animation(.spring(response: 0.35, dampingFraction: 0.7), value: page)
                    }
                }
                .padding(.top, 10)
                .padding(.leading, 28)

                Spacer()

                ARButton(page < pages.count - 1 ? "次へ" : "はじめる", icon: "arrow.right") {
                    if page < pages.count - 1 {
                        withAnimation { page += 1 }
                    } else {
                        onNext()
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 52)
            }
        }
    }
}
