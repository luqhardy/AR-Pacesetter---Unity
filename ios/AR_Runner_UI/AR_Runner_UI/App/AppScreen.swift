// MARK: - Navigation State
// 画面の一覧。遷移は ContentView が持つ(hsuyaminmyat625/AR_project の構成に合わせて分離)
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
    case boothDemo
    case glassSettings
    case phoneSettings
    case bodyInfo        // 初回(オンボーディングと免責の後に1度)
    case bodyInfoEdit    // スマホ設定から編集
}
