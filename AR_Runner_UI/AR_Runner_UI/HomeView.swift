//
//  HomeView.swift
//  AR_Runner_UI
//
//  Home screen (S3): "前回の記録" header with a hamburger menu (履歴・AR設定・
//  スマホ設定), a top card showing a small route preview + last-run stats
//  (tap → history / S10), and a bottom card with guidance text + a
//  slide-to-start control that leads into device connect (S4).
//
//  Visual style: dark/black background, card-based layout, lime-green
//  (#c7f219) accent — inspired by simple pet-tracker style dashboards.
//

import SwiftUI
import MapKit
import Combine

// MARK: - Last run summary model

/// Snapshot of the most recent run, used to populate the "前回の記録" card.
/// All fields are optional — when a field is `nil` the UI shows "-" per spec.
struct LastRunSummary {
    var dateText: String?
    var distanceKm: Double?
    var paceText: String?
    var durationText: String?
    var routeCoordinates: [CLLocationCoordinate2D]?

    static let empty = LastRunSummary(
        dateText: nil, distanceKm: nil, paceText: nil, durationText: nil, routeCoordinates: nil
    )
}

struct HomeView: View {

    let onNext: () -> Void
    let onBack: () -> Void
    var onOpenHistory: () -> Void = {}
    var onOpenARSettings: () -> Void = {}
    var onOpenPhoneSettings: () -> Void = {}

    /// Injected by the caller once real run history is available.
    /// Defaults to `.empty` so every field renders as "-".
    var lastRun: LastRunSummary = .empty

    @StateObject private var locationManager = HomeLocationManager()

    private let accent = Color(hex: "#c7f219")
    private let cardColor = Color(hex: "#1C1C1E")
    private let secondaryText = Color.white.opacity(0.5)

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
        .onAppear { locationManager.start() }
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

            Menu {
                Button {
                    onOpenHistory()
                } label: {
                    Label("履歴", systemImage: "clock.arrow.circlepath")
                }
                Button {
                    onOpenARSettings()
                } label: {
                    Label("AR設定", systemImage: "eyeglasses")
                }
                Button {
                    onOpenPhoneSettings()
                } label: {
                    Label("スマホ設定", systemImage: "gearshape")
                }
            } label: {
                Image(systemName: "line.3.horizontal")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 40)
                    .background(cardColor, in: Circle())
            }
        }
    }

    // MARK: - Top card: last route preview + stats (tap → history)

    private var lastRunCard: some View {
        Button {
            onOpenHistory()
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

            SlideToStartButton(title: "Get Started", accent: accent, onComplete: onNext)
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

/// A "slide to confirm" style control: drag the accent-colored knob to the
/// right edge of the track to trigger `onComplete`. Snaps back if released
/// before crossing the completion threshold.
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

/// Lightweight CoreLocation wrapper feeding the last-run preview map.
/// Publishes the user's current coordinate and generates a suggested
/// loop route once a fix is available, so the preview always has a
/// visible route even before real run history exists.
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

    /// Builds a smooth loop path around a center point (~800m long),
    /// evoking a suggested running route.
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
    /// Accepts hex strings with or without a leading "#" (e.g. "#c7f219" or "c7f219").
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
    HomeView(onNext: {}, onBack: {})
}
