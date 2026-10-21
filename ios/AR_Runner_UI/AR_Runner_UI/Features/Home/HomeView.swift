//
//  HomeView.swift
//  AR_Runner_UI
//
//  ホーム画面 (S3): 「前回の記録」ヘッダー + ハンバーガーメニュー(履歴・AR設定・スマホ設定・
//  使い方・安全上の注意)、前回の走行の地図プレビュー + 統計カード(タップ → 履歴)、
//  案内文 + スライド開始でデバイス接続(S4)へ進む。
//
//  デザインは hsuyaminmyat625/AR_project の HomeView(2026-09-11)を採用。
//  黒背景・カード構成・ライムグリーン(#c7f219)のアクセント。
//  本リポジトリへの合体で変えた点:
//    - 「前回の記録」は Unity のセッションストアから届く実履歴(HistoryData)で埋める
//    - メニューに「使い方」「安全上の注意」を追加。「AR設定」「スマホ設定」は 2026-10-14 に
//      相手側の画面を実際に効く項目だけで移植(アバターの選択は AR設定 へ移した)
//    - 走る前に一番知りたい ARグラスの接続状態を開始カードに1行だけ載せた
//

import SwiftUI
import MapKit
import Combine   // ObservableObject / @Published (Xcode 26 は SwiftUI 経由の暗黙再エクスポートをしない)

// MARK: - Last run summary model

/// 直近の走行のスナップショット(「前回の記録」カード用)。
/// 各項目は任意 — nil の項目は "-" と表示する。
struct LastRunSummary {
    var dateText: String?
    var distanceKm: Double?
    var paceText: String?
    var durationText: String?
    var routeCoordinates: [CLLocationCoordinate2D]?

    static let empty = LastRunSummary(
        dateText: nil, distanceKm: nil, paceText: nil, durationText: nil, routeCoordinates: nil
    )

    /// Unity から届いた履歴(新しい順)の先頭から作る。履歴が無ければ `.empty`。
    /// 経路は 2026-10-13 から記録している。それより前の走行は経路なし(地図は現在地だけ)
    static func from(history: [UnityBridge.HistoryEntry]) -> LastRunSummary {
        guard let latest = history.first else { return .empty }

        return LastRunSummary(
            dateText: latest.dateLabel(locale: .appLanguage),
            distanceKm: latest.distanceKm,
            paceText: latest.paceLabel,
            durationText: latest.timeLabel,
            routeCoordinates: latest.hasRoute
                ? latest.route.map { CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude) }
                : nil
        )
    }
}

struct HomeView: View {

    let onStartRun: () -> Void
    let onHistory: () -> Void
    let onARSettings: () -> Void
    let onPhoneSettings: () -> Void
    let onTutorial: () -> Void
    let onDisclaimer: () -> Void
    /// 展示ブースの体験モード(Kobe Calling)。メニューの開発者モードの隣に置く
    var onBoothDemo: () -> Void = {}

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var external = ExternalDisplayManager.shared
    @StateObject private var locationManager = HomeLocationManager()
    @State private var showDevMode = false

    private let accent = Color(hex: "#c7f219")
    private let cardColor = Color(hex: "#1C1C1E")
    private let secondaryText = Color.white.opacity(0.5)

    @ObservedObject private var session = ARSessionManager.shared

    /// 前回の走行が記録の途中で終わった(クラッシュ・電池切れ)か。そのときの履歴の先頭は
    /// 1つ前の走行なので出さない(タスク表 S3 異常系「不完全なデータとして『-』と表示」)
    private var lastRunInterrupted: Bool {
        RunInterruption.wasInterrupted(in: .standard, sessionActive: session.isSessionActive)
    }

    private var lastRun: LastRunSummary {
        lastRunInterrupted ? .empty : LastRunSummary.from(history: bridge.history)
    }

