import SwiftUI
import UIKit

/// Explains why location is needed when the user has denied or restricted it.
struct LocationPermissionMessage: View {
    @Environment(\.openURL) private var openURL

    var body: some View {
        VStack(spacing: 10) {
            Image(systemName: "location.slash")
                .font(.system(size: 22, weight: .semibold))
                .foregroundColor(.arYellow)

            Text("位置情報が許可されていません")
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(.white)

            Text("現在地とルートを表示するには、設定から位置情報を許可してください。")
                .font(.system(size: 13))
                .multilineTextAlignment(.center)
                .foregroundColor(.arGrayText)

            Button("設定を開く") {
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
