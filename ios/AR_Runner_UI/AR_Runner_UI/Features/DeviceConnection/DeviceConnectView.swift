import SwiftUI
import UIKit
import Combine   // Timer.publish().autoconnect() (Xcode 26 は SwiftUI 経由の暗黙再エクスポートをしない)

// MARK: - Device Connect Screen (All devices on one screen)
struct DeviceConnectView: View {
    let onNext: () -> Void
    let onBack: () -> Void
    /// 走行中にグラスが切れて戻ってきたか(§8.3)。記録は続いているので、つなぎ直して再開するよう案内する
    var returnedFromRun: Bool = false

    /// 手動の「接続」を押したか。実接続の検出とは別物として扱う
    @State private var manualConnect = false
    /// この画面でつながった後に切れたか(つながった直後の切断。タスク表 S4 AR異常系)
    @State private var droppedHere = false
    /// 「接続中」の待ち時間を数えるための時計(1秒ごと)
    @State private var now = Date()
    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    // 実グラス接続(USB-C外部ディスプレイ)を検知したら表示に反映
    @ObservedObject private var external = ExternalDisplayManager.shared
    /// AirPods の頭の向き(任意。首を振ってもアバターを進行方向に保つ)
    @ObservedObject private var headTracker = HeadphoneHeadTracker.shared
    /// グラス(XREAL One)自身のIMU(任意。AirPods より優先して頭の向きに使う)
    @ObservedObject private var glassImu = XrealImuReceiver.shared
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.openURL) private var openURL
    /// 開発者モード「ARグラスなしで走行を始める」(既定 OFF)
    @AppStorage(DeviceGate.allowRunWithoutGlassesKey) private var allowRunWithoutGlasses = false

    /// ARグラス行の表示。**実際の状態だけを出す**。
    /// 以前はタップから1.5秒後に、何も挿さっていなくても「接続済み」になった。そのため
    /// iOS がグラスへミラーリングしている(=グラス専用の画面が無い)ことに気づけなかった(2026-10-07 実機)
    private var glassStatus: GlassRowStatus {
        .resolve(glassesConnected: external.isGlassesConnected,
                 mirroring: external.isMirroring,
                 manualConnect: manualConnect)
    }

    /// 走行に必要なのはARグラスだけ。Watch/AirPodsは任意(基本設計書 §1.2 では第1期のスコープ外)
    var allConnected: Bool { glassStatus == .connected }

    /// ARグラスで起きている問題(エラー表示)。問題が無ければ nil
    private var glassProblem: GlassConnectionProblem? {
        .resolve(status: glassStatus, droppedHere: droppedHere, returnedFromRun: returnedFromRun,
                 mirroringSeconds: external.mirroringSince.map { now.timeIntervalSince($0) })
    }

    /// 「次へ」を押せるか。ARグラスは必須(タスク表 S4)— 未接続・ミラーリング中は進めない
    private var canProceed: Bool {
        DeviceGate.canProceed(glass: glassStatus, allowRunWithoutGlasses: allowRunWithoutGlasses)
    }

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar
                HStack {
                    ARBackButton(action: goBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 20)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: AppStrings.Devices.setupEyebrow)
                    Text(AppStrings.Devices.title)
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text(AppStrings.Devices.subtitle)
                        .font(.system(size: 14))
                        .foregroundColor(.arGrayText)
                        .padding(.top, 2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 28)

                // Device cards
                VStack(spacing: 12) {
                    DeviceConnectRow(
                        icon: "eyeglasses",
                        name: AppStrings.Devices.arGlasses,
                        subtitle: "XREAL One",
                        status: glassStatus,
                        onConnect: {
                            // 手動の ConnectXREAL(UnityのReadyチェックとグラス用の描画を有効にする)。
                            // グラスが検出されない・ミラーリング中の予備手段として残すが、
                            // 実接続とは表示で区別する
                            manualConnect = true
                            UnityBridge.shared.connect()
                        }
                    )
                    // Apple Watch は第1期のスコープ外(基本設計書 §1.2 —
                    // 通信ボトルネックを排除しM2P 20msを死守するため、iPhone+グラスの
                    // 2デバイスに特化する)。以前はタップから1秒後に「接続済み」と
                    // 表示するだけの**見せかけ**だったので、実態どおりの表示に改めた
                    DeviceUnavailableRow(
                        icon: "applewatch",
                        name: "Apple Watch",
                        note: AppStrings.Devices.outOfScope
                    )
                    // AirPods は任意。頭の向きだけを使う(首を振ってもアバターを進行方向の3m先に保つ —
                    // タスク表 ARグラス「首を振り向けた時に想定する場所を走る」)。失敗しても先へ進める
                    HeadTrackingRow(
                        status: headTracker.status,
                        onConnect: { headTracker.start() },
                        onRetry: { headTracker.retry() },
                        onOpenSettings: openAppSettings
                    )
                    // グラス自身のIMU(USBネットワーク越し)。届いている間は AirPods より優先して頭のヨーに使い、
                    // 走行ログCSVの imu_accel 列もグラスの値で埋める。任意・既定OFF
                    GlassImuRow(
                        status: glassImu.runStatus,
                        onConnect: { glassImu.startForRun() },
                        onRetry: { glassImu.retryForRun() }
                    )
                }
                .padding(.horizontal, 24)

                Spacer()

                // ARグラスのエラー(タスク表 S4 異常系)。問題が無ければ従来どおりの1行の案内
                if let problem = glassProblem {
                    GlassProblemCard(
                        problem: problem,
                        canReconnect: problem != .notDetected && problem != .connecting,
                        onReconnect: reconnectGlasses
                    )
                    .padding(.horizontal, 24)
                    .padding(.bottom, 12)
                    if allowRunWithoutGlasses {
                        Text(AppStrings.Devices.devBypass)
                            .font(.system(size: 12))
                            .foregroundColor(.orange)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 32)
                            .padding(.bottom, 12)
                    }
                } else {
                    Text(statusHint)
                        .font(.system(size: 13))
                        .foregroundColor(glassStatus.color)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 32)
                        .padding(.bottom, 16)
                }

                // 次へ — ARグラスがつながるまで押せない(タスク表 S4: ARグラスは必須)
                Button(action: onNext) {
                    ZStack {
                        Circle()
                            .fill(canProceed ? Color.arYellow : Color.arBorder)
                            .frame(width: 64, height: 64)
                            .shadow(color: Color.arYellow.opacity(allConnected ? 0.55 : (canProceed ? 0.25 : 0)), radius: 20)
                        Image(systemName: "arrow.right")
                            .font(.system(size: 22, weight: .bold))
                            .foregroundColor(canProceed ? .black : .arGrayText)
                    }
                }
                .disabled(!canProceed)
                .padding(.bottom, 52)
            }
            .onAppear {
                external.refreshMirroring()
            }
            .onChange(of: external.isGlassesConnected) { wasConnected, isConnected in
                // 実グラスを挿したら手動扱いは不要。抜いたら手動扱いも解く(実態に合わせる)
                manualConnect = false
                // つながった直後に切れた(電池切れ・ケーブルの接触)。つながり直せば消える
                droppedHere = wasConnected && !isConnected
            }
            .onReceive(tick) { now = $0 }
            .onChange(of: scenePhase) { _, phase in
                // 電話・他アプリから戻ったら、その間に変わった接続状態を取り直す(タスク表 S4)
                guard phase == .active else { return }
                external.refreshMirroring()
                headTracker.refreshAfterForeground()
            }
        }
    }

    /// 「戻る」。AirPods を探している途中でも止めてから離れる(タスク表 S4「接続処理中の戻る」)
    private func goBack() {
        headTracker.stop()
        glassImu.stop()
        onBack()
    }

    /// 「再接続」: ミラーリング・切断の状態を取り直す。グラスを挿し直した後に押してもらう
    private func reconnectGlasses() {
        droppedHere = false
        external.refreshMirroring()
    }

    private func openAppSettings() {
        guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
        openURL(url)
    }

    private var statusHint: LocalizedStringKey {
        switch glassStatus {
        case .connected:
            return AppStrings.Devices.hintConnected
        case .mirroring:
            return AppStrings.Devices.hintMirroring
        case .manual:
            return AppStrings.Devices.hintManual
        case .notDetected:
            return AppStrings.Devices.hintNotDetected
        }
    }
}

