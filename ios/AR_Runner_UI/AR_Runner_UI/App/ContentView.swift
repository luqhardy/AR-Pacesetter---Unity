import SwiftUI

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

    /// 走行中にグラスが切れて準備画面へ戻ったか(§8.3)。準備画面がつなぎ直しを案内する
    @State private var glassLostDuringRun = false

    init() {
        // @AppStorage の値を待って onAppear で切り替えると、2回目以降の起動でも
        // オンボーディングが一瞬見えてしまう。初期値の時点で行き先を決める
        let seen     = UserDefaults.standard.bool(forKey: "hasSeenOnboarding")
        let accepted = UserDefaults.standard.bool(forKey: "hasAcceptedDisclaimer")
        // 身体情報(S9-2)は免責の後に1度だけ。この画面ができる前に使い始めた人にも次の起動で1度出す
        let initial: AppScreen = !(seen && accepted) ? .onboarding
            : BodyInfoStore.shared.hasCompletedInitialSetup ? .home : .bodyInfo
        _screen = State(initialValue: initial)
    }

    /// 初回の流れの最後(免責に同意した後)。身体情報をまだ通っていなければそこへ
    private var afterFirstRun: AppScreen {
        BodyInfoStore.shared.hasCompletedInitialSetup ? .home : .bodyInfo
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            switch screen {

            // 0. ホーム — 走行フローの起点。終了後も必ずここへ戻る。
            //    デザインは hsuyaminmyat625/AR_project の HomeView を採用(2026-09-11 合体)。
            //    ハンバーガー(履歴/AR設定/スマホ設定/使い方/安全上の注意)とスライド開始を持つ
            case .home:
                HomeView(
                    onStartRun:   { screen = .deviceConnect },
                    onHistory:    { historyOrigin = .home; screen = .history },
                    onARSettings: { screen = .glassSettings },
                    onPhoneSettings: { screen = .phoneSettings },
                    onTutorial:   { isReviewing = true; screen = .onboarding },
                    onDisclaimer: { isReviewing = true; screen = .disclaimer },
                    onBoothDemo:  { screen = .boothDemo }
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
                        screen = afterFirstRun
                    },
                    onClose: {
                        isReviewing = false
                        screen = .home
                    }
                )

            // 2. Connect AR glasses + Apple Watch + AirPods
            case .deviceConnect:
                DeviceConnectView(
                    onNext: {
                        glassLostDuringRun = false
                        screen = .runningSettings
                    },
                    onBack: {
                        glassLostDuringRun = false
                        screen = .home
                    },
                    returnedFromRun: glassLostDuringRun
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
                    onNext: { screen = .running },
                    onBack: { screen = .runningSettings }
                )

            // 5. Running screen (Unity ARビュー + HUD)
            //    ロック画面は走行画面内のオーバーレイ。画面遷移にするとUnityのARビューが
            //    一度ヒエラルキーから外れるため、走行を止めずに覆うほうが安全
            case .running:
                RunningView(
                    onEnd: { screen = .stats },
                    // §8.3: グラス切断は準備画面へ戻す(走行記録・CSVログは継続し、
                    // 再接続後の再スタート操作でアバターが復帰する)
                    onGlassDisconnected: {
                        glassLostDuringRun = true
                        screen = .deviceConnect
                    }
                )

            // 6. Stats — 「終了」でホームへ戻る(課題 #9)
            case .stats:
                StatsView(
                    onHistory: { historyOrigin = .stats; screen = .history },
                    onBack:    { screen = .home },
                    // 「終了」と「戻る」を分けた(hsu/main 由来)。どちらもホームだが、
                    // 戻る導線が後から変わっても終了の行き先は動かない
                    onFinish:  { screen = .home }
                )

            // 7. History
            case .history:
                HistoryView(
                    onBack: { screen = historyOrigin },
                    onStartGhost: { screen = .running } // ゴースト競走を開始
                )

            // 8. 展示ブースの体験モード(Kobe Calling)— スタッフが操作する。
            //    走行フロー(ARSessionManager)は通らず、履歴・HealthKitにも残らない
            case .boothDemo:
                BoothDemoView(onExit: { screen = .home })

            // 9. AR設定・スマホ設定(ホームのメニューから)。hsuyaminmyat625/AR_project の画面を
            //    実際に効く項目だけで移植 — グラスの明るさ等、iOS から変えられないものは置かない
            case .glassSettings:
                ARSettingsView(onBack: { screen = .home })
            case .phoneSettings:
                PhoneSettingsView(
                    onBack: { screen = .home },
                    onEditBodyInfo: { screen = .bodyInfoEdit }
                )

            // 11. 身体情報(S9-2)— hsuyaminmyat625/AR_project から移植。すべて任意で、今は保存するだけ
            case .bodyInfo:
                BodyInfoView(isInitialSetup: true, onNext: { screen = .home }, onBack: { })
            case .bodyInfoEdit:
                BodyInfoView(
                    isInitialSetup: false,
                    onNext: { screen = .phoneSettings },
                    onBack: { screen = .phoneSettings }
                )
            }
        }
        .animation(.easeInOut(duration: 0.3), value: screen)
    }
}
