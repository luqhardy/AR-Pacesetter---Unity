import SwiftUI
import MapKit
import CoreLocation
import Combine

// MARK: - Route history item (past run, used for the horizontal card row)

struct PastRoute: Identifiable {
    let id = UUID()
    let dateText: String
    let distanceKm: Double
    let coordinates: [CLLocationCoordinate2D]
}

// MARK: - Map Route Screen

struct MapRouteView: View {
    let onNext: () -> Void
    let onBack: () -> Void

    /// Past runs to show as selectable cards below the map.
    /// Defaults to an empty array — pass real history data in from
    /// ContentView once it's available (e.g. from StatsHistoryView's
    /// history source) to replace the placeholder cards.
    var pastRoutes: [PastRoute] = []

    @StateObject private var locationManager = MapRouteLocationManager()
    @State private var selectedRouteID: PastRoute.ID?

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: "ROUTE")
                    Text("ルートを選択")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text("現在地を確認してください")
                        .font(.system(size: 14))
                        .foregroundColor(.arGrayText)
                        .padding(.top, 2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 20)

                ScrollView(showsIndicators: false) {
                    VStack(spacing: 0) {
                        // Large current-location map
                        Map(position: $locationManager.cameraPosition) {
                            UserAnnotation()
                        }
                        .mapStyle(.standard(elevation: .flat, pointsOfInterest: .excludingAll))
                        .mapControls { }
                        .colorScheme(.dark)
                        .frame(height: 280)
                        .clipShape(RoundedRectangle(cornerRadius: 24))
                        .overlay(
                            RoundedRectangle(cornerRadius: 24)
                                .stroke(Color.arBorder, lineWidth: 1)
                        )
                        .overlay {
                            if locationManager.isPermissionDenied {
                                LocationPermissionMessage()
                            }
                        }
                        .padding(.horizontal, 24)
                        .onAppear { locationManager.start() }

                        Spacer().frame(height: 20)

                        // Past routes header
                        Text("過去のルート")
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(.arGrayText)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 28)
                            .padding(.bottom, 10)

                        // Horizontal row of past-run route cards
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 12) {
                                if pastRoutes.isEmpty {
                                    Text("まだ記録がありません")
                                        .font(.system(size: 13))
                                        .foregroundColor(.arGrayText)
                                        .padding(.vertical, 40)
                                        .padding(.horizontal, 20)
                                } else {
                                    ForEach(pastRoutes) { route in
                                        PastRouteCard(
                                            route: route,
                                            isSelected: selectedRouteID == route.id
                                        )
                                        .onTapGesture {
                                            selectedRouteID = route.id
                                        }
                                    }
                                }
                            }
                            .padding(.horizontal, 24)
                        }

                        Spacer().frame(height: 24)

                        ARButton("ランニングを開始") { onNext() }
                            .padding(.horizontal, 24)
                            .padding(.bottom, 52)
                    }
                }
            }
        }
    }
}

// MARK: - Past route card

private struct PastRouteCard: View {
    let route: PastRoute
    let isSelected: Bool

    var body: some View {
        VStack(spacing: 0) {
            Text(route.dateText)
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 10)
                .background(Color.arYellowDim)

            RouteThumbnail(coordinates: route.coordinates)
                .frame(height: 90)
                .clipped()

            Text(String(format: "距離%.2fkm", route.distanceKm))
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 8)
                .background(Color.arCard)
        }
        .frame(width: 130)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(isSelected ? Color.arYellow : Color.arBorder, lineWidth: isSelected ? 2 : 1)
        )
    }
}

// MARK: - Small static route line thumbnail (no live map needed for a card)

private struct RouteThumbnail: View {
    let coordinates: [CLLocationCoordinate2D]

    var body: some View {
        ZStack {
            Color.arCard

            if coordinates.count > 1 {
                GeometryReader { proxy in
                    let lats = coordinates.map(\.latitude)
                    let lons = coordinates.map(\.longitude)
                    let minLat = lats.min() ?? 0
                    let maxLat = lats.max() ?? 0
                    let minLon = lons.min() ?? 0
                    let maxLon = lons.max() ?? 0
                    let latRange = max(maxLat - minLat, 0.0001)
                    let lonRange = max(maxLon - minLon, 0.0001)

                    Path { path in
                        for (index, coordinate) in coordinates.enumerated() {
                            let x = ((coordinate.longitude - minLon) / lonRange) * proxy.size.width
                            let y = (1 - (coordinate.latitude - minLat) / latRange) * proxy.size.height
                            if index == 0 {
                                path.move(to: CGPoint(x: x, y: y))
                            } else {
                                path.addLine(to: CGPoint(x: x, y: y))
                            }
                        }
                    }
                    .stroke(Color.arYellow, style: StrokeStyle(lineWidth: 3, lineCap: .round, lineJoin: .round))
                    .padding(10)
                }
            } else {
                Image(systemName: "figure.run")
                    .font(.system(size: 20))
                    .foregroundColor(.arGrayText)
            }
        }
    }
}

// MARK: - Location manager (current-location map only)

private final class MapRouteLocationManager: NSObject, ObservableObject, CLLocationManagerDelegate {
    @Published var cameraPosition: MapCameraPosition = .automatic
    @Published private(set) var authorizationStatus: CLAuthorizationStatus

    private let manager = CLLocationManager()

    override init() {
        authorizationStatus = manager.authorizationStatus
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBest
    }

    var isPermissionDenied: Bool {
        authorizationStatus == .denied || authorizationStatus == .restricted
    }

    func start() {
        authorizationStatus = manager.authorizationStatus
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
        authorizationStatus = manager.authorizationStatus
        if manager.authorizationStatus == .authorizedWhenInUse
            || manager.authorizationStatus == .authorizedAlways {
            manager.startUpdatingLocation()
        }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let latest = locations.last, latest.horizontalAccuracy <= 50 else { return }
        cameraPosition = .region(
            MKCoordinateRegion(
                center: latest.coordinate,
                span: MKCoordinateSpan(latitudeDelta: 0.006, longitudeDelta: 0.006)
            )
        )
    }
}

#Preview {
    MapRouteView(onNext: {}, onBack: {})
}
