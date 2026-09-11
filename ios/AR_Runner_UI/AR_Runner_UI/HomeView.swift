import SwiftUI

// MARK: - 0. Home Screen
/// アプリの起点。走行フローは必ずここから始まり、結果画面の「終了」でもここへ戻る。
///
/// 以前は「起動＝オンボーディング直行」の一本道で、結果画面の終了は地図画面へ戻っていた。
/// そのため一度走り終えると「最初に戻る」手段が無く、チュートリアルも二度と見られなかった
/// (課題 #6 / #9 / #10)。起点をひとつ置くことで、どの画面からでも帰る先が決まる。
struct HomeView: View {
    let onStartRun: () -> Void
    let onHistory: () -> Void
    let onDevices: () -> Void
    let onTutorial: () -> Void
    let onDisclaimer: () -> Void

    /// 実グラス(USB-C外部ディスプレイ)の接続状態をそのまま映す
    @ObservedObject private var external = ExternalDisplayManager.shared

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {

                // Header
                VStack(alignment: .leading, spacing: 6) {
                    ARLabel(text: "AR PACESETTER")
                    Text("走ろう。")
                        .font(.system(size: 34, weight: .bold))
                        .foregroundColor(.white)
                    Text("3m先の相棒が、あなたのペースをつくる。")
                        .font(.system(size: 14))
                        .foregroundColor(.arGrayText)
                        .padding(.top, 2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.top, 72)

                Spacer()

                // グラス接続ステータス — 走る前に一番知りたい情報なので常設する
                HStack(spacing: 14) {
                    Image(systemName: "eyeglasses")
                        .font(.system(size: 22))
                        .foregroundColor(external.isGlassesConnected ? .arYellow : .arGrayText)

                    VStack(alignment: .leading, spacing: 3) {
                        Text(external.isGlassesConnected ? "ARグラス接続済み" : "ARグラス未接続")
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundColor(.white)
                        Text(external.isGlassesConnected
                             ? "映像はグラスへ出力されます"
                             : "USB-Cで接続、または端末画面のまま走れます")
                            .font(.system(size: 12))
                            .foregroundColor(.arGrayText)
                    }

                    Spacer()
                }
                .padding(16)
                .background(Color.arCard, in: RoundedRectangle(cornerRadius: 18))
                .overlay(
                    RoundedRectangle(cornerRadius: 18)
                        .stroke(external.isGlassesConnected ? Color.arYellow.opacity(0.35) : Color.arBorder,
                                lineWidth: 1)
                )
                .padding(.horizontal, 24)
                .padding(.bottom, 20)
                .animation(.easeInOut(duration: 0.2), value: external.isGlassesConnected)

                // Actions
                VStack(spacing: 12) {
                    ARButton("走る", icon: "figure.run", action: onStartRun)

                    HStack(spacing: 12) {
                        HomeTileButton(icon: "clock.arrow.circlepath", title: "履歴", action: onHistory)
                        HomeTileButton(icon: "eyeglasses", title: "デバイス", action: onDevices)
                    }
                    HStack(spacing: 12) {
                        HomeTileButton(icon: "book", title: "使い方", action: onTutorial)
                        HomeTileButton(icon: "exclamationmark.shield", title: "安全上の注意", action: onDisclaimer)
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 52)
            }
        }
    }
}

// MARK: - Home Tile Button
/// ホームの副アクション。ARButton(全幅)だと縦に伸びすぎるため2列で並べる
struct HomeTileButton: View {
    let icon: String
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 8) {
                Image(systemName: icon)
                    .font(.system(size: 20, weight: .medium))
                    .foregroundColor(.arYellow)
                Text(title)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.white)
            }
            .frame(maxWidth: .infinity)
            .frame(height: 78)
            .background(Color.arCard, in: RoundedRectangle(cornerRadius: 18))
            .overlay(
                RoundedRectangle(cornerRadius: 18)
                    .stroke(Color.arBorder, lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
    }
}
