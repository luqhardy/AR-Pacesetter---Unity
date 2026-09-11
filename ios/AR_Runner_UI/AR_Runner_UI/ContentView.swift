import SwiftUI

// MARK: - Navigation State
enum AppScreen {
    case home
    case onboarding
    case disclaimer
    case deviceConnect
    case runningSettings
    case mapRoute
    case running
    case stats
    case history
}

struct ContentView: View {
    /// 初回起動の判定。オンボーディングと免責は**一度通ればスキップ**し、
    /// 以降はホームからいつでも開き直せる(課題 #6)
    @AppStorage("hasSeenOnboarding")     private var hasSeenOnboarding = false
    @AppStorage("hasAcceptedDisclaimer") private var hasAcceptedDisclaimer = false

    @State private var screen: AppScreen

    /// オンボーディング/免責を「ホームから読み返している」か。初回フローとは戻り先が違う
    @State private var isReviewing = false

    /// 履歴画面の戻り先。ホームから来たか結果画面から来たかで変わる
    @State private var historyOrigin: AppScreen = .home

    init() {
        // @AppStorage の値を待って onAppear で切り替えると、2回目以降の起動でも
        // オンボーディングが一瞬見えてしまう。初期値の時点で行き先を決める
        let seen     = UserDefaults.standard.bool(forKey: "hasSeenOnboarding")
        let accepted = UserDefaults.standard.bool(forKey: "hasAcceptedDisclaimer")
        _screen = State(initialValue: (seen && accepted) ? .home : .onboarding)
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            switch screen {

            // 0. ホーム — 走行フローの起点。終了後も必ずここへ戻る。
            //    デザインは hsuyaminmyat625/AR_project の HomeView を採用(2026-09-11 合体)。
            //    ハンバーガー(履歴/AR設定/使い方/安全上の注意)とスライド開始を持つ
            case .home:
                HomeView(
                    onStartRun:   { screen = .deviceConnect },
                    onHistory:    { historyOrigin = .home; screen = .history },
                    onDevices:    { screen = .deviceConnect },
                    onTutorial:   { isReviewing = true; screen = .onboarding },
                    onDisclaimer: { isReviewing = true; screen = .disclaimer }
                )

            // 1. Three-page tutorial (初回のみ自動表示 / ホームからいつでも再表示)
            case .onboarding:
                OnboardingView(
                    onNext: {
                        hasSeenOnboarding = true
                        // 初回は免責へ。読み返しならホームへ戻す
                        if isReviewing {
                            isReviewing = false
                            screen = .home
                        } else {
                            screen = .disclaimer
                        }
                    },
                    onBack: {
                        isReviewing = false
                        screen = .home
                    }
                )

            // 1b. 免責・安全上の注意 (初回は同意が必要 / 以降はホームから閲覧)
            case .disclaimer:
                DisclaimerView(
                    requiresConsent: !hasAcceptedDisclaimer,
                    onAgree: {
                        hasAcceptedDisclaimer = true
                        isReviewing = false
                        screen = .home
                    },
                    onClose: {
                        isReviewing = false
                        screen = .home
                    }
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
                    onStart: { screen = .running },
                    onBack:  { screen = .runningSettings }
                )

            // 5. Running screen (Unity ARビュー + HUD)
            //    ロック画面は走行画面内のオーバーレイ。画面遷移にするとUnityのARビューが
            //    一度ヒエラルキーから外れるため、走行を止めずに覆うほうが安全
            case .running:
                RunningView(
                    onEnd: { screen = .stats },
                    // §8.3: グラス切断は準備画面へ戻す(走行記録・CSVログは継続し、
                    // 再接続後の再スタート操作でアバターが復帰する)
                    onGlassDisconnected: { screen = .deviceConnect }
                )

            // 6. Stats — 「終了」でホームへ戻る(課題 #9)
            case .stats:
                StatsView(
                    onHistory: { historyOrigin = .stats; screen = .history },
                    onBack:    { screen = .home }
                )

            // 7. History
            case .history:
                HistoryView(
                    onBack: { screen = historyOrigin },
                    onStartGhost: { screen = .running } // ゴースト競走を開始
                )
            }
        }
        .animation(.easeInOut(duration: 0.3), value: screen)
    }
}