    var body: some View {
        // ヘッダー(メニュー)はスクロールせず上に固定する(hsuyaminmyat625/AR_project 2026-10-21)
        VStack(spacing: 16) {
            header
                .padding(.horizontal, 20)
                .padding(.top, 8)
                .fixedSize(horizontal: false, vertical: true)

            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 16) {
                    lastRunCard
                    if let problem = locationManager.locationProblem {
                        LocationPermissionMessage(problem: problem)
                            .frame(maxWidth: .infinity)
                    }
                    bottomStartCard
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 20)
            }
            .scrollBounceBehavior(.basedOnSize)
        }
        .background(Color.black.ignoresSafeArea())
        .onAppear {
            locationManager.start()
            // Unity が起動済みなら実履歴を取りに行く(初回起動は未起動のため "-" のまま)
            bridge.requestHistory()
        }
        .preferredColorScheme(.dark)
        .sheet(isPresented: $showDevMode) { DevModeView() }
    }

    // MARK: - Header

    private var header: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 4) {
                    Text(AppStrings.Home.lastRun)
                        .font(.system(size: 24, weight: .bold, design: .rounded))
                }
                .foregroundStyle(.white)

                Text(AppStrings.Home.latestStatus)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(secondaryText)
                    .textCase(.uppercase)
            }

            Spacer()

            // ハンバーガーメニュー(課題 #7)
            Menu {
                Button { onHistory() } label: {
                    Label(AppStrings.Home.history, systemImage: "clock.arrow.circlepath")
                }
                Button { onARSettings() } label: {
                    Label(AppStrings.Home.arSettings, systemImage: "eyeglasses")
                }
                Button { onPhoneSettings() } label: {
                    Label(AppStrings.Home.phoneSettings, systemImage: "iphone")
                }
                Button { onTutorial() } label: {
                    Label(AppStrings.Home.tutorial, systemImage: "book")
                }
                Button { onDisclaimer() } label: {
                    Label(AppStrings.Home.safety, systemImage: "exclamationmark.shield")
                }

                // 開発者モード: 走行ログCSVの取り出しと実機の状態確認。
                // 第1期の成果物(CSV)はサンドボックス内にあり、これが唯一の取り出し口
                Divider()
                Button { onBoothDemo() } label: {
                    Label(AppStrings.Home.boothDemo, systemImage: "person.wave.2")
                }
                Button { showDevMode = true } label: {
                    Label(AppStrings.Home.devMode, systemImage: "wrench.and.screwdriver")
                }
            } label: {
                Image(systemName: "line.3.horizontal")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(cardColor, in: Circle())
            }
        }
        .padding(.top, 48)
    }

    // MARK: - Top card: last route preview + stats (tap → history)

    private var lastRunCard: some View {
        Button {
            onHistory()
        } label: {
            VStack(spacing: 0) {
                // 前回の走行の経路だけを引く。経路が無ければ線は無し —
                // 以前は現在地の周りに作った「提案ループ」を引いており、前回の記録に見えていた
                Map(position: .constant(.automatic)) {
                    UserAnnotation()
                    if let route = lastRun.routeCoordinates {
                        MapPolyline(coordinates: route)
                            .stroke(accent, style: StrokeStyle(lineWidth: 4, lineCap: .round, lineJoin: .round))
                    }
                }
                .mapStyle(.standard(elevation: .flat, pointsOfInterest: .excludingAll))
                .mapControls { }
                .colorScheme(.dark)
                .allowsHitTesting(false)
                .frame(height: 260)
                .clipShape(RoundedRectangle(cornerRadius: 24, style: .continuous))

                statsRow
                    .padding(16)

                if lastRunInterrupted {
                    Text(AppStrings.Home.interrupted)
                        .font(.system(size: 12))
                        .foregroundStyle(secondaryText)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(.horizontal, 16)
                        .padding(.bottom, 14)
                }
            }
        }
        .buttonStyle(.plain)
        .background(cardColor)
        .clipShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 28, style: .continuous)
                .stroke(.white.opacity(0.22), lineWidth: 1)
        )
    }

    private var statsRow: some View {
        VStack(spacing: 10) {
            HStack(spacing: 10) {
                statBox(label: AppStrings.Home.date, value: lastRun.dateText ?? "-")
                statBox(label: AppStrings.Home.time, value: lastRun.durationText ?? "-")
            }
            HStack(spacing: 10) {
                statBox(
                    label: AppStrings.Home.distance,
                    value: lastRun.distanceKm.map { String(format: "%.2f", $0) } ?? "-",
                    unit: lastRun.distanceKm != nil ? "km" : nil
                )
                statBox(
                    label: AppStrings.Home.speed,
                    value: lastRun.paceText ?? "-",
                    unit: lastRun.paceText != nil ? "/km" : nil
                )
            }
        }
    }

    private func statBox(label: LocalizedStringKey, value: String, unit: String? = nil) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label)
                .font(.system(size: 14, weight: .medium))
                .foregroundStyle(secondaryText)
            HStack(alignment: .firstTextBaseline, spacing: 4) {
                Text(value)
                    .font(.system(size: 23, weight: .bold, design: .rounded))
                    .foregroundStyle(.white)
                if let unit {
                    Text(unit)
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(secondaryText)
                }
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.white.opacity(0.05))
        .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .stroke(.white.opacity(0.15), lineWidth: 1)
        )
    }

    // MARK: - Bottom card: guidance + slide to start

    private var bottomStartCard: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(AppStrings.Home.runTogether)
                .font(.system(size: 20, weight: .medium, design: .rounded))
                .foregroundStyle(.white.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true)

            // 走る前に一番知りたい情報。実グラス(USB-C外部ディスプレイ)の接続状態をそのまま映す
            HStack(spacing: 8) {
                Image(systemName: "eyeglasses")
                    .font(.system(size: 13))
                Text(external.isGlassesConnected
                     ? AppStrings.Home.glassesConnected
                     : AppStrings.Home.glassesNotConnected)
                    .font(.system(size: 12, weight: .medium))
            }
            .foregroundStyle(external.isGlassesConnected ? accent : secondaryText)
            .animation(.easeInOut(duration: 0.2), value: external.isGlassesConnected)

            SlideToStartButton(title: AppStrings.Home.getStarted, accent: accent, onComplete: onStartRun)
        }
        .padding(22)
        .background(cardColor)
        .clipShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 28, style: .continuous)
                .stroke(.white.opacity(0.22), lineWidth: 1)
        )
    }
}

