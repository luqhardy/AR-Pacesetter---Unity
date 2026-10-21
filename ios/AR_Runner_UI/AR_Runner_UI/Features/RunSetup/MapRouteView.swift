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
    /// 選んでいる候補。画面のローカル状態なので、「戻る」で S5 へ戻れば捨てられ、
    /// 次に来たときは最初の候補を選んだ状態からやり直す(タスク表 S8)
    @State private var selectedRouteID: String?
    /// 「詳細を見る」で開いている候補。閉じても選択は変えない(タスク表 S8)
    @State private var detailCandidate: RouteCandidate?
    /// 「ルートを外れたらアバターが案内する」(既定 OFF。過去に走ったルートでだけ使える — RouteGuidance)
    @AppStorage(RouteGuidance.storageKey) private var routeGuidanceEnabled = false

    /// 候補: 経路のある過去の走行を目標距離(S5で確定した値)に近い順に。
    /// 1件も無ければ、現在地から北へ行って戻る往復を作る(タスク表 S8「候補が無いときは既定のルート」)
    private var candidates: [RouteCandidate] {
        RouteCandidates.build(history: bridge.history,
                              targetDistanceKm: RunSettings.shared.distanceKm,
                              currentLocation: locationManager.originCoordinate)
    }

    private var selectedRoute: RouteCandidate? {
        candidates.first { $0.id == selectedRouteID }
    }

    /// 候補が変わった(履歴が届いた・現在地が取れた)ら、選択を保つか先頭を選び直す
    private func refreshSelection() {
        let next = RouteCandidates.initialSelection(candidates, keeping: selectedRouteID)
        guard next != selectedRouteID else { return }
        selectedRouteID = next
        if let route = selectedRoute { focusMap(on: route) }
    }

    /// カードを選んだら、上の地図をその経路に合わせる(現在地の追従はやめる)。ポップアップは出さない
    private func select(_ route: RouteCandidate) {
        selectedRouteID = route.id
        focusMap(on: route)
    }

    private func focusMap(on route: RouteCandidate) {
        guard route.hasLine, let bounds = RouteBounds(route: route.coordinates) else { return }
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
                        // 上: 選んでいるルートを表示する地図
                        Map(position: $locationManager.cameraPosition) {
                            UserAnnotation()
                            if let route = selectedRoute, route.hasLine {
                                MapPolyline(coordinates: route.coordinates.map {
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
                            if let problem = locationManager.locationProblem {
                                LocationPermissionMessage(problem: problem)
                            }
                        }
                        .padding(.horizontal, 24)
                        .onAppear {
                            locationManager.start()
                            bridge.requestHistory()   // Unity 起動済みなら最新へ(未起動なら端末の保存分のまま)
                            refreshSelection()
                        }
                        .onChange(of: candidates.map(\.id)) { _, _ in refreshSelection() }
                        // 往復ルートは現在地が取れてから線が引ける。選んだまま線が付いたら地図をそこへ合わせる
                        // (以前は現在地の拡大のままで、数kmの線が画面外にあった)
                        .onChange(of: selectedRoute?.hasLine) { _, hasLine in
                            if hasLine == true, let route = selectedRoute { focusMap(on: route) }
                        }

                        Spacer().frame(height: 20)

                        // Candidates header
                        Text(AppStrings.Route.pastRoutes)
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(.arGrayText)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 28)
                            .padding(.bottom, 10)

                        // 下: 候補カードを横スクロールで(目標距離に近い順)
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 12) {
                                ForEach(candidates) { route in
                                    RouteCandidateCard(
                                        route: route,
                                        isSelected: selectedRouteID == route.id,
                                        onSelect: { select(route) },
                                        onDetails: { detailCandidate = route }
                                    )
                                }
                            }
                            .padding(.horizontal, 24)
                        }

                        // 既定のルートしか無いときは、なぜそれが出ているかを書く
                        if candidates.first?.kind == .generated {
                            Text(locationManager.originCoordinate == nil
                                 ? AppStrings.Route.generatedNoLocation
                                 : AppStrings.Route.generatedNote)
                                .font(.system(size: 12))
                                .foregroundColor(.arGrayText)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .fixedSize(horizontal: false, vertical: true)
                                .padding(.horizontal, 28)
                                .padding(.top, 10)
                        }

                        Spacer().frame(height: 16)

                        // 選んだルートから外れたらアバターが案内する(HANDOVER §5「ルート同期」)
                        routeGuidanceCard
                            .padding(.horizontal, 24)

                        Spacer().frame(height: 24)

                        // ルートが選ばれていないと開始できない(タスク表 S8 異常系)
                        ARButton(AppStrings.Route.start) {
                            guard selectedRoute != nil else { return }
                            RunSettings.shared.guidanceRoute = RouteGuidance.payload(for: selectedRoute,
                                                                                    enabled: routeGuidanceEnabled)
                            onNext()
                        }
                        .disabled(selectedRoute == nil)
                        .opacity(selectedRoute == nil ? 0.4 : 1)
                        .padding(.horizontal, 24)
                        .padding(.bottom, 52)
                    }
                }
            }
        }
        .sheet(item: $detailCandidate) { route in
            RouteDetailSheet(route: route)
        }
    }

    /// 「ルートを外れたらアバターが案内する」。作った往復ルートでは使えない
    private var routeGuidanceCard: some View {
        let available = RouteGuidance.isAvailable(for: selectedRoute)
        return VStack(alignment: .leading, spacing: 6) {
            Toggle(isOn: $routeGuidanceEnabled) {
                Text(AppStrings.Route.guidanceToggle)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(.white)
            }
            .tint(.arYellow)
            .disabled(!available)
            Text(available ? AppStrings.Route.guidanceNote : AppStrings.Route.guidanceUnavailable)
                .font(.system(size: 12))
                .foregroundColor(.arGrayText)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(14)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.arBorder, lineWidth: 1))
        .opacity(available ? 1 : 0.6)
    }
}

