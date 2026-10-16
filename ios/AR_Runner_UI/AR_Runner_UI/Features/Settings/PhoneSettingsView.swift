import SwiftUI
import CoreLocation
import UIKit

// MARK: - スマホ設定
// hsuyaminmyat625/AR_project のスマホ設定画面(2026-10-06)を移植。**実際に効く項目だけ**を置く:
//   - 言語(2026-10-14 の英語対応 — 切り替えは iOS の設定アプリのアプリ別の「言語」)
//   - 位置情報の許可(今の状態と設定アプリへの導線。iPhone 全体の位置情報サービスがオフならそれを出す)
// 相手側にあった「通知」(アプリは通知を送らない)・「バックグラウンド位置情報」(OS の許可で決まる)・
// 「距離の単位」(km 以外の表示経路が無い)は、押しても何も変わらないので置かない。
struct PhoneSettingsView: View {
    let onBack: () -> Void

    @Environment(\.openURL) private var openURL
    @Environment(\.scenePhase) private var scenePhase
    @State private var locationStatus = CLLocationManager().authorizationStatus
    /// iPhone 全体の位置情報サービス(オフならアプリの許可に関係なく測位できない)
    @State private var servicesEnabled = true

    /// 今の表示言語の名前(その言語で: 日本語 / English)
    private var languageName: String {
        let locale = Locale.appLanguage
        let code = locale.language.languageCode?.identifier ?? "ja"
        return locale.localizedString(forLanguageCode: code) ?? code
    }

    private var locationStatusText: LocalizedStringKey {
        guard servicesEnabled else { return AppStrings.PhoneSettings.locationServicesOff }
        switch locationStatus {
        case .authorizedAlways: return AppStrings.PhoneSettings.locationAlways
        case .authorizedWhenInUse: return AppStrings.PhoneSettings.locationWhenInUse
        case .denied: return AppStrings.PhoneSettings.locationDenied
        case .restricted: return AppStrings.PhoneSettings.locationRestricted
        case .notDetermined: return AppStrings.PhoneSettings.locationNotDetermined
        @unknown default: return AppStrings.PhoneSettings.locationNotDetermined
        }
    }

    private var locationAllowed: Bool {
        servicesEnabled && (locationStatus == .authorizedAlways || locationStatus == .authorizedWhenInUse)
    }

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                SettingsHeader(title: AppStrings.PhoneSettings.title, subtitle: AppStrings.PhoneSettings.subtitle)

                ScrollView(showsIndicators: false) {
                    VStack(spacing: 12) {
                        SettingsSectionCard(icon: "globe", title: AppStrings.PhoneSettings.language) {
                            Text(languageName)
                                .font(.system(size: 14, weight: .medium))
                                .foregroundColor(.white)
                            Text(AppStrings.PhoneSettings.languageNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                            SettingsActionButton(title: AppStrings.Common.changeInSettings, action: openAppSettings)
                        }

                        SettingsSectionCard(icon: "location.fill", title: AppStrings.PhoneSettings.location) {
                            Text(locationStatusText)
                                .font(.system(size: 14, weight: .medium))
                                .foregroundColor(locationAllowed ? .arYellow : .orange)
                            Text(AppStrings.PhoneSettings.locationNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                            SettingsActionButton(title: AppStrings.Common.changeInSettings, action: openAppSettings)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 52)
                }
            }
        }
        .task { servicesEnabled = await LocationServices.isEnabled() }
        .onChange(of: scenePhase) { _, phase in
            // 設定アプリから戻ってきたら、変えた許可を反映する
            guard phase == .active else { return }
            locationStatus = CLLocationManager().authorizationStatus
            Task { servicesEnabled = await LocationServices.isEnabled() }
        }
    }

    /// iOS の設定アプリのこのアプリのページ(言語・位置情報の許可がここにある)
    private func openAppSettings() {
        guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
        openURL(url)
    }
}

#Preview {
    PhoneSettingsView(onBack: {})
}
