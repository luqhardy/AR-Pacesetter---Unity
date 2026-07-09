import SwiftUI
import MapKit
import Combine

// MARK: - 3. Course Setup Screen
struct CourseSetupView: View {
    let onStart: () -> Void
    let onSettings: () -> Void
    let onBack: () -> Void

    @State private var region = MKCoordinateRegion(
        center: CLLocationCoordinate2D(latitude: 34.6937, longitude: 135.5023),
        span: MKCoordinateSpan(latitudeDelta: 0.02, longitudeDelta: 0.02)
    )

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Map fills the top portion; back button floats over it
                ZStack(alignment: .topLeading) {
                    Map(coordinateRegion: $region)
                        .environment(\.locale, Locale(identifier: "ja_JP"))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .overlay(
                            VStack {
                                Spacer()
                                LinearGradient(
                                    colors: [.clear, Color.arBG.opacity(0.5)],
                                    startPoint: .top, endPoint: .bottom
                                )
                                .frame(height: 60)
                            }
                        )
                        .ignoresSafeArea(edges: .top)

                    // Back button floats over map with notch clearance
                    ARBackButton(action: onBack)
                        .padding(.leading, 20)
                        .padding(.top, 56)
                }

                // Bottom info panel (never overlaps map content)
                VStack(spacing: 0) {
                    HStack(spacing: 12) {
                        VStack(spacing: 3) {
                            Circle().fill(Color.arYellow).frame(width: 9, height: 9)
                            ForEach(0..<4) { _ in
                                Rectangle()
                                    .fill(Color.arBorder)
                                    .frame(width: 1, height: 4)
                            }
                            Circle().fill(Color.arGrayText).frame(width: 9, height: 9)
                        }

                        VStack(alignment: .leading, spacing: 6) {
                            Text("三宮駅")
                                .font(.system(size: 15, weight: .semibold))
                                .foregroundColor(.white)
                            Text("市役所前")
                                .font(.system(size: 15))
                                .foregroundColor(.arGrayText)
                        }

                        Spacer()

                        VStack(alignment: .trailing, spacing: 2) {
                            Text("2.1")
                                .font(.system(size: 26, weight: .bold))
                                .foregroundColor(.arYellow)
                            Text("km")
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.top, 22)
                    .padding(.bottom, 18)

                    ARDivider()
                        .padding(.horizontal, 24)

                    HStack(spacing: 10) {
                        Button {
                            onSettings()
                        } label: {
                            HStack(spacing: 8) {
                                Image(systemName: "gearshape")
                                    .font(.system(size: 14))
                                Text("設定")
                                    .font(.system(size: 15, weight: .medium))
                            }
                            .frame(maxWidth: .infinity)
                            .frame(height: 52)
                            .background(Color.arCard)
                            .foregroundColor(.white)
                            .clipShape(RoundedRectangle(cornerRadius: 26))
                            .overlay(RoundedRectangle(cornerRadius: 26).stroke(Color.arBorder, lineWidth: 1))
                        }

                        Button {
                            onStart()
                        } label: {
                            HStack(spacing: 8) {
                                Image(systemName: "play.fill")
                                    .font(.system(size: 13))
                                Text("開始")
                                    .font(.system(size: 15, weight: .bold))
                            }
                            .frame(maxWidth: .infinity)
                            .frame(height: 52)
                            .background(Color.arYellow)
                            .foregroundColor(.black)
                            .clipShape(RoundedRectangle(cornerRadius: 26))
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.top, 16)
                    .padding(.bottom, 40)
                }
                .background(Color.arBG)
            }
        }
    }
}

// MARK: - 4. Running Screen
struct RunningView: View {
    let onEnd: () -> Void
    @State private var elapsed = 0
    @State private var distance = 0.0
    @State private var bpm = 142
    @State private var pace = "5'24\""
    @State private var syncRate = 87
    @State private var showEndAlert = false

    let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var elapsedStr: String {
        String(format: "%02d:%02d", elapsed / 60, elapsed % 60)
    }