// MARK: - ARグラス行の見た目
// 状態の判定は AppLogic/GlassConnectionStatus.swift(テスト対象)
extension GlassRowStatus {
    var color: Color {
        switch self {
        case .connected: return .arYellow
        case .mirroring, .manual: return .orange
        case .notDetected: return .arGrayText
        }
    }

    var label: LocalizedStringKey? {
        switch self {
        case .connected: return AppStrings.Devices.connected
        case .mirroring: return AppStrings.Devices.mirroring
        case .manual: return AppStrings.Devices.manual
        case .notDetected: return nil
        }
    }
}

// MARK: - Device Connect Row
struct DeviceConnectRow: View {
    let icon: String
    let name: LocalizedStringKey
    let subtitle: LocalizedStringKey
    let status: GlassRowStatus
    let onConnect: () -> Void

    private var isConnected: Bool { status == .connected }
    /// 手動の接続を押せるのは、実接続も手動接続もしていないときだけ
    private var canConnect: Bool { status.canConnectManually }

    var body: some View {
        Button(action: {
            if canConnect { onConnect() }
        }) {
            HStack(spacing: 16) {
                // Icon
                ZStack {
                    Circle()
                        .fill(Color.arBG)
                        .frame(width: 56, height: 56)
                        .overlay(
                            Circle().stroke(
                                status == .notDetected ? Color.arBorder : status.color.opacity(0.6),
                                lineWidth: 1
                            )
                        )
                    Image(systemName: icon)
                        .font(.system(size: 24))
                        .foregroundColor(status == .notDetected ? .arGrayText : status.color)
                }

                // Text
                VStack(alignment: .leading, spacing: 3) {
                    Text(name)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundColor(.white)
                    HStack(spacing: 5) {
                        if let label = status.label {
                            Image(systemName: isConnected ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                                .font(.system(size: 11))
                                .foregroundColor(status.color)
                            Text(label)
                                .font(.system(size: 13))
                                .foregroundColor(status.color)
                        } else {
                            Text(subtitle)
                                .font(.system(size: 13))
                                .foregroundColor(.arGrayText)
                        }
                    }
                }

                Spacer()

                // Status badge
                if isConnected {
                    Image(systemName: "checkmark")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(.black)
                        .frame(width: 28, height: 28)
                        .background(Color.arYellow)
                        .clipShape(Circle())
                } else if canConnect {
                    // 手動の接続(予備手段)。実接続は挿せば自動で反映される
                    Text(AppStrings.Devices.connectManually)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(.arYellow)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .background(Color.arYellow.opacity(0.12))
                        .clipShape(Capsule())
                        .overlay(
                            Capsule().stroke(Color.arYellow.opacity(0.4), lineWidth: 1)
                        )
                }
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 16)
            .background(Color.arCard)
            .clipShape(RoundedRectangle(cornerRadius: 18))
            .overlay(
                RoundedRectangle(cornerRadius: 18)
                    .stroke(status == .notDetected ? Color.arBorder : status.color.opacity(0.35), lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
        .animation(.easeInOut(duration: 0.2), value: status)
    }
}

// MARK: - Unavailable Device Row
/// 第1期のスコープ外デバイス。**押せない**ことを見た目で示す。
/// 接続できないものを「接続」ボタン付きで並べると、押しても何も起きない or
/// 偽の接続済み表示になり、ユーザーは「壊れている」と受け取る。
struct DeviceUnavailableRow: View {
    let icon: String
    let name: LocalizedStringKey
    let note: LocalizedStringKey

    var body: some View {
        HStack(spacing: 16) {
            ZStack {
                Circle()
                    .fill(Color.arBG)
                    .frame(width: 56, height: 56)
                    .overlay(Circle().stroke(Color.arBorder, lineWidth: 1))
                Image(systemName: icon)
                    .font(.system(size: 24))
                    .foregroundColor(.arGrayText.opacity(0.6))
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(name)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white.opacity(0.55))
                Text(note)
                    .font(.system(size: 13))
                    .foregroundColor(.arGrayText)
            }

            Spacer()
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 16)
        .background(Color.arCard.opacity(0.5))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(Color.arBorder.opacity(0.6), lineWidth: 1)
        )
    }
}

// MARK: - ARグラスのエラー表示(タスク表 S4 異常系)

private struct GlassProblemCard: View {
    let problem: GlassConnectionProblem
    let canReconnect: Bool
    let onReconnect: () -> Void

    private var title: LocalizedStringKey {
        switch problem {
        case .connecting: return AppStrings.Devices.connectingTitle
        case .notDetected: return AppStrings.Devices.errorNotDetectedTitle
        case .incomplete: return AppStrings.Devices.errorIncompleteTitle
        case .droppedAfterConnect: return AppStrings.Devices.errorDroppedTitle
        case .lostDuringRun: return AppStrings.Devices.errorLostDuringRunTitle
        }
    }

    private var message: LocalizedStringKey {
        switch problem {
        case .connecting: return AppStrings.Devices.connectingBody
        case .notDetected: return AppStrings.Devices.errorNotDetectedBody
        case .incomplete: return AppStrings.Devices.errorIncompleteBody
        case .droppedAfterConnect: return AppStrings.Devices.errorDroppedBody
        case .lostDuringRun: return AppStrings.Devices.errorLostDuringRunBody
        }
    }

    /// 接続中は待っているだけなのでエラーの色にしない
    private var tint: Color { problem == .connecting ? .arGrayText : .orange }

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            if problem == .connecting {
                ProgressView().tint(.white)
            } else {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 15))
                    .foregroundColor(.orange)
            }
            VStack(alignment: .leading, spacing: 4) {
                Text(title)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(.white)
                Text(message)
                    .font(.system(size: 12))
                    .foregroundColor(.arGrayText)
                    .fixedSize(horizontal: false, vertical: true)
                if canReconnect {
                    SettingsActionButton(title: AppStrings.Devices.reconnect, action: onReconnect)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .background(tint.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(
            RoundedRectangle(cornerRadius: 14)
                .stroke(tint.opacity(0.45), lineWidth: 1)
        )
    }
}

// MARK: - AirPods(頭の向き)行

/// 任意の機器。押すと AirPods の頭の向きを使い始める。見つからない・対応していない・許可が無いときは
/// 理由を出すが、走行は止めない(タスク表 S4「Apple Watch・AirPods の失敗は、エラー表示はするが進める」)
private struct HeadTrackingRow: View {
    let status: HeadTrackingStatus
    let onConnect: () -> Void
    let onRetry: () -> Void
    let onOpenSettings: () -> Void

    private var tint: Color {
        switch status {
        case .active: return .arYellow
        case .searching: return .white
        case .notFound, .noHeadMotion, .unsupported, .denied: return .orange
        case .off: return .arGrayText
        }
    }

    private var detail: LocalizedStringKey {
        switch status {
        case .off: return AppStrings.Devices.airpodsSubtitle
        case .searching: return AppStrings.Devices.airpodsSearching
        case .active: return AppStrings.Devices.airpodsActive
        case .notFound: return AppStrings.Devices.airpodsNotFound
        case .noHeadMotion: return AppStrings.Devices.airpodsNoHeadMotion
        case .unsupported: return AppStrings.Devices.airpodsUnsupported
        case .denied: return AppStrings.Devices.airpodsDenied
        }
    }

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            ZStack {
                Circle()
                    .fill(Color.arBG)
                    .frame(width: 56, height: 56)
                    .overlay(Circle().stroke(status == .off ? Color.arBorder : tint.opacity(0.6), lineWidth: 1))
                Image(systemName: "airpodspro")
                    .font(.system(size: 24))
                    .foregroundColor(status == .off ? .arGrayText : tint)
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(verbatim: "AirPods")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                Text(detail)
                    .font(.system(size: 12))
                    .foregroundColor(status == .off ? .arGrayText : tint)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Spacer(minLength: 0)

            switch status {
            case .off:
                pill(AppStrings.Devices.connect, action: onConnect)
            case .searching:
                // 探している間は押せない(連打で二重に始めない)
                ProgressView().tint(.white)
            case .active:
                Image(systemName: "checkmark")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(.black)
                    .frame(width: 28, height: 28)
                    .background(Color.arYellow)
                    .clipShape(Circle())
            case .notFound, .noHeadMotion:
                pill(AppStrings.Devices.reconnect, action: onRetry)
            case .denied:
                pill(AppStrings.Common.openSettings, action: onOpenSettings)
            case .unsupported:
                EmptyView()
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(status == .off ? Color.arBorder : tint.opacity(0.35), lineWidth: 1)
        )
        .animation(.easeInOut(duration: 0.2), value: status)
    }

    private func pill(_ title: LocalizedStringKey, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.arYellow)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(Color.arYellow.opacity(0.12))
                .clipShape(Capsule())
                .overlay(Capsule().stroke(Color.arYellow.opacity(0.4), lineWidth: 1))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - グラスのIMU(頭の向き)行

/// 任意。押すとグラス(XREAL One)自身のIMUを USBネットワーク越しに受け取り、頭の向きと CSV の加速度に使う。
/// 届かなくても走行は止めない(AirPods と同じ扱い — タスク表 S4)
private struct GlassImuRow: View {
    let status: GlassImuStatus
    let onConnect: () -> Void
    let onRetry: () -> Void

    private var tint: Color {
        switch status {
        case .active: return .arYellow
        case .searching: return .white
        case .notFound: return .orange
        case .off: return .arGrayText
        }
    }

    private var detail: LocalizedStringKey {
        switch status {
        case .off: return AppStrings.Devices.glassImuSubtitle
        case .searching: return AppStrings.Devices.glassImuSearching
        case .active: return AppStrings.Devices.glassImuActive
        case .notFound: return AppStrings.Devices.glassImuNotFound
        }
    }

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            ZStack {
                Circle()
                    .fill(Color.arBG)
                    .frame(width: 56, height: 56)
                    .overlay(Circle().stroke(status == .off ? Color.arBorder : tint.opacity(0.6), lineWidth: 1))
                Image(systemName: "gyroscope")
                    .font(.system(size: 24))
                    .foregroundColor(status == .off ? .arGrayText : tint)
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(AppStrings.Devices.glassImuName)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                Text(detail)
                    .font(.system(size: 12))
                    .foregroundColor(status == .off ? .arGrayText : tint)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Spacer(minLength: 0)

            switch status {
            case .off:
                pill(AppStrings.Devices.connect, action: onConnect)
            case .searching:
                ProgressView().tint(.white)
            case .active:
                Image(systemName: "checkmark")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(.black)
                    .frame(width: 28, height: 28)
                    .background(Color.arYellow)
                    .clipShape(Circle())
            case .notFound:
                pill(AppStrings.Devices.reconnect, action: onRetry)
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(status == .off ? Color.arBorder : tint.opacity(0.35), lineWidth: 1)
        )
        .animation(.easeInOut(duration: 0.2), value: status)
    }

    private func pill(_ title: LocalizedStringKey, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.arYellow)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(Color.arYellow.opacity(0.12))
                .clipShape(Capsule())
                .overlay(Capsule().stroke(Color.arYellow.opacity(0.4), lineWidth: 1))
        }
        .buttonStyle(.plain)
    }
}
