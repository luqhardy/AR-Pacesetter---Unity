import SwiftUI
import MapKit
import CoreLocation
import Combine

// MARK: - Map Route Screen

struct MapRouteView: View {
    let onNext: () -> Void
    let onBack: () -> Void

    /// 過去の走行(Unity の履歴・経路つき)。以前は誰も渡しておらず、常に「まだ記録がありません」だった
    @ObservedObject private var bridge = UnityBridge.shared
    @StateObject private var locationManager = MapRouteLocationManager()
    @State private var selectedRouteID: RunHistoryEntry.ID?

    /// 経路のある過去の走行を、目標距離(S5で確定した値)に近い順に
    private var pastRoutes: [RunHistoryEntry] {
        PastRouteOrdering.candidates(from: bridge.history,
                                     targetDistanceKm: RunSettings.shared.distanceKm)
    }

    private var selectedRoute: RunHistoryEntry? {
        pastRoutes.first { $0.id == selectedRouteID }
    }

    /// カードを選んだら、上の地図をその経路に合わせる(現在地の追従はやめる)
    private func select(_ route: RunHistoryEntry) {
        selectedRouteID = route.id
        guard let bounds = RouteBounds(route: route.route) else { return }
        locationManager.followsUser = false
        withAnimation(.easeInOut(duration: 0.35)) {
            locationManager.cameraPosition = .region(MKCoordinateRegion(
                center: CLLocationCoordinate2D(latitude: bounds.centerLatitude, longitude: bounds.centerLongitude),
                span: MKCoordinateSpan(latitudeDelta: bounds.latitudeSpan, longitudeDelta: bounds.longitudeSpan)
            ))
        }
    }

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
                    ARLabel(text: AppStrings.Route.eyebrow)
                    Text(AppStrings.Route.title)
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text(AppStrings.Route.subtitle)
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
                            if let route = selectedRoute {
                                MapPolyline(coordinates: route.route.map {
                                    CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
                                })
                                .stroke(Color.arYellow, style: StrokeStyle(lineWidth: 4, lineCap: .round, lineJoin: .round))
                            }
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
                        .onAppear {
                            locationManager.start()
                            bridge.requestHistory()   // Unity 起動済みなら最新へ(未起動なら端末の保存分のまま)
                        }

                        Spacer().frame(height: 20)

                        // Past routes header
                        Text(AppStrings.Route.pastRoutes)
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(.arGrayText)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 28)
                            .padding(.bottom, 10)

                        // Horizontal row of past-run route cards
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 12) {
                                if pastRoutes.isEmpty {
                                    // 経路は 2026-10-13 から記録している。それより前の走行にはカードが出ない
                                    Text(bridge.history.isEmpty
                                         ? AppStrings.Common.noRecords
                                         : AppStrings.Route.noRoutedRecords)
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
                                        .onTapGesture { select(route) }
                                    }
                                }
                            }
                            .padding(.horizontal, 24)
                        }

                        Spacer().frame(height: 24)

                        ARButton(AppStrings.Route.start) { onNext() }
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
    let route: RunHistoryEntry
    let isSelected: Bool

    var body: some View {
        VStack(spacing: 0) {
            Text(route.dateLabel(locale: .appLanguage))
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 10)
                .background(Color.arYellowDim)

            RouteThumbnail(coordinates: route.route.map {
                CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
            })
                .frame(height: 90)
                .clipped()

            Text(AppStrings.Formats.routeDistance(route.distanceKm))
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
    /// 地図を現在地へ合わせ続けるか。過去ルートを選んだら止める(測位のたびに選んだ経路から戻されないように)
    var followsUser = true

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
        guard followsUser, let latest = locations.last, latest.horizontalAccuracy <= 50 else { return }
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
