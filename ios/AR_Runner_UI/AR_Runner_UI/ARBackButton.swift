import SwiftUI

// MARK: - Back Button
// Pill-shaped, blur-backed, with a yellow chevron and "戻る" label.
// Sits comfortably in any ZStack without fighting other UI elements.
struct ARBackButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 5) {
                Image(systemName: "chevron.left")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(.arYellow)
                Text("戻る")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(.white)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 9)
            .background(.ultraThinMaterial, in: Capsule())
            .overlay(
                Capsule()
                    .strokeBorder(Color.arYellow.opacity(0.35), lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
    }
}
