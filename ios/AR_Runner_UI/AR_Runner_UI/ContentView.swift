import SwiftUI

// MARK: - Navigation State
enum AppScreen {
    case onboarding
    case deviceConnect
    case runningSettings
    case mapRoute
    case running
    case lockScreen
    case stats
    case history
}

struct ContentView: View {
    @State private var screen: AppScreen = .onboarding

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            switch screen {

            // 1. Three-page tutorial
            case .onboarding:
                OnboardingView(
                    onNext: { screen = .deviceConnect },
                    onBack: { }
                )

            // 2. Connect AR glasses + Apple Watch + AirPods
            case .deviceConnect:
                DeviceConnectView(
                    onNext: { screen = .runningSettings },
                    onBack: { screen = .onboarding }
                )

            // 3. Set time, distance, pace
            case .runningSettings:
                RunningSettingsView(
                    onNext: { screen = .mapRoute },
                    onBack: { screen = .deviceConnect }
                )

            // 4. Draw route on map
            case .mapRoute:
                MapRouteView(
                    onStart: { screen = .running },
                    onBack: { screen = .runningSettings }
                )

            // 5. Running screen (Unity ARビュー + HUD)
            case .running:
                RunningView(
                    onEnd: { screen = .stats }
                )

            // 7. Stats
            case .stats:
                StatsView(
                    onHistory: { screen = .history },
                    onBack: { screen = .mapRoute }
                )

            // 8. History
            case .history:
                HistoryView(
                    onBack: { screen = .stats }
                )
            case .lockScreen:
                        LockScreenView(
                            onUnlock: { screen = .stats }
                        )
            }
        }
        .animation(.easeInOut(duration: 0.3), value: screen)
    }
}
