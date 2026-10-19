import SwiftUI

// MARK: - AR設定 (S9-3)
// hsuyaminmyat625/AR_project の AR設定画面(2026-10-06)を移植。**実際に効く項目だけ**を置く:
//   - ARグラスの接続状態(USB-C の外部ディスプレイとして実際に検出しているもの)
//   - アバターの選択(以前はホームのメニューにあった)
// 相手側にあった「明るさ」「自動再接続」は置かない — 明るさは iOS からは変えられず(グラス本体のボタン)、
// 接続は USB-C を挿せば自動で認識されるため、押しても何も起きないスイッチになる。
struct ARSettingsView: View {
    let onBack: () -> Void

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var external = ExternalDisplayManager.shared
    @State private var showAvatarLibrary = false
    /// アバターが目標ペースで走る(既定 OFF = 走者の3m前に付いていく — AvatarPacing)
    @AppStorage(AvatarPacing.storageKey) private var avatarSelfPaced = false

    /// 準備画面と同じ判定(手動の接続はここでは扱わない)
    private var glassStatus: GlassRowStatus {
        .resolve(glassesConnected: external.isGlassesConnected,
                 mirroring: external.isMirroring,
                 manualConnect: false)
    }

    private var currentAvatarName: String {
        bridge.currentVrmAvatar.isEmpty ? AppStrings.Avatar.defaultModel : bridge.currentVrmAvatar
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

                SettingsHeader(title: AppStrings.ARSettings.title, subtitle: AppStrings.ARSettings.subtitle)

                ScrollView(showsIndicators: false) {
                    VStack(spacing: 12) {
                        SettingsSectionCard(icon: "eyeglasses", title: AppStrings.ARSettings.glasses) {
                            HStack(spacing: 6) {
                                Circle()
                                    .fill(glassStatus == .notDetected ? Color.arBorder : glassStatus.color)
                                    .frame(width: 8, height: 8)
                                Text(glassStatus.label ?? AppStrings.ARSettings.notConnected)
                                    .font(.system(size: 14, weight: .medium))
                                    .foregroundColor(glassStatus == .notDetected ? .arGrayText : glassStatus.color)
                            }
                            Text(AppStrings.ARSettings.brightnessNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                                .fixedSize(horizontal: false, vertical: true)
                        }

                        SettingsSectionCard(icon: "person.crop.square", title: AppStrings.ARSettings.avatar) {
                            Text(currentAvatarName)
                                .font(.system(size: 14, weight: .medium))
                                .foregroundColor(.white)
                            SettingsActionButton(title: AppStrings.ARSettings.chooseAvatar) {
                                showAvatarLibrary = true
                            }
                        }

                        // タスク表 ARグラス「指定した速さで走る」— 既定は「走者+3m」
                        SettingsSectionCard(icon: "figure.run", title: AppStrings.ARSettings.pacing) {
                            Toggle(isOn: $avatarSelfPaced) {
                                Text(AppStrings.ARSettings.selfPaced)
                                    .font(.system(size: 14, weight: .medium))
                                    .foregroundColor(.white)
                            }
                            .tint(.arYellow)
                            Text(avatarSelfPaced ? AppStrings.ARSettings.selfPacedOnNote : AppStrings.ARSettings.selfPacedOffNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 52)
                }
            }
        }
        .onAppear {
            external.refreshMirroring()
            bridge.requestVrmAvatars()   // Unity 起動前なら起動後に届く(UnityBridge が溜める)
        }
        .sheet(isPresented: $showAvatarLibrary) { AvatarLibraryView() }
    }
}

// MARK: - 設定画面の共通部品(AR設定・スマホ設定)

struct SettingsHeader: View {
    let title: LocalizedStringKey
    let subtitle: LocalizedStringKey

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ARLabel(text: AppStrings.Common.settingsEyebrow)
            Text(title)
                .font(.system(size: 30, weight: .bold))
                .foregroundColor(.white)
            Text(subtitle)
                .font(.system(size: 14))
                .foregroundColor(.arGrayText)
                .padding(.top, 2)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 28)
        .padding(.bottom, 24)
    }
}

struct SettingsSectionCard<Content: View>: View {
    let icon: String
    let title: LocalizedStringKey
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Image(systemName: icon)
                    .foregroundColor(.arYellow)
                    .frame(width: 22)
                Text(title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(.white)
                Spacer()
            }
            content
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color.arBorder, lineWidth: 1)
        )
    }
}

struct SettingsActionButton: View {
    let title: LocalizedStringKey
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.arYellow)
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .background(Color.arYellow.opacity(0.12))
                .clipShape(Capsule())
                .overlay(Capsule().stroke(Color.arYellow.opacity(0.4), lineWidth: 1))
        }
        .buttonStyle(.plain)
        .padding(.top, 2)
    }
}

#Preview {
    ARSettingsView(onBack: {})
}
