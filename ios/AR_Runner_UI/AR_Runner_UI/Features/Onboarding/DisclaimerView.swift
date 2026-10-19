import SwiftUI

// MARK: - Disclaimer / Safety Notice
/// 免責・安全上の注意。初回起動時に一度だけ同意を求め、以降はホームから読み返せる。
///
/// ⚠ **文言はドラフト**。AR越しに走るというアプリの性質上、掲示内容はチーム(および必要なら
/// 法務)の確認を経て確定させること。文言は AppStrings.Safety(日本語・英語は Localizable.xcstrings)。
struct DisclaimerView: View {
    /// 初回起動か(=同意ボタンを出すか)。ホームからの閲覧では「閉じる」だけ出す
    let requiresConsent: Bool
    let onAgree: () -> Void
    let onClose: () -> Void

    private let notices: [(icon: String, title: LocalizedStringKey, body: LocalizedStringKey)] = [
        ("eye.trianglebadge.exclamationmark", AppStrings.Safety.surroundingsTitle, AppStrings.Safety.surroundingsBody),
        ("figure.run.circle", AppStrings.Safety.placeTitle, AppStrings.Safety.placeBody),
        ("person.fill.viewfinder", AppStrings.Safety.virtualTitle, AppStrings.Safety.virtualBody),
        ("heart.text.square", AppStrings.Safety.healthTitle, AppStrings.Safety.healthBody),
        ("exclamationmark.shield", AppStrings.Safety.disclaimerTitle, AppStrings.Safety.disclaimerBody),
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
                    ARLabel(text: AppStrings.Safety.eyebrow)
                    Text(AppStrings.Safety.title)
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 20)

                ScrollView {
                    VStack(spacing: 12) {
                        ForEach(notices, id: \.icon) { notice in
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
                    ARButton(AppStrings.Safety.agree, icon: "checkmark") { onAgree() }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                } else {
                    ARButton(AppStrings.Common.close, style: .secondary) { onClose() }
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                }
            }
        }
    }
}