    var body: some View {
        ARScreen {
            ZStack {
                LinearGradient(
                    colors: [Color(red: 0.07, green: 0.10, blue: 0.07), Color.black],
                    startPoint: .top, endPoint: .bottom
                )
                .ignoresSafeArea()

                // Avatar placeholder
                VStack {
                    Spacer()
                    HStack(spacing: 10) {
                        ForEach(0..<3) { i in
                            RoundedRectangle(cornerRadius: 8)
                                .fill(Color.arYellow.opacity(0.85 - Double(i) * 0.22))
                                .frame(width: 52, height: 52)
                                .rotationEffect(.degrees(Double(i) * 8 - 8))
                        }
                    }
                    .padding(.bottom, 130)
                }

                // Ground grid
                Canvas { ctx, size in
                    for i in 0..<8 {
                        let y = size.height * 0.65 + CGFloat(i) * 18
                        let compress = CGFloat(i) * 0.09
                        var path = Path()
                        path.move(to: CGPoint(x: 0, y: y))
                        path.addLine(to: CGPoint(x: size.width, y: y))
                        let opacity = max(0.0, 0.08 - Double(compress.clamped(to: 0...0.08)))
                        ctx.stroke(path, with: .color(.arYellow.opacity(opacity)), lineWidth: 1)
                    }
                }

                // HUD
                VStack(spacing: 0) {
                    HStack(alignment: .top) {
                        VStack(alignment: .leading, spacing: 1) {
                            ARLabel(text: "伴走中")
                            Text(elapsedStr)
                                .font(.system(size: 34, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }
                        Spacer()
                        VStack(alignment: .trailing, spacing: 1) {
                            ARLabel(text: "距離")
                            Text(String(format: "%.2fkm", distance))
                                .font(.system(size: 34, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.top, 60)
                    .padding(.bottom, 20)
                    .background(
                        LinearGradient(colors: [.black.opacity(0.65), .clear],
                                       startPoint: .top, endPoint: .bottom)
                    )

                    Spacer()

                    VStack(spacing: 0) {
                        LinearGradient(colors: [.clear, .black.opacity(0.72)],
                                       startPoint: .top, endPoint: .bottom)
                            .frame(height: 56)

                        HStack(alignment: .center, spacing: 0) {
                            VStack(spacing: 3) {
                                Image(systemName: "heart.fill")
                                    .foregroundColor(Color(red: 1, green: 0.25, blue: 0.25))
                                    .font(.system(size: 13))
                                Text("\(bpm)")
                                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                                    .foregroundColor(.white)
                                Text("bpm")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            VStack(spacing: 3) {
                                Text(pace)
                                    .font(.system(size: 30, weight: .bold, design: .monospaced))
                                    .foregroundColor(.arYellow)
                                Text("ペース /km")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            VStack(spacing: 3) {
                                Text("\(syncRate)%")
                                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                                    .foregroundColor(syncRate >= 80 ? .arYellow : .orange)
                                Text("シンクロ率")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .padding(.horizontal, 24)
                        .padding(.vertical, 20)
                        .background(Color.black.opacity(0.78))
                    }
                }

                // Stop button
                VStack {
                    HStack {
                        Spacer()
                        Button { showEndAlert = true } label: {
                            Image(systemName: "stop.fill")
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(.white)
                                .frame(width: 42, height: 42)
                                .background(.ultraThinMaterial, in: Circle())
                                .overlay(Circle().strokeBorder(Color.arBorder, lineWidth: 1))
                        }
                        .padding(.trailing, 20)
                        .padding(.top, 60)
                    }
                    Spacer()
                }
            }
            .ignoresSafeArea()
            .onReceive(timer) { _ in
                elapsed += 1
                distance += 0.0028
                bpm = Int.random(in: 138...148)
            }
            .alert("ランを終了しますか？", isPresented: $showEndAlert) {
                Button("終了", role: .destructive) { onEnd() }
                Button("続ける", role: .cancel) {}
            }
        }
    }
}

extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