// MARK: - Route candidate card

private struct RouteCandidateCard: View {
    let route: RouteCandidate
    let isSelected: Bool
    let onSelect: () -> Void
    let onDetails: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Button(action: onSelect) {
                VStack(spacing: 0) {
                    Text(route.title)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.white)
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 10)
                        .padding(.horizontal, 6)
                        .background(Color.arYellowDim)

                    RouteThumbnail(coordinates: route.coordinates.map {
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
            }
            .buttonStyle(.plain)

            // 「詳細を見る」— ポップアップで全体像・距離・推定時間・ペース(タスク表 S8)
            Button(action: onDetails) {
                Text(AppStrings.Route.details)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(.arYellow)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                    .background(Color.arCard)
            }
            .buttonStyle(.plain)
        }
        .frame(width: 130)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(isSelected ? Color.arYellow : Color.arBorder, lineWidth: isSelected ? 2 : 1)
        )
    }
}

private extension RouteCandidate {
    /// カードの見出し: 過去のルートは日付、作ったルートは「おまかせ(往復)」
    var title: String {
        guard kind == .past, let dateIso else { return AppStrings.Route.generatedTitle }
        return RunHistoryEntry(dateIso: dateIso, distanceKm: distanceKm, elapsedSeconds: 0,
                               averageSync: 0, grade: "").dateLabel(locale: .appLanguage)
    }
}

// MARK: - Route details popup

/// ルートの全体像・距離・推定時間・ペース。閉じても選択は変えない
private struct RouteDetailSheet: View {
    let route: RouteCandidate
    @Environment(\.dismiss) private var dismiss

    private var paceKmH: Double { RunSettings.shared.paceKmH }

    private var estimatedTime: String {
        RunResultFormat.duration(route.estimatedSeconds(paceKmH: paceKmH).map { Double($0) })
    }

    private var region: MapCameraPosition {
        guard let bounds = RouteBounds(route: route.coordinates) else { return .userLocation(fallback: .automatic) }
        return .region(MKCoordinateRegion(
            center: CLLocationCoordinate2D(latitude: bounds.centerLatitude, longitude: bounds.centerLongitude),
            span: MKCoordinateSpan(latitudeDelta: bounds.latitudeSpan, longitudeDelta: bounds.longitudeSpan)))
    }

    var body: some View {
        ARScreen {
            VStack(alignment: .leading, spacing: 16) {
                HStack {
                    Text(AppStrings.Route.detailsTitle)
                        .font(.system(size: 22, weight: .bold))
                        .foregroundColor(.white)
                    Spacer()
                    Button { dismiss() } label: {
                        Image(systemName: "xmark")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(.white)
                            .frame(width: 34, height: 34)
                            .background(Color.arCard, in: Circle())
                    }
                    .accessibilityLabel(Text(AppStrings.Common.close))
                }

                Map(initialPosition: region) {
                    if route.hasLine {
                        MapPolyline(coordinates: route.coordinates.map {
                            CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
                        })
                        .stroke(Color.arYellow, style: StrokeStyle(lineWidth: 4, lineCap: .round, lineJoin: .round))
                    }
                }
                .mapStyle(.standard(elevation: .flat, pointsOfInterest: .excludingAll))
                .colorScheme(.dark)
                .frame(height: 240)
                .clipShape(RoundedRectangle(cornerRadius: 18))

                Grid(horizontalSpacing: 10, verticalSpacing: 10) {
                    GridRow {
                        StatMiniCard(title: AppStrings.Route.distance,
                                     value: RunResultFormat.distanceKm(route.distanceKm), unit: "km")
                        StatMiniCard(title: AppStrings.Route.estimatedTime, value: estimatedTime, unit: "")
                    }
                    GridRow {
                        StatMiniCard(title: AppStrings.Route.pace,
                                     value: RunSettings.shared.paceMinPerKmString, unit: "/km")
                        Color.clear.frame(height: 1)
                    }
                }

                Spacer()
            }
            .padding(24)
        }
        .presentationDetents([.large])
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

    /// 最初に取れた現在地。既定のルート(往復)の起点 — 測位のたびに線が揺れないよう、最初の1回で固定する
    @Published private(set) var originCoordinate: RouteCoordinate?

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
        guard let latest = locations.last, latest.horizontalAccuracy >= 0, latest.horizontalAccuracy <= 50 else { return }
        if originCoordinate == nil {
            originCoordinate = RouteCoordinate(latitude: latest.coordinate.latitude,
                                               longitude: latest.coordinate.longitude)
        }
        guard followsUser else { return }
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
