//
//  HomeView.swift
//  AR_Runner_UI
//
//  ホーム画面 (S3): 「前回の記録」ヘッダー + ハンバーガーメニュー(履歴・AR設定・
//  使い方・安全上の注意)、前回の走行の地図プレビュー + 統計カード(タップ → 履歴)、
//  案内文 + スライド開始でデバイス接続(S4)へ進む。
//
//  デザインは hsuyaminmyat625/AR_project の HomeView(2026-09-11)を採用。
//  黒背景・カード構成・ライムグリーン(#c7f219)のアクセント。
//  本リポジトリへの合体で変えた点:
//    - 「前回の記録」は Unity のセッションストアから届く実履歴(HistoryData)で埋める
//    - メニューの「スマホ設定」(該当画面なし)を「使い方」「安全上の注意」に置き換え
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
    /// 経路座標は現状記録していないので nil(地図には提案ループが出る)
    static func from(history: [UnityBridge.HistoryEntry]) -> LastRunSummary {
        guard let latest = history.first else { return .empty }

        var pace: String? = nil
        if latest.distanceKm > 0.01 {
            let minPerKm = (latest.elapsedSeconds / 60.0) / latest.distanceKm
            let minutes = Int(minPerKm)
            let seconds = Int((minPerKm - Double(minutes)) * 60.0)
            pace = String(format: "%d'%02d\"", minutes, seconds)
        }

        return LastRunSummary(
            dateText: latest.dateLabel,
            distanceKm: latest.distanceKm,
            paceText: pace,
            durationText: latest.timeLabel,
            routeCoordinates: nil
        )
    }
}

struct HomeView: View {

    let onStartRun: () -> Void
    let onHistory: () -> Void
    let onDevices: () -> Void
    let onTutorial: () -> Void
    let onDisclaimer: () -> Void

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var external = ExternalDisplayManager.shared
    @StateObject private var locationManager = HomeLocationManager()

    private let accent = Color(hex: "#c7f219")
    private let cardColor = Color(hex: "#1C1C1E")
    private let secondaryText = Color.white.opacity(0.5)

    private var lastRun: LastRunSummary { LastRunSummary.from(history: bridge.history) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                header
                lastRunCard
                bottomStartCard
            }
            .padding(.horizontal, 20)
            .padding(.top, 8)
            .padding(.bottom, 20)
        }
        .background(Color.black.ignoresSafeArea())
        .onAppear {
            locationManager.start()
            // Unity が起動済みなら実履歴を取りに行く(初回起動は未起動のため "-" のまま)
            bridge.requestHistory()
        }
        .preferredColorScheme(.dark)
    }

    // MARK: - Header

    private var header: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 4) {
                    Image(systemName: "chevron.down")
                        .font(.system(size: 12, weight: .bold))
                    Text("前回の記録")
                        .font(.system(size: 24, weight: .bold, design: .rounded))
                }
                .foregroundStyle(.white)

                Text("最新のランニング状況")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(secondaryText)
                    .textCase(.uppercase)
            }

            Spacer()

            // ハンバーガーメニュー(課題 #7)
            Menu {
                Button { onHistory() } label: {
                    Label("履歴", systemImage: "clock.arrow.circlepath")
                }
                Button { onDevices() } label: {
                    Label("AR設定", systemImage: "eyeglasses")
                }
                Button { onTutorial() } label: {
                    Label("使い方", systemImage: "book")
                }
                Button { onDisclaimer() } label: {
                    Label("安全上の注意", systemImage: "exclamationmark.shield")
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
                Map(position: .constant(.automatic)) {
                    UserAnnotation()
                    if let route = lastRun.routeCoordinates ?? locationManager.suggestedRoute {
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
                statBox(label: "日付", value: lastRun.dateText ?? "-")
                statBox(label: "時間", value: lastRun.durationText ?? "-")
            }
            HStack(spacing: 10) {
                statBox(
                    label: "距離",
                    value: lastRun.distanceKm.map { String(format: "%.2f", $0) } ?? "-",
                    unit: lastRun.distanceKm != nil ? "km" : nil
                )
                statBox(
                    label: "速さ",
                    value: lastRun.paceText ?? "-",
                    unit: lastRun.paceText != nil ? "/km" : nil
                )
            }
        }
    }

    private func statBox(label: String, value: String, unit: String? = nil) -> some View {
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
            Text("Let's run together!")
                .font(.system(size: 20, weight: .medium, design: .rounded))
                .foregroundStyle(.white.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true)

            // 走る前に一番知りたい情報。実グラス(USB-C外部ディスプレイ)の接続状態をそのまま映す
            HStack(spacing: 8) {
                Image(systemName: "eyeglasses")
                    .font(.system(size: 13))
                Text(external.isGlassesConnected
                     ? "ARグラス接続済み — 映像はグラスへ出力されます"
                     : "ARグラス未接続 — 端末画面のままでも走れます")
                    .font(.system(size: 12, weight: .medium))
            }
            .foregroundStyle(external.isGlassesConnected ? accent : secondaryText)
            .animation(.easeInOut(duration: 0.2), value: external.isGlassesConnected)

            SlideToStartButton(title: "Get Started", accent: accent, onComplete: onStartRun)
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
    let title: String
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

/// 前回の記録の地図プレビュー用の軽量な CoreLocation ラッパー。
/// 現在地を publish し、測位が取れたら提案ループ経路を1本作る
/// (実走行の経路を記録するまでの間も地図に線が出るように)。
/// 走行中の測位は LocationTracker が担当し、こちらはホーム表示専用
private final class HomeLocationManager: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var userCoordinate: CLLocationCoordinate2D?
    @Published var suggestedRoute: [CLLocationCoordinate2D]?

    private let manager = CLLocationManager()

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBest
    }

    func start() {
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
        if manager.authorizationStatus == .authorizedWhenInUse
            || manager.authorizationStatus == .authorizedAlways {
            manager.startUpdatingLocation()
        }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let latest = locations.last, latest.horizontalAccuracy <= 50 else { return }
        userCoordinate = latest.coordinate
        if suggestedRoute == nil {
            suggestedRoute = Self.loopRoute(around: latest.coordinate)
        }
    }

    /// 中心点の周りに滑らかなループ経路(約800m)を作る。「提案経路」の見た目のため
    private static func loopRoute(around center: CLLocationCoordinate2D, radiusMeters: Double = 130) -> [CLLocationCoordinate2D] {
        let metersPerDegreeLat = 111_320.0
        let metersPerDegreeLon = 111_320.0 * cos(center.latitude * .pi / 180)

        let pointCount = 48
        var coordinates: [CLLocationCoordinate2D] = []

        for i in 0...pointCount {
            let theta = (Double(i) / Double(pointCount)) * 2 * .pi
            let r = radiusMeters * (1 + 0.55 * cos(theta))
            let dx = r * sin(theta)
            let dy = r * (1 - cos(theta))

            coordinates.append(
                CLLocationCoordinate2D(
                    latitude: center.latitude + dy / metersPerDegreeLat,
                    longitude: center.longitude + dx / metersPerDegreeLon
                )
            )
        }
        return coordinates
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
    HomeView(onStartRun: {}, onHistory: {}, onDevices: {}, onTutorial: {}, onDisclaimer: {})
}
