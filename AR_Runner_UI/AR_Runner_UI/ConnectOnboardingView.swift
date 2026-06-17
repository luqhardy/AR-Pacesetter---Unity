import SwiftUI

// MARK: - 1. Connect Screen (XREAL接続)
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
                VStack(alignment: .leading, spacing: 6) {
                    Text("XREALを接続")
                        .font(.system(size: 28, weight: .bold))
                        .foregroundColor(.white)
                    Text("しましょう")
                        .font(.system(size: 28, weight: .bold))
                        .foregroundColor(.white)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)

                Spacer().frame(height: 48)

                // Glasses illustration
                ZStack {
                    // Glow rings
                    ForEach(0..<3) { i in
                        // Extract calculations into separate constants so the compiler doesn't choke
                        let ringSize = CGFloat(160 + (i * 50))
                        let activeOpacity = pulse ? 0.0 : 0.15
                        let opacityReduction = Double(i) * 0.04
                        let finalOpacity = max(0, activeOpacity - opacityReduction)
                        let delayAmount = Double(i) * 0.4

                        Circle()
                            .stroke(Color.arYellow.opacity(finalOpacity), lineWidth: 1)
                            .frame(width: ringSize, height: ringSize)
                            .scaleEffect(pulse ? 1.3 : 1.0)
                            .animation(
                                .easeInOut(duration: 1.5)
                                .repeatForever(autoreverses: false)
                                .delay(delayAmount),
                                value: pulse
                            )
                    }

                    // Glasses icon
                    ZStack {
                        RoundedRectangle(cornerRadius: 24)
                            .fill(Color.arCard)
                            .frame(width: 140, height: 140)

                        Image(systemName: "eyeglasses") // Fixed system icon name
                            .font(.system(size: 64))
                            .foregroundColor(isConnected ? .arYellow : .white)
                    }
                }
                .frame(height: 220)
                .onAppear { pulse = true }

                Spacer().frame(height: 32)

                // Status
                HStack(spacing: 8) {
                    Circle()
                        .fill(isConnected ? Color.arYellow : Color.arGrayText)
                        .frame(width: 8, height: 8)
                    Text(isConnected ? "XREAL One 接続済み" : scanning ? "スキャン中..." : "デバイスを検索しています")
                        .font(.system(size: 14))
                        .foregroundColor(isConnected ? .arYellow : .arGrayText)
                }
                .padding(.bottom, 32)

                Spacer()

                // Buttons
                VStack(spacing: 12) {
                    if isConnected {
                        ARButton("次へ", icon: "arrow.right") { onNext() }
                    } else {
                        ARButton("接続", icon: "arrow.right") {
                            scanning = true
                            // Simulate connection
                            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                                scanning = false
                                isConnected = true
                            }
                        }
                        ARButton("スキップ", style: .secondary) { onNext() }
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 48)
            }
        }
    }
}

// MARK: - 2. Onboarding Screen
struct OnboardingView: View {
    let onNext: () -> Void
    let onBack: () -> Void // ADDED BACK ACTION
    @State private var page = 0

    let pages: [(title: String, subtitle: String, body: String)] = [
        ("ランニングの未来へ", "ようこそ。", "ARグラスをつけて走るだけ。\nアバターがあなたのペースをリードします。"),
        ("アバターが先導する", "3m先に相棒。", "視線移動ゼロ。数値を見なくていい。\n感覚だけで最適なペースへ"),
        ("全デバイスが連携", "シームレスに。", "Apple Watch・イヤホン・ARグラスが\n一体となってゾーンへ導きます。"),
    ]

    var body: some View {
        ARScreen {
            // ZStack used to overlay the back button on top of the content
            ZStack(alignment: .topLeading) {
                
                // Main Content
                VStack(spacing: 0) {
                    // Hero area
                    ZStack(alignment: .bottom) {
                        LinearGradient(
                            colors: [Color.arCard, Color.arBG],
                            startPoint: .top, endPoint: .bottom
                        )
                        .frame(height: 360)
                        .clipShape(RoundedRectangle(cornerRadius: 32))
                        .padding(.horizontal, 24)

                        VStack {
                            Spacer()
                            Image(systemName: "figure.run")
                                .font(.system(size: 120))
                                .foregroundColor(.white.opacity(0.85))
                                .offset(y: -32)
                        }
                        .frame(height: 320)

                        LinearGradient(
                            colors: [.clear, Color.arBG.opacity(0.6)],
                            startPoint: .top, endPoint: .bottom
                        )
                        .frame(height: 360)
                        .padding(.horizontal, 24)
                    }

                    Spacer().frame(height: 36)

                    // Text
                    TabView(selection: $page) {
                        ForEach(pages.indices, id: \.self) { i in
                            VStack(alignment: .leading, spacing: 8) {
                                Text(pages[i].title)
                                    .font(.system(size: 22, weight: .regular))
                                    .foregroundColor(.arGrayText)
                                Text(pages[i].subtitle)
                                    .font(.system(size: 32, weight: .bold))
                                    .foregroundColor(.white)
                                Text(pages[i].body)
                                    .font(.system(size: 15))
                                    .foregroundColor(.arGrayText)
                                    .lineSpacing(4)
                                    .padding(.top, 4)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 32)
                            .tag(i)
                        }
                    }
                    .tabViewStyle(.page(indexDisplayMode: .never))
                    .frame(height: 160)

                    // Page dots
                    HStack(spacing: 6) {
                        ForEach(pages.indices, id: \.self) { i in
                            Capsule()
                                .fill(i == page ? Color.arYellow : Color.arBorder)
                                .frame(width: i == page ? 20 : 6, height: 6)
                                .animation(.spring(), value: page)
                        }
                    }
                    .padding(.top, 12)

                    Spacer()

                    ARButton("次へ", icon: "arrow.right") {
                        if page < pages.count - 1 {
                            withAnimation { page += 1 }
                        } else {
                            onNext()
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 48)
                }
                .padding(.top, 72) // Pushed down slightly to make room for back button
                
                // BACK BUTTON OVERLAY
                ARBackButton(action: onBack)
                    .padding(.leading, 24)
                    .padding(.top, 16)
            }
        }
    }
}