// MARK: - Slide to start button

/// 「スライドで確定」式のコントロール。アクセント色のノブをトラックの右端まで
/// ドラッグすると `onComplete` を呼ぶ。しきい値の手前で離すと元に戻る
private struct SlideToStartButton: View {
    let title: LocalizedStringKey
    let accent: Color
    let onComplete: () -> Void

    private let knobSize: CGFloat = 48
    private let trackHeight: CGFloat = 60
    private let horizontalInset: CGFloat = 6

    @State private var dragOffset: CGFloat = 0
    @State private var isCompleted = false
    @GestureState private var isDragging = false

    var body: some View {
        GeometryReader { proxy in
            let trackWidth = proxy.size.width
            let maxOffset: CGFloat = max(trackWidth - knobSize - horizontalInset * 2, 0)
            let progress: CGFloat = maxOffset > 0 ? min(max(dragOffset / maxOffset, 0), 1) : 0

            ZStack(alignment: .leading) {
                Capsule()
                    .fill(.white.opacity(0.08))
                    .overlay(Capsule().stroke(.white.opacity(0.12), lineWidth: 0.5))

                Capsule()
                    .fill(accent.opacity(0.22))
                    .frame(width: max(dragOffset + knobSize + horizontalInset, knobSize))
                    .padding(horizontalInset)

                let labelFont: Font = .system(size: 15, weight: .semibold, design: .rounded)
                let styledLabel: Text = Text(title)
                    .font(labelFont)
                    .foregroundStyle(.white)
                styledLabel
                    .frame(maxWidth: .infinity)
                    .opacity(Double(1 - progress * 1.3))

                ZStack {
                    Circle()
                        .fill(accent)
                        .shadow(color: accent.opacity(0.45), radius: 10, y: 3)
                    Image(systemName: "arrow.right")
                        .font(.system(size: 15, weight: .bold))
                        .foregroundStyle(.black)
                }
                .frame(width: knobSize, height: knobSize)
                .offset(x: horizontalInset + dragOffset)
                .gesture(
                    DragGesture()
                        .updating($isDragging) { _, state, _ in state = true }
                        .onChanged { value in
                            guard !isCompleted else { return }
                            dragOffset = min(max(value.translation.width, 0), maxOffset)
                        }
                        .onEnded { value in
                            guard !isCompleted else { return }
                            if dragOffset >= maxOffset * 0.85 {
                                isCompleted = true
                                withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                                    dragOffset = maxOffset
                                }
                                onComplete()
                            } else {
                                withAnimation(.spring(response: 0.35, dampingFraction: 0.7)) {
                                    dragOffset = 0
                                }
                            }
                        }
                )
                .animation(.spring(response: 0.35, dampingFraction: 0.7), value: isDragging)
            }
        }
        .frame(height: trackHeight)
    }
}

