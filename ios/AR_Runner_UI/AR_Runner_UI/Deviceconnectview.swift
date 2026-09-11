import SwiftUI

// MARK: - Device Connect Screen (All devices on one screen)
struct DeviceConnectView: View {
    let onNext: () -> Void
    let onBack: () -> Void

    @State private var arConnected = false
    @State private var arScanning = false
    @State private var pulse = false

    // 実グラス接続(USB-C外部ディスプレイ)を検知したら表示に反映
    @ObservedObject private var external = ExternalDisplayManager.shared

    /// 走行に必要なのはARグラスだけ。Watch/AirPodsは第1期のスコープ外(基本設計書 §1.2)
    var allConnected: Bool { arConnected }

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
                        isConnected: $arConnected,
                        isScanning: $arScanning,
                        onConnect: {
                            arScanning = true
                            // UnityへConnectXREAL送信(UnityのReadyチェックが更新される)
                            UnityBridge.shared.connect()
                            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                                arScanning = false
                                arConnected = true
                            }
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
                Text(allConnected
                     ? "ARグラス接続済み — このまま進めます"
                     : "ARグラスをUSB-Cで接続してください(未接続でも端末画面で走れます)")
                    .font(.system(size: 13))
                    .foregroundColor(allConnected ? .arYellow : .arGrayText)
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
                if external.isGlassesConnected { arConnected = true }
            }
            .onChange(of: external.isGlassesConnected) { _, connected in
                // 実グラスの抜き差しを即時反映(タップ不要)
                arScanning = false
                arConnected = connected
            }
        }
    }
}

// MARK: - Device Connect Row
struct DeviceConnectRow: View {
    let icon: String
    let name: String
    let subtitle: String
    @Binding var isConnected: Bool
    @Binding var isScanning: Bool
    let onConnect: () -> Void

    var body: some View {
        Button(action: {
            if !isConnected && !isScanning { onConnect() }
        }) {
            HStack(spacing: 16) {
                // Icon
                ZStack {
                    Circle()
                        .fill(Color.arBG)
                        .frame(width: 56, height: 56)
                        .overlay(
                            Circle().stroke(
                                isConnected ? Color.arYellow.opacity(0.6) : Color.arBorder,
                                lineWidth: 1
                            )
                        )
                    Image(systemName: icon)
                        .font(.system(size: 24))
                        .foregroundColor(isConnected ? .arYellow : .arGrayText)
                }

                // Text
                VStack(alignment: .leading, spacing: 3) {
                    Text(name)
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundColor(.white)
                    HStack(spacing: 5) {
                        if isConnected {
                            Image(systemName: "checkmark.circle.fill")
                                .font(.system(size: 11))
                                .foregroundColor(.arYellow)
                            Text("接続済み")
                                .font(.system(size: 13))
                                .foregroundColor(.arYellow)
                        } else if isScanning {
                            ProgressView()
                                .scaleEffect(0.7)
                                .tint(Color.arGrayText)
                            Text("接続中...")
                                .font(.system(size: 13))
                                .foregroundColor(.arGrayText)
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
                } else {
                    Text(isScanning ? "..." : "接続")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(isScanning ? .arGrayText : .arYellow)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .background(Color.arYellow.opacity(isScanning ? 0.05 : 0.12))
                        .clipShape(Capsule())
                        .overlay(
                            Capsule().stroke(Color.arYellow.opacity(isScanning ? 0.2 : 0.4), lineWidth: 1)
                        )
                }
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 16)
            .background(Color.arCard)
            .clipShape(RoundedRectangle(cornerRadius: 18))
            .overlay(
                RoundedRectangle(cornerRadius: 18)
                    .stroke(isConnected ? Color.arYellow.opacity(0.35) : Color.arBorder, lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
        .animation(.easeInOut(duration: 0.2), value: isConnected)
        .animation(.easeInOut(duration: 0.2), value: isScanning)
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
