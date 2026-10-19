import SwiftUI
import UniformTypeIdentifiers

// MARK: - アバターライブラリ
//
// 伴走アバターを .vrm で差し替える画面。
//
// **VRChatのアバターはそのままでは使えない**(`.vrca` は暗号化されVRChatから取り出せず、
// iOSは実行時のコード読み込みを許さないためVRC SDKのコンポーネントやシェーダが失われる)。
// 使えるのはVRM — ユーザーへの案内は「VRChat連携」ではなく「VRMで書き出して入れる」。
// 背景と制約は Docs/VRM_AVATARS.md。
//
// 取り込みの流れ:
//   ファイルアプリで .vrm を選ぶ → セキュリティスコープを開いてサンドボックスへコピー
//   → 絶対パスをUnityへ渡す → Unityが計測・判定して差し替え → 結果(理由つき)が返る

struct AvatarLibraryView: View {

    @ObservedObject private var bridge = UnityBridge.shared
    @Environment(\.dismiss) private var dismiss

    @State private var showImporter = false
    @State private var importError: String?

    private let accent = Color.arYellow
    private let cardColor = Color.arCard
    private let secondaryText = Color.arGrayText

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    intro
                    importButton
                    deliveryCard
                    resultCard
                    avatarList
                }
                .padding(.horizontal, 20)
                .padding(.vertical, 12)
            }
            .background(Color.black.ignoresSafeArea())
            .navigationTitle(AppStrings.Avatar.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button(AppStrings.Common.close) { dismiss() }
                }
            }
        }
        .preferredColorScheme(.dark)
        .onAppear { bridge.requestVrmAvatars() }
        .fileImporter(
            isPresented: $showImporter,
            allowedContentTypes: Self.vrmContentTypes,
            allowsMultipleSelection: false
        ) { result in
            handleImport(result)
        }
    }

    // MARK: - 説明

    private var intro: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(AppStrings.Avatar.intro)
                .font(.system(size: 14, weight: .medium))
                .foregroundStyle(.white)
            Text(AppStrings.Avatar.vrchatNote)
                .font(.system(size: 12))
                .foregroundStyle(secondaryText)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
    }

    private var importButton: some View {
        Button {
            importError = nil
            showImporter = true
        } label: {
            HStack(spacing: 8) {
                Image(systemName: "square.and.arrow.down")
                Text(AppStrings.Avatar.importVrm).font(.system(size: 15, weight: .semibold))
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 13)
            .background(accent, in: RoundedRectangle(cornerRadius: 12))
            .foregroundStyle(.black)
        }
    }

    // MARK: - グラスへの反映(タスク表 S9 異常系「送信失敗時のトーストと再送信」)

    @ViewBuilder
    private var deliveryCard: some View {
        switch bridge.avatarDelivery {
        case .idle:
            EmptyView()
        case .sending:
            HStack(spacing: 10) {
                ProgressView().tint(.white)
                Text(AppStrings.Avatar.sending)
                    .font(.system(size: 13))
                    .foregroundStyle(secondaryText)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
        case .queued:
            noticeCard(title: AppStrings.Avatar.queuedTitle, body: "", tint: secondaryText)
        case .failed:
            VStack(alignment: .leading, spacing: 8) {
                Text(AppStrings.Avatar.deliveryFailed)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.orange)
                Button { bridge.retryAvatarSelection() } label: {
                    Text(AppStrings.Avatar.resend)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(accent)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        .background(accent.opacity(0.12), in: Capsule())
                        .overlay(Capsule().stroke(accent.opacity(0.4), lineWidth: 1))
                }
                .buttonStyle(.plain)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.orange.opacity(0.45), lineWidth: 1))
        }
    }

    // MARK: - 直近の結果(断った理由を必ず出す)

    @ViewBuilder
    private var resultCard: some View {
        if let error = importError {
            noticeCard(title: AppStrings.Avatar.importFailedTitle, body: error, tint: .red)
        } else if let r = bridge.lastVrmResult {
            if r.accepted {
                noticeCard(title: AppStrings.Avatar.applied(r.name), body: r.report, tint: accent)
            } else {
                noticeCard(title: AppStrings.Avatar.unusable(r.name.isEmpty ? AppStrings.Avatar.thisModel : r.name),
                           body: r.reason.isEmpty ? r.report : r.reason + "\n" + r.report,
                           tint: .red)
            }
        }
    }

    private func noticeCard(title: LocalizedStringKey, body: String, tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(tint)
            if !body.isEmpty {
                Text(body)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(tint.opacity(0.35), lineWidth: 1))
    }

    // MARK: - 一覧

    @ViewBuilder
    private var avatarList: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(AppStrings.Avatar.available)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(secondaryText)
                .textCase(.uppercase)

            if bridge.vrmAvatars.isEmpty {
                Text(AppStrings.Avatar.none)
                    .font(.system(size: 13))
                    .foregroundStyle(secondaryText)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            } else {
                VStack(spacing: 0) {
                    ForEach(bridge.vrmAvatars) { avatar in
                        // 応答を待っている間は押せない(連打で二重に送らない)
                        Button { bridge.selectVrmAvatar(path: avatar.path) } label: {
                            HStack(spacing: 12) {
                                Image(systemName: "person.crop.square")
                                    .foregroundStyle(accent)
                                Text(avatar.name)
                                    .font(.system(size: 14, weight: .medium))
                                    .foregroundStyle(.white)
                                Spacer()
                                if avatar.name == bridge.currentVrmAvatar {
                                    Text(AppStrings.Avatar.inUse)
                                        .font(.system(size: 11, weight: .semibold))
                                        .foregroundStyle(accent)
                                }
                            }
                            .padding(.horizontal, 14)
                            .padding(.vertical, 12)
                        }
                        .disabled(bridge.avatarDelivery == .sending)
                        if avatar.id != bridge.vrmAvatars.last?.id {
                            Divider().background(Color.white.opacity(0.08)).padding(.leading, 44)
                        }
                    }
                }
                .background(cardColor, in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    // MARK: - 取り込み

    /// `.vrm` にシステムのUTIは無い。拡張子で宣言した型があればそれを、
    /// 無ければ任意のデータとして受ける(ピッカーで選べないほうが困るため広く取る)。
    private static var vrmContentTypes: [UTType] {
        if let vrm = UTType(filenameExtension: "vrm") { return [vrm] }
        return [.data]
    }

    private func handleImport(_ result: Result<[URL], Error>) {
        switch result {
        case .failure(let error):
            importError = error.localizedDescription

        case .success(let urls):
            guard let picked = urls.first else { return }
            do {
                let copied = try copyIntoSandbox(picked)
                // Unityへは**サンドボックス内の絶対パス**を渡す。ピッカーが返すURLは
                // セキュリティスコープ付きで、この関数を抜けると読めなくなる
                bridge.importVrmAvatar(path: copied.path)
            } catch {
                importError = AppStrings.Avatar.fileImportFailed(error.localizedDescription)
            }
        }
    }

    /// 選ばれたファイルをアプリのサンドボックス(`Documents/Avatars/`)へコピーする。
    /// Unityの `persistentDataPath` が同じ `Documents/` を指すため、一覧にも現れる。
    private func copyIntoSandbox(_ url: URL) throws -> URL {
        let fm = FileManager.default
        let documents = try fm.url(for: .documentDirectory, in: .userDomainMask,
                                   appropriateFor: nil, create: true)
        let folder = documents.appendingPathComponent("Avatars", isDirectory: true)
        try fm.createDirectory(at: folder, withIntermediateDirectories: true)

        let destination = folder.appendingPathComponent(url.lastPathComponent)

        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }

        if fm.fileExists(atPath: destination.path) {
            try fm.removeItem(at: destination)
        }
        try fm.copyItem(at: url, to: destination)
        return destination
    }
}

#Preview {
    AvatarLibraryView()
}
