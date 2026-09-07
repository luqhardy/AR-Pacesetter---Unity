//
//  HomeView.swift
//  AR_Runner_UI
//
//  Home screen: real dark MapKit map background, hamburger menu icon
//  (no action yet), and a Get Started button that advances the app's
//  navigation via onNext(), matching the AppScreen switch in ContentView.
//

import SwiftUI
import MapKit
import Combine

struct HomeView: View {

    let onNext: () -> Void
    let onBack: () -> Void

    @StateObject private var locationManager = HomeLocationManager()
    @State private var cameraPosition: MapCameraPosition = .automatic

    var body: some View {
        ZStack(alignment: .top) {

            // MARK: Background — real map, dark style
            Map(position: $cameraPosition) {
                UserAnnotation()

                if let suggestedRoute = locationManager.suggestedRoute {
                    MapPolyline(coordinates: suggestedRoute)
                        .stroke(Color(hex: "#c7f219"), style: StrokeStyle(lineWidth: 5, lineCap: .round, lineJoin: .round))
                }

                if locationManager.routeCoordinates.count > 1 {
                    MapPolyline(coordinates: locationManager.routeCoordinates)
                        .stroke(Color(hex: "#c7f219"), lineWidth: 5)
                }
            }
            .mapStyle(.standard(elevation: .flat, pointsOfInterest: .excludingAll))
            .mapControls { } // hide default compass/scale controls
            .colorScheme(.dark)
            .ignoresSafeArea()
            .onAppear { locationManager.start() }
            .onReceive(locationManager.$userCoordinate) { coordinate in
                guard let coordinate else { return }
                cameraPosition = .region(
                    MKCoordinateRegion(
                        center: coordinate,
                        span: MKCoordinateSpan(latitudeDelta: 0.005, longitudeDelta: 0.005)
                    )
                )
            }

            // subtle dark scrim so the card + top controls stay legible
            LinearGradient(
                colors: [.black.opacity(0.25), .clear, .clear, .black.opacity(0.6)],
                startPoint: .top,
                endPoint: .bottom
            )
            .allowsHitTesting(false)
            .ignoresSafeArea()

            // MARK: Top bar — hamburger menu (no action)
            VStack {
                HStack {
                    Spacer()
                    Button {
                        // no action yet
                    } label: {
                        Image(systemName: "line.3.horizontal")
                            .font(.system(size: 17, weight: .semibold))
                            .foregroundStyle(.white)
                            .frame(width: 44, height: 44)
                            .background(.ultraThinMaterial, in: Circle())
                            .overlay(Circle().stroke(.white.opacity(0.15), lineWidth: 0.5))
                            .shadow(color: .black.opacity(0.25), radius: 10, y: 4)
                    }
                    .buttonStyle(PressableButtonStyle())
                }
                .padding(.horizontal, 20)
                .padding(.top, 8)
                Spacer()
            }

            // MARK: Bottom content card
            VStack {
                Spacer()
                bottomCard
            }
        }
        .preferredColorScheme(.dark)
    }

    // MARK: - Bottom Card

    private var bottomCard: some View {
        VStack(alignment: .leading, spacing: 20) {
            let headlineFont: Font = .system(size: 32, weight: .bold, design: .rounded)
            let headline: Text = (
                Text("Let's run together!")
                    .foregroundStyle(.white)
            )
            .font(headlineFont)
            headline
                .lineSpacing(2)
                .fixedSize(horizontal: false, vertical: true)

            SlideToStartButton(title: "Get started", onComplete: onNext)
        }
        .padding(24)
        .background(
            RoundedRectangle(cornerRadius: 32, style: .continuous)
                .fill(.ultraThinMaterial)
                .environment(\.colorScheme, .dark)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 32, style: .continuous)
                .stroke(.white.opacity(0.12), lineWidth: 0.5)
        )
        .shadow(color: .black.opacity(0.35), radius: 24, y: 12)
        .padding(.horizontal, 14)
        .padding(.bottom, 14)
    }
}

// MARK: - Slide to start button

/// A "slide to confirm" style control: drag the green knob to the right
/// edge of the track to trigger `onComplete`. Snaps back if released
/// before crossing the completion threshold.
private struct SlideToStartButton: View {
    let title: String
    let onComplete: () -> Void

    private let knobSize: CGFloat = 46
    private let trackHeight: CGFloat = 58
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
                // Track background
                Capsule()
                    .fill(.white.opacity(0.1))
                    .overlay(Capsule().stroke(.white.opacity(0.14), lineWidth: 0.5))

                // Fill that grows as the knob is dragged, giving progress feedback
                Capsule()
                    .fill(Color(hex: "#c7f219").opacity(0.25))
                    .frame(width: max(dragOffset + knobSize + horizontalInset, knobSize))
                    .padding(horizontalInset)

                // Label — fades out as the knob approaches the end
                let labelFont: Font = .system(size: 16, weight: .semibold, design: .rounded)
                let styledLabel: Text = Text(title)
                    .font(labelFont)
                    .foregroundStyle(.white)
                styledLabel
                    .frame(maxWidth: .infinity)
                    .opacity(Double(1 - progress * 1.3))

                // Draggable knob
                ZStack {
                    Circle()
                        .fill(Color(hex: "#c7f219"))
                        .shadow(color: Color(hex: "#c7f219").opacity(0.45), radius: 10, y: 3)
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

private struct PressableButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .opacity(configuration.isPressed ? 0.9 : 1)
            .animation(.spring(response: 0.3, dampingFraction: 0.6), value: configuration.isPressed)
    }
}

// MARK: - Location manager for live map + route trail

/// Lightweight CoreLocation wrapper for the Home screen's map.
/// Publishes the user's current coordinate and a short trailing
/// breadcrumb of recent points, drawn as the AR-route polyline.
private final class HomeLocationManager: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var userCoordinate: CLLocationCoordinate2D?
    @Published var routeCoordinates: [CLLocationCoordinate2D] = []
    @Published var suggestedRoute: [CLLocationCoordinate2D]?

    private let manager = CLLocationManager()
    private let maxTrailPoints = 200

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
        routeCoordinates.append(latest.coordinate)
        if routeCoordinates.count > maxTrailPoints {
            routeCoordinates.removeFirst(routeCoordinates.count - maxTrailPoints)
        }

        // Generate a suggested loop route once, centered on the first fix,
        // so there's always a visible AR route on the map even before the
        // user starts moving.
        if suggestedRoute == nil {
            suggestedRoute = Self.loopRoute(around: latest.coordinate)
        }
    }

    /// Builds a smooth loop path around a center point (~800m long),
    /// evoking a suggested running route that starts and ends near
    /// the user's current position.
    private static func loopRoute(around center: CLLocationCoordinate2D, radiusMeters: Double = 130) -> [CLLocationCoordinate2D] {
        let metersPerDegreeLat = 111_320.0
        let metersPerDegreeLon = 111_320.0 * cos(center.latitude * .pi / 180)

        let pointCount = 48
        var coordinates: [CLLocationCoordinate2D] = []

        for i in 0...pointCount {
            let theta = (Double(i) / Double(pointCount)) * 2 * .pi
            // Limaçon-style curve: pinches near theta = π so the path
            // narrows back toward the starting point, reading as a loop
            // that departs and returns rather than a plain circle.
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
