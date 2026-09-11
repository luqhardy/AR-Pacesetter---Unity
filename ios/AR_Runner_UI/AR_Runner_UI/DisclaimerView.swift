import SwiftUI

// MARK: - Disclaimer / Safety Notice
/// 免責・安全上の注意。初回起動時に一度だけ同意を求め、以降はホームから読み返せる。
///
/// ⚠ **文言はドラフト**。AR越しに走るというアプリの性質上、掲示内容はチーム(および必要なら
/// 法務)の確認を経て確定させること。差し替えは `notices` 配列だけで済むようにしてある。
struct DisclaimerView: View {
    /// 初回起動か(=同意ボタンを出すか)。ホームからの閲覧では「閉じる」だけ出す
    let requiresConsent: Bool
    let onAgree: () -> Void
    let onClose: () -> Void

    private let notices: [(icon: String, title: String, body: String)] = [
        ("eye.trianglebadge.exclamationmark",
         "周囲の安全を最優先に",
         "ARグラスは視界に映像を重ねます。路面・障害物・他の利用者から注意をそらさないでください。"),
        ("figure.run.circle",
         "走行に適した場所で",
         "陸上トラックなど安全な場所でご使用ください。交通量のある道路、段差・階段の近く、\n人混みでの使用は避けてください。"),
        ("person.fill.viewfinder",
         "アバターは仮想の映像です",
         "3m前方のアバターは実在しません。実際の障害物を覆い隠すことがあります。\n見えているものより、目の前の現実を優先してください。"),
        ("heart.text.square",
         "体調に異変を感じたら中止を",
         "めまい・吐き気・目の疲れ・痛みを感じた場合は、直ちに使用を中止してください。"),
        ("exclamationmark.shield",
         "免責事項",
         "本アプリの使用中に生じた事故・負傷・損害について、開発者および提供者は\n一切の責任を負いかねます。使用は利用者ご自身の判断と責任において行ってください。"),
    ]

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {

                // Nav bar — 読み返しのときだけ戻れる(初回は同意で先へ進む)
                HStack {
                    if !requiresConsent {
                        ARBackButton(action: onClose)
                    }
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: "SAFETY")
                    Text("安全上の注意")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 20)

                ScrollView {
                    VStack(spacing: 12) {
                        ForEach(notices, id: \.title) { notice in
                            HStack(alignment: .top, spacing: 14) {
                                Image(systemName: notice.icon)
                                    .font(.system(size: 18))
                                    .foregroundColor(.arYellow)
                                    .frame(width: 26)

                                VStack(alignment: .leading, spacing: 5) {
                                    Text(notice.title)
                                        .font(.system(size: 15, weight: .bold))
                                        .foregroundColor(.white)
                                    Text(notice.body)
                                        .font(.system(size: 13))
                                        .foregroundColor(.arGrayText)
                                        .lineSpacing(4)
                                        .fixedSize(horizontal: false, vertical: true)
                                }

                                Spacer(minLength: 0)
                            }
                            .padding(16)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.arCard, in: RoundedRectangle(cornerRadius: 16))
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 24)
                }

                if requiresConsent {
                    ARButton("同意して始める", icon: "checkmark") { onAgree() }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                } else {
                    ARButton("閉じる", style: .secondary) { onClose() }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                }
            }
        }
    }
}
