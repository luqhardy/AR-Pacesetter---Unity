import SwiftUI

struct ARBackButton: View {
    let action: () -> Void // Add this closure
    
    var body: some View {
        Button(action: action) {
            Image(systemName: "chevron.left")
                .font(.system(size: 18, weight: .bold))
                .foregroundColor(.white)
                .frame(width: 44, height: 44)
                .background(Color.black.opacity(0.6))
                .clipShape(Circle())
                .overlay(Circle().stroke(Color.arBorder, lineWidth: 1))
        }
    }
}
