import SwiftUI
import UIKit

/// Explains why location can't be used, and what to do about it.
/// 拒否・制限・位置情報サービスのオフで案内を分ける(判断は AppLogic/LocationProblem)。
/// 制限(ペアレンタルコントロール・管理プロファイル)は本人には変えられないので「設定を開く」を出さない
struct LocationPermissionMessage: View {
    let problem: LocationProblem

    @Environment(\.openURL) private var openURL

    private var title: LocalizedStringKey {
        switch problem {
        case .denied: return AppStrings.Location.permissionTitle
        case .restricted: return AppStrings.Location.restrictedTitle
        case .servicesOff: return AppStrings.Location.servicesOffTitle
        }
    }

    private var message: LocalizedStringKey {
        switch problem {
        case .denied: return AppStrings.Location.permissionMessage
        case .restricted: return AppStrings.Location.restrictedMessage
        case .servicesOff: return AppStrings.Location.servicesOffMessage
        }
    }

    var body: some View {
        VStack(spacing: 10) {
            Image(systemName: "location.slash")
                .font(.system(size: 22, weight: .semibold))
                .foregroundColor(.arYellow)

            Text(title)
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(.white)
                .multilineTextAlignment(.center)

            Text(message)
                .font(.system(size: 13))
                .multilineTextAlignment(.center)
                .foregroundColor(.arGrayText)

            if problem.userCanFix {
                Button(AppStrings.Common.openSettings) {
                    guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
                    openURL(url)
                }
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.black)
                .padding(.horizontal, 16)
                .padding(.vertical, 9)
                .background(Color.arYellow)
                .clipShape(Capsule())
            }
        }
        .padding(18)
        .frame(maxWidth: 300)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(Color.arBorder, lineWidth: 1)
        )
    }
}
