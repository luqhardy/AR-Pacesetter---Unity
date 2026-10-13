import SwiftUI

// MARK: - Device Connect Screen (All devices on one screen)
struct DeviceConnectView: View {
    let onNext: () -> Void
    let onBack: () -> Void

    /// 手動の「接続」を押したか。実接続の検出とは別物として扱う
    @State private var manualConnect = false

    // 実グラス接続(USB-C外部ディスプレイ)を検知したら表示に反映
    @ObservedObject private var external = ExternalDisplayManager.shared

    /// ARグラス行の表示。**実際の状態だけを出す**。
    /// 以前はタップから1.5秒後に、何も挿さっていなくても「接続済み」になった。そのため
    /// iOS がグラスへミラーリングしている(=グラス専用の画面が無い)ことに気づけなかった(2026-10-07 実機)
    private var glassStatus: GlassRowStatus {
        if external.isGlassesConnected { return .connected }
        if external.isMirroring { return .mirroring }
        if manualConnect { return .manual }
        return .notDetected
    }

    /// 走行に必要なのはARグラスだけ。Watch/AirPodsは第1期のスコープ外(基本設計書 §1.2)
    var allConnected: Bool { glassStatus == .connected }

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 20)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: "SETUP")
                    Text("デバイスを接続")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text("使用するデバイスを接続してください")
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
                        name: "ARグラス",
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
                    // Watch / AirPods は第1期のスコープ外(基本設計書 §1.2 —
                    // 通信ボトルネックを排除しM2P 20msを死守するため、iPhone+グラスの
                    // 2デバイスに特化する)。以前はタップから1秒後に「接続済み」と
                    // 表示するだけの**見せかけ**だったので、実態どおりの表示に改めた
                    DeviceUnavailableRow(
                        icon: "applewatch",
                        name: "Apple Watch",
                        note: "第1期のスコープ外"
                    )
                    DeviceUnavailableRow(
                        icon: "airpodspro",
                        name: "AirPods",
                        note: "第1期のスコープ外"
                    )
                }
                .padding(.horizontal, 24)

                Spacer()

                // Status hint
                Text(statusHint)
                    .font(.system(size: 13))
                    .foregroundColor(glassStatus.color)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 32)
                    .padding(.bottom, 16)

                // Next button — always available (user can skip devices)
                Button(action: onNext) {
                    ZStack {
                        Circle()
                            .fill(Color.arYellow)
                            .frame(width: 64, height: 64)
                            .shadow(color: Color.arYellow.opacity(allConnected ? 0.55 : 0.25), radius: 20)
                        Image(systemName: "arrow.right")
                            .font(.system(size: 22, weight: .bold))
                            .foregroundColor(.black)
                    }
                }
                .padding(.bottom, 52)
            }
            .onAppear {
                external.refreshMirroring()
            }
            .onChange(of: external.isGlassesConnected) { _, _ in
                // 実グラスを挿したら手動扱いは不要。抜いたら手動扱いも解く(実態に合わせる)
                manualConnect = false
            }
        }
    }

    private var statusHint: String {
        switch glassStatus {
        case .connected:
            return "ARグラス接続済み — このまま進めます"
        case .mirroring:
            return "グラスにiPhoneの画面がそのまま映っています(ミラーリング)。グラスを挿し直してください"
        case .manual:
            return "グラスは検出されていません。手動でグラス用の表示にしています"
        case .notDetected:
            return "ARグラスをUSB-Cで接続してください(未接続でも端末画面で走れます)"
        }
    }
}

// MARK: - ARグラス行の状態
/// 実接続(iOS がグラス専用の画面を作った)だけを「接続済み」とする
enum GlassRowStatus {
    case connected    // 専用の画面にARビューを出している
    case mirroring    // 挿さっているが iOS がミラーリングしている
    case manual       // 検出は無いが、手動でグラス用の表示にした
    case notDetected

    var color: Color {
        switch self {
        case .connected: return .arYellow
        case .mirroring, .manual: return .orange
        case .notDetected: return .arGrayText
        }
    }

    var label: String? {
        switch self {
        case .connected: return "接続済み"
        case .mirroring: return "ミラーリング中"
        case .manual: return "手動(未検出)"
        case .notDetected: return nil
        }
    }
}

// MARK: - Device Connect Row
struct DeviceConnectRow: View {
    let icon: String
    let name: String
    let subtitle: String
    let status: GlassRowStatus
    let onConnect: () -> Void

    private var isConnected: Bool { status == .connected }
    /// 手動の接続を押せるのは、実接続も手動接続もしていないときだけ
    private var canConnect: Bool { status == .notDetected || status == .mirroring }

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
                    Text("手動で接続")
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
    let name: String
    let note: String

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
