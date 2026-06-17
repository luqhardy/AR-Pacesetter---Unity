import SwiftUI

// MARK: - Navigation State
enum AppScreen {
    case connect, onboarding, courseSetup, running, stats, history
}

struct ContentView: View {
    @State private var screen: AppScreen = .connect
    @State private var isXREALConnected = false

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            
            switch screen {
            case .connect:
                ConnectView(
                    isConnected: $isXREALConnected,
                    onNext: { screen = .onboarding }
                )
                
            case .onboarding:
                OnboardingView(
                    onNext: { screen = .courseSetup },
                    onBack: { screen = .connect } // Goes back to Connect
                )
                
            case .courseSetup:
                // Assuming you add an 'onBack' parameter to CourseSetupView too
                CourseSetupView(
                    onStart: { screen = .running },
                    onSettings: { /* Handle settings */ },
                    onBack: { screen = .onboarding } // Goes back to Onboarding
                )
                
            case .running:
                RunningView(
                    onEnd: { screen = .stats }
                    // Usually you don't want a back button during an active run,
                    // but you can add one here if needed!
                )
                
            case .stats:
                StatsView(
                    onHistory: { screen = .history },
                    onBack: { screen = .courseSetup } // Goes back to setup
                )
                
            case .history:
                HistoryView(
                    onBack: { screen = .stats } // Goes back to stats
                )
            }
        }
        // Optional: Adds a smooth fade transition when switching screens
        .animation(.easeInOut, value: screen)
    }
}
