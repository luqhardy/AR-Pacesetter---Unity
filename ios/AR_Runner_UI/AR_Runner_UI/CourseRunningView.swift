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

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var session = ARSessionManager.shared
    @ObservedObject private var unity = UnityLauncher.shared
    @ObservedObject private var heartMonitor = HeartRateMonitor.shared

    @State private var bpm = 142
    @State private var showEndAlert = false
    @State private var showGoalOverlay = false

    let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var elapsedStr: String {
        String(format: "%02d:%02d", session.elapsedSeconds / 60, session.elapsedSeconds % 60)
    }

    /// UnityFrameworkがリンク済みでARビューを持っているか
    private var hasUnityView: Bool {
        unity.isRunning && unity.unityRootView != nil
    }

    var body: some View {
        ARScreen {
            ZStack {
                if hasUnityView {
                    // ★ Unity ARビュー（カメラ映像+伴走アバター）が背景全面に描画される
                    UnityContainerView()
                        .ignoresSafeArea()
                } else {
                    // UnityFramework未リンク時（シミュレータ・UI単体開発）のモック背景
                    mockBackground
                }

                // HUD
                VStack(spacing: 0) {
                    HStack(alignment: .top) {
                        VStack(alignment: .leading, spacing: 1) {
                            ARLabel(text: RunSettings.shared.ghostDateIso != nil ? "ゴースト競走中" : "伴走中")
                            Text(elapsedStr)
                                .font(.system(size: 34, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }
                        Spacer()
                        VStack(alignment: .trailing, spacing: 1) {
                            ARLabel(text: "距離")
                            Text(String(format: "%.2fkm", session.currentDistance))
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
                                Text(RunSettings.shared.paceMinPerKmString)
                                    .font(.system(size: 30, weight: .bold, design: .monospaced))
                                    .foregroundColor(.arYellow)
                                Text("ペース /km")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            VStack(spacing: 3) {
                                Text("\(bridge.avatarSyncRate)%")
                                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                                    .foregroundColor(bridge.avatarSyncRate >= 80 ? .arYellow : .orange)
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

                // ゴール到達オーバーレイ（Unityの目標距離自動終了 → AvatarState "Goal"）
                if showGoalOverlay {
                    VStack(spacing: 12) {
                        Text("GOAL!")
                            .font(.system(size: 56, weight: .black))
                            .foregroundColor(.arYellow)
                            .shadow(color: Color.arYellow.opacity(0.6), radius: 24)
                        Text("目標距離を達成しました")
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundColor(.white)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.black.opacity(0.55))
                    .transition(.opacity)
                }

                // GPS喪失バナー（Unityの GPSLost / GPSRecovered イベント連動）
                if bridge.gpsStatus == .lost {
                    VStack {
                        HStack(spacing: 8) {
                            Image(systemName: "location.slash.fill")
                                .font(.system(size: 13))
                            Text("GPS再取得中 — アバターは慣性走行中")
                                .font(.system(size: 13, weight: .semibold))
                        }
                        .foregroundColor(.black)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                        .background(Color.arYellow, in: Capsule())
                        .padding(.top, 116)
                        Spacer()
                    }
                    .transition(.move(edge: .top).combined(with: .opacity))
                }
            }
            .animation(.easeInOut(duration: 0.25), value: bridge.gpsStatus == .lost)
            .ignoresSafeArea()
            .onAppear {
                // Unityランタイム起動 → 走行セッション開始（設定値をUnityへ引き渡す）
                UnityLauncher.shared.launch()
                if !session.isSessionActive {
                    session.start(
                        paceKmH: RunSettings.shared.paceKmH,
                        distanceKm: RunSettings.shared.distanceKm,
                        ghostDateIso: RunSettings.shared.ghostDateIso
                    )
                }
            }
            .onDisappear {
                // ゴースト指定は1走行限り
                RunSettings.shared.ghostDateIso = nil
            }
            .onReceive(timer) { _ in
                // 心拍: HealthKit実測(Watch装着時)を優先、未取得時は仮表示
                let realBpm = heartMonitor.latestBpm
                bpm = realBpm > 0 ? realBpm : Int.random(in: 138...148)
            }
            .onChange(of: bridge.avatarState) { _, newState in
                // Unity側の自動ゴール（目標距離到達）→ ローカル停止 → 統計画面へ
                guard newState == .goal, session.isSessionActive else { return }
                session.endLocally()
                withAnimation(.easeIn(duration: 0.3)) { showGoalOverlay = true }
                DispatchQueue.main.asyncAfter(deadline: .now() + 2.5) {
                    UnityLauncher.shared.pause()
                    onEnd()
                }
            }
            .alert("ランを終了しますか？", isPresented: $showEndAlert) {
                Button("終了", role: .destructive) {
                    session.end()               // Unityへ EndSession → SessionEnded が返る
                    UnityLauncher.shared.pause()
                    onEnd()
                }
                Button("続ける", role: .cancel) {}
            }
        }
    }

    // MARK: UnityFramework未リンク時のモック背景（UI単体開発用）
    private var mockBackground: some View {
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
        }
    }
}

extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
