import SwiftUI
import Combine

// MARK: - 4. Running Screen
struct RunningView: View {
    let onEnd: () -> Void
    /// §8.3 ARグラス切断時: 準備画面へ戻す(走行記録・CSVログは継続)
    var onGlassDisconnected: (() -> Void)? = nil

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var session = ARSessionManager.shared
    @ObservedObject private var unity = UnityLauncher.shared
    @ObservedObject private var heartMonitor = HeartRateMonitor.shared
    @ObservedObject private var external = ExternalDisplayManager.shared

    /// HealthKit(Watch)実測のみ。未取得は0=不明として "--" を表示する(H2)
    @State private var bpm = 0
    @State private var showEndAlert = false
    @State private var showGoalOverlay = false
    @State private var targetSpeedKmH = RunSettings.shared.paceKmH

    /// 画面ロック(誤タップ防止)。**画面遷移ではなくオーバーレイ**にしている —
    /// 別画面へ遷移すると UnityContainerView がヒエラルキーから外れ、ARビューの
    /// 貼り直しが起きる。走行はそのまま続けたいので、上から覆うだけにする
    @State private var isLocked = false

    let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var elapsedStr: String {
        String(format: "%02d:%02d", session.elapsedSeconds / 60, session.elapsedSeconds % 60)
    }

    /// UnityFrameworkがリンク済みでARビューを持っているか
    private var hasUnityView: Bool {
        unity.isRunning && unity.unityRootView != nil
    }

    /// SwiftUI側で統計HUDを出すか。**表示面で所有者をひとつに決める**(監査M1):
    ///   iPhone表示中  = SwiftUI(手に持って正面から見る画面に適した意匠)
    ///   グラス出力中  = UnityのF-07 HUD(周辺視野レイアウトはグラス用の設計)
    /// どちらの場合も二重には描かない。Unity側は StartSession の hideUnityHud で切り替える。
    private var showsSwiftStatsHud: Bool { !external.isGlassesConnected }

    /// グラス出力中はiPhoneを「操作面」として扱う。
    /// 走行中は腕・ポケットの端末を読めないので、統計を並べるより
    /// 大きく押せる終了操作を置くほうが役に立つ。
    private var isControlSurface: Bool { external.isGlassesConnected && hasUnityView }

