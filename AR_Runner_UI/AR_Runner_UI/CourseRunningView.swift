import SwiftUI
import MapKit
import Combine

// MARK: - 3. Course Setup Screen
struct CourseSetupView: View {
    let onStart: () -> Void
    let onSettings: () -> Void
    let onBack: () -> Void // FIXED: Added the back closure property

    @State private var region = MKCoordinateRegion(
        center: CLLocationCoordinate2D(latitude: 34.6937, longitude: 135.5023), // 三宮
        span: MKCoordinateSpan(latitudeDelta: 0.02, longitudeDelta: 0.02)
    )

    var body: some View {
        ARScreen {
            // Using a ZStack allows the back button to overlay nicely on top of the Map
            ZStack(alignment: .topLeading) {
                
                VStack(spacing: 0) {
                    // Map
                    Map(coordinateRegion: $region)
                        .environment(\.locale, Locale(identifier: "ja_JP")) // Forces Japanese map labels
                        .frame(maxHeight: .infinity)
                        .clipShape(RoundedRectangle(cornerRadius: 0))
                        .overlay(
                            // Route overlay dots
                            VStack {
                                Spacer()
                                HStack {
                                    Spacer()
                                    // Start dot
                                    Circle()
                                        .fill(Color.arYellow)
                                        .frame(width: 14, height: 14)
                                        .offset(x: -120, y: -180)
                                }
                            }
                        )

                    // Course info + buttons
                    VStack(spacing: 16) {
                        // Route info
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("三宮駅 → 市役所")
                                    .font(.system(size: 17, weight: .bold))
                                    .foregroundColor(.white)
                                Text("伴走距離：2.1km")
                                    .font(.system(size: 14))
                                    .foregroundColor(.arGrayText)
                            }
                            Spacer()
                            // Distance badge
                            Text("2.1km")
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(.arYellow)
                                .padding(.horizontal, 14)
                                .padding(.vertical, 8)
                                .background(Color.arYellow.opacity(0.15))
                                .clipShape(Capsule())
                        }
                        .padding(.horizontal, 24)
                        .padding(.top, 20)

                        // Action buttons
                        HStack(spacing: 12) {
                            Button {
                                onSettings()
                            } label: {
                                HStack(spacing: 8) {
                                    Text("コース設定")
                                        .font(.system(size: 15, weight: .medium))
                                    Image(systemName: "gearshape")
                                        .font(.system(size: 14))
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
                                    Text("開始")
                                        .font(.system(size: 15, weight: .bold))
                                    Image(systemName: "play.fill")
                                        .font(.system(size: 13))
                                }
                                .frame(maxWidth: .infinity)
                                .frame(height: 52)
                                .background(Color.arYellow)
                                .foregroundColor(.black)
                                .clipShape(RoundedRectangle(cornerRadius: 26))
                            }
                        }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 40)
                    }
                    .background(Color.arBG)
                }
                .ignoresSafeArea(edges: .top)
                
                // FLOATING BACK BUTTON
                ARBackButton(action: onBack)
                    .padding(.leading, 20)
                    .padding(.top, 56) // Provides enough notch clearance for iOS devices
            }
        }
    }
}

// MARK: - 4. Running Screen (AR伴走中)
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
                // AR camera feed placeholder
                ZStack {
                    LinearGradient(
                        colors: [Color(red:0.08, green:0.10, blue:0.08), Color.black],
                        startPoint: .top, endPoint: .bottom
                    )
                    .ignoresSafeArea()

                    // Avatar placeholder (Unity sends frames here via UnityBridge)
                    VStack {
                        Spacer()
                        // Geometric avatar shapes as placeholder
                        HStack(spacing: 12) {
                            ForEach(0..<3) { i in
                                RoundedRectangle(cornerRadius: 8)
                                    .fill(Color.arYellow.opacity(0.85 - Double(i)*0.2))
                                    .frame(width: 56, height: 56)
                                    .rotationEffect(.degrees(Double(i) * 8 - 8))
                            }
                        }
                        .padding(.bottom, 120)
                    }

                    // Ground grid (AR visual)
                    Canvas { ctx, size in
                        for i in 0..<8 {
                            let y = size.height * 0.65 + CGFloat(i) * 20
                            let compress = CGFloat(i) * 0.08
                            var path = Path()
                            path.move(to: CGPoint(x: 0, y: y))
                            path.addLine(to: CGPoint(x: size.width, y: y))
                            ctx.stroke(path, with: .color(.arYellow.opacity(0.08 - compress.clamped(to:0...0.08))), lineWidth: 1)
                        }
                    }
                }

                // HUD overlay
                VStack {
                    // Top HUD
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("伴走中")
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(.arGrayText)
                            Text(elapsedStr)
                                .font(.system(size: 32, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }

                        Spacer()

                        VStack(alignment: .trailing, spacing: 2) {
                            Text("距離")
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                            Text(String(format: "%.1fkm", distance))
                                .font(.system(size: 32, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.top, 56)
                    .background(
                        LinearGradient(colors: [.black.opacity(0.6), .clear],
                                       startPoint: .top, endPoint: .bottom)
                    )

                    Spacer()

                    // Bottom HUD
                    VStack(spacing: 0) {
                        LinearGradient(colors: [.clear, .black.opacity(0.7)],
                                       startPoint: .top, endPoint: .bottom)
                            .frame(height: 60)

                        HStack(alignment: .center, spacing: 0) {
                            // BPM
                            VStack(spacing: 2) {
                                Image(systemName: "heart.fill")
                                    .foregroundColor(.red)
                                    .font(.system(size: 14))
                                Text("\(bpm)")
                                    .font(.system(size: 22, weight: .bold))
                                    .foregroundColor(.white)
                                Text("bpm")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            // Pace
                            VStack(spacing: 2) {
                                Text(pace)
                                    .font(.system(size: 28, weight: .bold, design: .monospaced))
                                    .foregroundColor(.arYellow)
                                Text("ペース /km")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            // Sync rate
                            VStack(spacing: 2) {
                                Text("\(syncRate)%")
                                    .font(.system(size: 22, weight: .bold))
                                    .foregroundColor(syncRate >= 80 ? .arYellow : .orange)
                                Text("シンクロ率")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .padding(.horizontal, 24)
                        .padding(.vertical, 20)
                        .background(Color.black.opacity(0.75))
                    }
                }

                // End button (Top Right overlay)
                VStack {
                    HStack {
                        Spacer()
                        Button {
                            showEndAlert = true
                        } label: {
                            Image(systemName: "stop.fill")
                                .font(.system(size: 16, weight: .bold))
                                .foregroundColor(.white)
                                .frame(width: 44, height: 44)
                                .background(Color.black.opacity(0.6))
                                .clipShape(Circle())
                                .overlay(Circle().stroke(Color.arBorder, lineWidth: 1))
                        }
                        .padding(.trailing, 20)
                        .padding(.top, 56)
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

// Extension for clamped
extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