// MARK: - Location manager for map preview + route trail

/// 前回の記録の地図プレビュー用の軽量な CoreLocation ラッパー(現在地と許可状態)。
/// 走行中の測位は LocationTracker が担当し、こちらはホーム表示専用
private final class HomeLocationManager: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var userCoordinate: CLLocationCoordinate2D?
    @Published private(set) var authorizationStatus: CLAuthorizationStatus

    private let manager = CLLocationManager()

    override init() {
        authorizationStatus = manager.authorizationStatus
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBest
    }

    /// iPhone 全体の位置情報サービス(裏で調べる — LocationServices)
    @Published private(set) var servicesEnabled = true

    /// 位置情報が使えない理由(拒否・制限・サービスオフ)。問題が無ければ nil
    var locationProblem: LocationProblem? {
        .resolve(servicesEnabled: servicesEnabled, authorization: LocationAuthorization(authorizationStatus))
    }

    private func refreshServicesEnabled() {
        Task { servicesEnabled = await LocationServices.isEnabled() }
    }

    func start() {
        authorizationStatus = manager.authorizationStatus
        refreshServicesEnabled()
        switch manager.authorizationStatus {
        case .notDetermined:
            manager.requestWhenInUseAuthorization()
        case .authorizedAlways, .authorizedWhenInUse:
            manager.startUpdatingLocation()
        default:
            break
        }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        // 位置情報サービスのオン・オフを切り替えたときもここに来る
        authorizationStatus = manager.authorizationStatus
        refreshServicesEnabled()
        if manager.authorizationStatus == .authorizedWhenInUse
            || manager.authorizationStatus == .authorizedAlways {
            manager.startUpdatingLocation()
        }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let latest = locations.last, latest.horizontalAccuracy <= 50 else { return }
        userCoordinate = latest.coordinate
    }
}

// MARK: - Hex color helper

private extension Color {
    /// "#c7f219" / "c7f219" のどちらも受け付ける
    init(hex: String) {
        var hexString = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        if hexString.hasPrefix("#") {
            hexString.removeFirst()
        }
        let scanner = Scanner(string: hexString)
        var rgb: UInt64 = 0
        scanner.scanHexInt64(&rgb)
        let r = Double((rgb >> 16) & 0xFF) / 255
        let g = Double((rgb >> 8) & 0xFF) / 255
        let b = Double(rgb & 0xFF) / 255
        self.init(red: r, green: g, blue: b)
    }
}

#Preview {
    HomeView(onStartRun: {}, onHistory: {}, onARSettings: {}, onPhoneSettings: {}, onTutorial: {}, onDisclaimer: {})
}