    private var speedControl: some View {
        VStack(alignment: .leading, spacing: 4) {
            Stepper(value: $targetSpeedKmH, in: 1...30, step: 0.5) {
                Text(String(format: "アバター速度 %.1f km/h", targetSpeedKmH)) // TODO: 文言の集約対象
                    .font(.system(size: 15, weight: .semibold))
            }
            .onChange(of: targetSpeedKmH) { _, speed in bridge.setTargetSpeed(kmh: speed) }
            if let applied = bridge.confirmedTargetSpeedKmH {
                Text(String(format: "設定済み %.1f km/h・待機中は停止", applied)) // TODO: 文言の集約対象
                    .font(.caption)
            } else {
                Text("Unityの開始を待っています") // TODO: 文言の集約対象
                    .font(.caption)
            }
        }
        .foregroundColor(.white)
        .padding(12)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 12))
        .disabled(bridge.confirmedTargetSpeedKmH == nil || showGoalOverlay)
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

                // AR準備中のオーバーレイ。
                //
                // この区間はメインスレッドが Unity の初期化で塞がっているため、
                // **アニメーションは一切動かない**。だからスピナーもプログレスバーも
                // 置いていない — 止まったスピナーは「読み込み中」ではなく
                // 「アプリがハングした」に見えるので、静止画のほうが誠実で安心感がある。
                // 初回のみ数秒かかる旨も文言で伝え、別途アラートを出す必要をなくす
                if unity.isPreparing {
                    ZStack {
                        Color.black.opacity(0.92).ignoresSafeArea()
                        VStack(spacing: 14) {
                            Image(systemName: "arkit")
                                .font(.system(size: 44, weight: .light))
                                .foregroundColor(.arYellow)
                            Text(AppStrings.Running.preparingTitle)
                                .font(.system(size: 18, weight: .bold))
                                .foregroundColor(.white)
                            Text(AppStrings.Running.preparingNote)
                                .font(.system(size: 13))
                                .foregroundColor(.arGrayText)
                        }
                    }
                    .transition(.opacity)
                }

                // HUD
                VStack(spacing: 0) {
                    if showsSwiftStatsHud {
                    HStack(alignment: .top) {
                        VStack(alignment: .leading, spacing: 1) {
                            ARLabel(text: RunSettings.shared.ghostDateIso != nil ? AppStrings.Running.ghost : AppStrings.Running.active)
                            Text(elapsedStr)
                                .font(.system(size: 34, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }
                        Spacer()
                        VStack(alignment: .trailing, spacing: 1) {
                            ARLabel(text: AppStrings.Running.distance)
                            Text(String(format: "%.2fkm", session.currentDistance))
                                .font(.system(size: 34, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                            if session.isDistanceEstimated {
                                // GPS未取得中の表示値は推定 — 実測と混同させない(H3)
                                Text(AppStrings.Running.estimated)
                                    .font(.system(size: 10, weight: .semibold))
                                    .foregroundColor(.arGrayText)
                            }
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.top, 60)
                    .padding(.bottom, 20)
                    .background(
                        LinearGradient(colors: [.black.opacity(0.65), .clear],
                                       startPoint: .top, endPoint: .bottom)
                    )
                    } // showsSwiftStatsHud

                    Spacer()

                    if showsSwiftStatsHud {
                    VStack(spacing: 0) {
                        LinearGradient(colors: [.clear, .black.opacity(0.72)],
                                       startPoint: .top, endPoint: .bottom)
                            .frame(height: 56)

                        HStack(alignment: .center, spacing: 0) {
                            VStack(spacing: 3) {
                                Image(systemName: "heart.fill")
                                    .foregroundColor(Color(red: 1, green: 0.25, blue: 0.25))
                                    .font(.system(size: 13))
                                Text(bpm > 0 ? "\(bpm)" : "--")
                                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                                    .foregroundColor(bpm > 0 ? .white : .arGrayText)
                                Text("bpm")
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            VStack(spacing: 3) {
                                Text(RunSettings.shared.paceMinPerKmString)
                                    .font(.system(size: 30, weight: .bold, design: .monospaced))
                                    .foregroundColor(.arYellow)
                                Text(AppStrings.Running.pace)
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)

                            VStack(spacing: 3) {
                                // 負値は「未計測」(Unity未リンク等)。0%と区別できるよう
                                // 数字ではなくダッシュで示す — 見えている数字は必ず実測
                                Text(bridge.avatarSyncRate < 0 ? "—" : "\(bridge.avatarSyncRate)%")
                                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                                    .foregroundColor(bridge.avatarSyncRate < 0 ? .arGrayText
                                                     : (bridge.avatarSyncRate >= 80 ? .arYellow : .orange))
                                Text(AppStrings.Running.syncRate)
                                    .font(.system(size: 11))
                                    .foregroundColor(.arGrayText)
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .padding(.horizontal, 24)
                        .padding(.vertical, 20)
                        .background(Color.black.opacity(0.78))
                    }
                    } // showsSwiftStatsHud
                }

                // 終了操作。グラス出力中はiPhoneが操作面になるので大きく置く
                if isControlSurface {
                    VStack(spacing: 20) {
                        Spacer()

                        VStack(spacing: 4) {
                            Text(AppStrings.Running.onGlasses)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(.arGrayText)
                            Text(elapsedStr)
                                .font(.system(size: 64, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                        }

                        speedControl.padding(.horizontal, 28)

                        Button { showEndAlert = true } label: {
                            HStack(spacing: 10) {
                                Image(systemName: "stop.fill")
                                    .font(.system(size: 22, weight: .bold))
                                Text(AppStrings.Common.end)
                                    .font(.system(size: 22, weight: .bold))
                            }
                            .foregroundColor(.white)
                            .frame(maxWidth: .infinity, minHeight: 88)
                            .background(Color(red: 0.85, green: 0.25, blue: 0.2), in: RoundedRectangle(cornerRadius: 18))
                        }
                        .padding(.horizontal, 28)

                        // グラス出力中は端末を見ずに走るので、ポケットでの誤タップを防ぐ
                        Button { isLocked = true } label: {
                            HStack(spacing: 8) {
                                Image(systemName: "lock.fill")
                                    .font(.system(size: 15, weight: .bold))
                                Text(AppStrings.Running.lockScreen)
                                    .font(.system(size: 15, weight: .semibold))
                            }
                            .foregroundColor(.white)
                            .frame(maxWidth: .infinity, minHeight: 54)
                            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 16))
                            .overlay(
                                RoundedRectangle(cornerRadius: 16)
                                    .strokeBorder(Color.arBorder, lineWidth: 1)
                            )
                        }
                        .padding(.horizontal, 28)
                        .padding(.bottom, 48)
                    }
                } else {
                    // Stop / Lock buttons
                    VStack {
                        HStack(spacing: 10) {
                            Spacer()
                            Button { isLocked = true } label: {
                                Image(systemName: "lock.fill")
                                    .font(.system(size: 15, weight: .bold))
                                    .foregroundColor(.white)
                                    .frame(width: 42, height: 42)
                                    .background(.ultraThinMaterial, in: Circle())
                                    .overlay(Circle().strokeBorder(Color.arBorder, lineWidth: 1))
                            }
                            Button { showEndAlert = true } label: {
                                Image(systemName: "stop.fill")
                                    .font(.system(size: 15, weight: .bold))
                                    .foregroundColor(.white)
                                    .frame(width: 42, height: 42)
                                    .background(.ultraThinMaterial, in: Circle())
                                    .overlay(Circle().strokeBorder(Color.arBorder, lineWidth: 1))
                            }
                            .padding(.trailing, 20)
                        }
                        .padding(.top, 60)
                        speedControl.padding(.horizontal, 20)
                        Spacer()
                    }
                }

                // ゴール到達オーバーレイ（Unityの目標距離自動終了 → AvatarState "Goal"）
                if showGoalOverlay {
                    VStack(spacing: 12) {
                        Text("GOAL!")
                            .font(.system(size: 56, weight: .black))
                            .foregroundColor(.arYellow)
                            .shadow(color: Color.arYellow.opacity(0.6), radius: 24)
                        Text(AppStrings.Running.goalReached)
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundColor(.white)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.black.opacity(0.55))
                    .transition(.opacity)
                }

                // 低バッテリー退避バナー（Unityの LowBattery イベント連動）
                if bridge.lowBatteryMode {
                    VStack {
                        HStack(spacing: 8) {
                            Image(systemName: "battery.25percent")
                                .font(.system(size: 13))
                            Text(AppStrings.Running.lowBattery)
                                .font(.system(size: 13, weight: .semibold))
                        }
                        .foregroundColor(.white)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                        .background(Color(red: 0.85, green: 0.25, blue: 0.2), in: Capsule())
                        .padding(.top, 160)
                        Spacer()
                    }
                    .transition(.move(edge: .top).combined(with: .opacity))
                }

                // GPS喪失バナー（Unityの GPSLost / GPSRecovered イベント連動）
                if bridge.gpsStatus == .lost {
                    VStack {
                        HStack(spacing: 8) {
                            Image(systemName: "location.slash.fill")
                                .font(.system(size: 13))
                            Text(AppStrings.Running.gpsLost)
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

                // アバター非表示の理由バナー(Unity AvatarVisibilityDiagnostics)。
                // 実機で「消えた」と言われたとき、どの経路で消えたかをその場で読めるようにする
                if let reason = bridge.avatarHiddenReason {
                    VStack {
                        HStack(alignment: .top, spacing: 8) {
                            Image(systemName: "eye.slash.fill")
                                .font(.system(size: 13))
                            Text(AppStrings.Running.avatarHidden(reason))
                                .font(.system(size: 12, weight: .semibold))
                                .multilineTextAlignment(.leading)
                        }
                        .foregroundColor(.white)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 9)
                        .background(Color(red: 0.55, green: 0.2, blue: 0.7), in: RoundedRectangle(cornerRadius: 14))
                        .padding(.horizontal, 24)
                        .padding(.top, 206)
                        Spacer()
                    }
                    .transition(.move(edge: .top).combined(with: .opacity))
                }

                // 画面ロック — 最前面。走行・Unityの描画は裏で続いており、
                // 上へスワイプすると元の走行画面へそのまま戻る
                if isLocked {
                    // 終了はロックを解除してから(タスク表 S6: 誤操作でランが終わらないように)。
                    // ロック画面にあった停止ボタン(hsu/main 由来)は 2026-10-14 に外した
                    LockScreenView(onUnlock: { isLocked = false })
                        .transition(.opacity)
                }
            }
            .animation(.easeInOut(duration: 0.2), value: isLocked)
            .animation(.easeInOut(duration: 0.25), value: bridge.gpsStatus == .lost)
            .ignoresSafeArea()
            .onAppear {
                // Unityランタイム起動 → 走行セッション開始（設定値をUnityへ引き渡す）
                //
                // prepare() を使う理由: Unityの初期化は同期でメインスレッドを塞ぐため、
                // launch() を直接呼ぶと SwiftUI に描く隙が無く、準備画面が一度も
                // 表示されないまま固まる。prepare() は表示を1フレーム描かせてから入る
                UnityLauncher.shared.prepare()
                if !session.isSessionActive {
                    session.start(
                        paceKmH: RunSettings.shared.paceKmH,
                        distanceKm: RunSettings.shared.distanceKm,
                        ghostDateIso: RunSettings.shared.ghostDateIso,
                        // iPhoneに映している間はSwiftUIがHUDを持つ
                        hideUnityHud: !ExternalDisplayManager.shared.isGlassesConnected
                    )
                } else {
                    // §8.3: グラス切断で準備画面へ戻った後の再スタート。
                    // 新規セッションは開始せず、スタンバイ中の表示だけ復帰させる
                    bridge.resumeSession()
                }
            }
            .onChange(of: external.isGlassesConnected) { wasConnected, isConnected in
                // §8.3: 走行中にグラスが切断されたら準備画面へ戻す
                // (接続されたことがある場合のみ反応 — 未接続環境で誤発火しない)
                if wasConnected && !isConnected && session.isSessionActive {
                    onGlassDisconnected?()
                }
            }
            .onDisappear {
                // ゴースト指定は1走行限り
                RunSettings.shared.ghostDateIso = nil
            }
            .onReceive(timer) { _ in
                // 心拍はHealthKit実測のみ。Watch未装着なら "--" のまま
                bpm = heartMonitor.latestBpm
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
            .alert(AppStrings.Common.endRunTitle, isPresented: $showEndAlert) {
                Button(AppStrings.Common.end, role: .destructive) {
                    session.end()               // Unityへ EndSession → SessionEnded が返る
                    UnityLauncher.shared.pause()
                    onEnd()
                }
                Button(AppStrings.Common.continueRun, role: .cancel) {}
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
