import SwiftUI

// MARK: - Navigation State
enum AppScreen {
    case onboarding
    case home
    case deviceConnect
    case runningSettings
    case mapRoute
    case lockScreen
    case stats
    case history
}

struct ContentView: View {
    private static let onboardingCompletionKey = "hasCompletedOnboarding"
    @State private var screen: AppScreen
    
    init() {
        _screen = State(
            initialValue: UserDefaults.standard.bool(forKey: Self.onboardingCompletionKey)
            ? .deviceConnect
            : .onboarding
        )
    }
    
//struct ContentView: View {
    //@State private var screen: AppScreen = .onboarding

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            switch screen {

            // 1. Three-page tutorial
            case .onboarding:
                OnboardingView(
                    onNext: { screen = .home },
                    onBack: { }
                )
            // add on
            case .home:
                HomeView(
                    onNext: { screen = .deviceConnect },
                    onBack: { }
                )
                
                
            // 2. Connect AR glasses + Apple Watch + AirPods
            case .deviceConnect:
                DeviceConnectView(
                    onNext: { screen = .runningSettings },
                    onBack: { screen = .home }
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
                    onStart: { screen = .lockScreen }, // Routes back to Lock Screen
                    onBack: { screen = .runningSettings }
                )



            // 7. Stats
            case .stats:
                StatsView(
                    onHistory: { screen = .history },
                    onBack: { screen = .lockScreen }
                )

            // 8. History
            case .history:
                HistoryView(
                    onBack: { screen = .home }
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
