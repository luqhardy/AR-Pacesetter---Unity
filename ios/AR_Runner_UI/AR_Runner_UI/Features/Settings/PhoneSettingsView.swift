import SwiftUI
import CoreLocation
import UIKit

// MARK: - スマホ設定
// hsuyaminmyat625/AR_project のスマホ設定画面(2026-10-06)を移植。**実際に効く項目だけ**を置く:
//   - 言語(日本語 / English / 端末に合わせる。既定は日本語 — AppLanguage。選ぶとその場で全画面が切り替わる)
//   - 位置情報の許可(今の状態と設定アプリへの導線。iPhone 全体の位置情報サービスがオフならそれを出す)
//   - 身体情報(S9-2)の編集へ(2026-10-21 相手側から移植)
// 相手側にあった「通知」(アプリは通知を送らない)・「バックグラウンド位置情報」(OS の許可で決まる)・
// 「距離の単位」(km 以外の表示経路が無い)は、押しても何も変わらないので置かない。
struct PhoneSettingsView: View {
    let onBack: () -> Void
    var onEditBodyInfo: () -> Void = {}

    @Environment(\.openURL) private var openURL
    @Environment(\.scenePhase) private var scenePhase
    @State private var locationStatus = CLLocationManager().authorizationStatus
    /// iPhone 全体の位置情報サービス(オフならアプリの許可に関係なく測位できない)
    @State private var servicesEnabled = true
    /// 表示言語(App のルートと同じ保存値。変えるとルートが描き直されて全画面が切り替わる)
    @AppStorage(AppLanguage.storageKey) private var appLanguage = AppLanguage.defaultValue.rawValue

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
                            // 言語名はその言語のまま書く(英語の画面でも「日本語」と読めるように)
                            Picker(AppStrings.PhoneSettings.language, selection: $appLanguage) {
                                Text(verbatim: "日本語").tag(AppLanguage.japanese.rawValue)
                                Text(verbatim: "English").tag(AppLanguage.english.rawValue)
                                Text(AppStrings.PhoneSettings.languageSystem).tag(AppLanguage.system.rawValue)
                            }
                            .pickerStyle(.segmented)
                            .colorScheme(.dark)
                            Text(AppStrings.PhoneSettings.languageNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                                .fixedSize(horizontal: false, vertical: true)
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

                        linkRow(icon: "figure.stand", title: AppStrings.PhoneSettings.bodyInfo, action: onEditBodyInfo)
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

    /// 別の画面へ進む行(相手側の linkRow)
    private func linkRow(icon: String, title: LocalizedStringKey, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack {
                Image(systemName: icon)
                    .foregroundColor(.arYellow)
                    .frame(width: 22)
                Text(title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(.white)
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(.arGrayText)
            }
            .padding(16)
            .background(Color.arCard)
            .clipShape(RoundedRectangle(cornerRadius: 16))
            .overlay(
                RoundedRectangle(cornerRadius: 16)
                    .stroke(Color.arBorder, lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
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
