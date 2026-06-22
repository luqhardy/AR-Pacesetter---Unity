import SwiftUI

// MARK: - AR Lock Screen
// Pure black screen shown while running on AR glasses.
// Swipe up to unlock and return to the running HUD.
struct LockScreenView: View {
    let onUnlock: () -> Void

    @State private var dragOffset: CGFloat = 0
    @State private var unlocking = false

    // How far the user needs to swipe up to trigger unlock
    private let unlockThreshold: CGFloat = 160

    var body: some View {
        ZStack {
            // Pure black background
            Color.black.ignoresSafeArea()

            VStack(spacing: 0) {
                Spacer()

                // Lock icon + label
                VStack(spacing: 20) {
                    ZStack {
                        // Outer ring, fades as user swipes
                        Circle()
                            .stroke(Color.white.opacity(0.08 - (progress * 0.08)), lineWidth: 1)
                            .frame(width: 88, height: 88)

                        Image(systemName: unlocking ? "lock.open" : "lock")
                            .font(.system(size: 30, weight: .light))
                            .foregroundColor(Color.white.opacity(0.6 + progress * 0.4))
                            .scaleEffect(1 + progress * 0.2)
                            .animation(.easeOut(duration: 0.1), value: dragOffset)
                    }

                    VStack(spacing: 6) {
                        Text("AR ランニング中")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(Color.white.opacity(0.3))
                            .tracking(1)

                        HStack(spacing: 6) {
                            Image(systemName: "chevron.up")
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundColor(Color.white.opacity(0.25 + progress * 0.5))
                                .offset(y: -dragOffset * 0.05)
                                .animation(
                                    Animation.easeInOut(duration: 0.8)
                                        .repeatForever(autoreverses: true),
                                    value: UUID()
                                )
                            Text("スワイプして解除")
                                .font(.system(size: 13))
                                .foregroundColor(Color.white.opacity(0.25 + progress * 0.5))
                        }
                    }
                }
                // Shift upward as user swipes
                .offset(y: -dragOffset * 0.4)
                .animation(.interactiveSpring(), value: dragOffset)

                Spacer()

                // Swipe progress bar at bottom (very subtle)
                if dragOffset > 10 {
                    Capsule()
                        .fill(Color.white.opacity(0.15))
                        .frame(width: 120, height: 3)
                        .overlay(
                            Capsule()
                                .fill(Color.white.opacity(0.5))
                                .frame(width: 120 * progress, height: 3),
                            alignment: .leading
                        )
                        .padding(.bottom, 48)
                        .transition(.opacity)
                }
            }
        }
        .gesture(
            DragGesture(minimumDistance: 10)
                .onChanged { value in
                    // Only respond to upward swipes
                    let translation = -value.translation.height
                    if translation > 0 {
                        dragOffset = translation
                    }
                }
                .onEnded { value in
                    let translation = -value.translation.height
                    if translation > unlockThreshold {
                        // Unlock!
                        withAnimation(.easeOut(duration: 0.2)) {
                            unlocking = true
                            dragOffset = unlockThreshold + 20
                        }
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                            onUnlock()
                        }
                    } else {
                        // Snap back
                        withAnimation(.spring(response: 0.35, dampingFraction: 0.7)) {
                            dragOffset = 0
                        }
                    }
                }
        )
        .statusBarHidden(true)
    }

    // 0.0 → 1.0 progress toward unlock threshold
    private var progress: Double {
        min(1.0, Double(dragOffset) / Double(unlockThreshold))
    }
}
