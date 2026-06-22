import SwiftUI
import MapKit

// MARK: - Map Route View
struct MapRouteView: View {
    let onStart: () -> Void
    let onBack: () -> Void
    
    // Hardcoded example endpoints (e.g., around Osaka)
    let startLocation = CLLocationCoordinate2D(latitude: 34.6937, longitude: 135.4983)
    let endLocation = CLLocationCoordinate2D(latitude: 34.7024, longitude: 135.4959)
    
    @State private var route: MKRoute?
    @State private var cameraPosition: MapCameraPosition = .automatic
    
    var body: some View {
        ARScreen {
            ZStack(alignment: .top) {
                // 1. Full-bleed Map
                Map(position: $cameraPosition) {
                    Annotation("スタート", coordinate: startLocation) {
                        Circle()
                            .fill(Color.arYellow)
                            .frame(width: 16, height: 16)
                            .overlay(Circle().stroke(Color.black, lineWidth: 2))
                    }
                    
                    Annotation("ゴール", coordinate: endLocation) {
                        Circle()
                            .fill(Color.red)
                            .frame(width: 16, height: 16)
                            .overlay(Circle().stroke(Color.black, lineWidth: 2))
                    }
                    
                    // The street-snapped path
                    if let route {
                        MapPolyline(route)
                            .stroke(Color.arYellow, style: StrokeStyle(lineWidth: 6, lineCap: .round, lineJoin: .round))
                    }
                }
                .mapStyle(.standard(elevation: .realistic))
                .ignoresSafeArea()
                
                // 2. Top Gradient (makes the status bar readable without a back button)
                LinearGradient(
                    colors: [.black.opacity(0.6), .clear],
                    startPoint: .top, endPoint: .bottom
                )
                .frame(height: 100)
                .ignoresSafeArea()
                
                // 3. Bottom Panel
                VStack(spacing: 0) {
                    Spacer()
                    
                    LinearGradient(
                        colors: [.clear, Color.black.opacity(0.8)],
                        startPoint: .top, endPoint: .bottom
                    )
                    .frame(height: 60)
                    
                    VStack(spacing: 16) {
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("おすすめルート")
                                    .font(.system(size: 14, weight: .bold))
                                    .foregroundColor(.arGrayText)
                                Text(route != nil ? String(format: "%.2f km", route!.distance / 1000.0) : "計算中...")
                                    .font(.system(size: 32, weight: .bold, design: .monospaced))
                                    .foregroundColor(.white)
                            }
                            Spacer()
                        }
                        .padding(.horizontal, 24)
                        
                        Button(action: onStart) {
                            HStack(spacing: 8) {
                                Image(systemName: "play.fill")
                                    .font(.system(size: 15, weight: .bold))
                                Text("ランを開始")
                                    .font(.system(size: 16, weight: .bold))
                            }
                            .foregroundColor(.black)
                            .frame(maxWidth: .infinity)
                            .frame(height: 56)
                            .background(route == nil ? Color.arYellow.opacity(0.5) : Color.arYellow)
                            .clipShape(RoundedRectangle(cornerRadius: 25))
                        }
                        .disabled(route == nil)
                        .padding(.horizontal, 24)
                        .padding(.bottom, 40)
                    }
                    .background(Color.arBG)
                }
            }
        }
        .onAppear {
            calculateRoute()
        }
        // SWIPE TO BACK GESTURE
        .gesture(
            DragGesture()
                .onEnded { value in
                    // If swipe goes from left edge to the right
                    if value.translation.width > 50 && value.startLocation.x < 50 {
                        onBack()
                    }
                }
        )
    }
    
    // Requests the real-world walking path from Apple Maps
    private func calculateRoute() {
        let request = MKDirections.Request()
        request.source = MKMapItem(placemark: MKPlacemark(coordinate: startLocation))
        request.destination = MKMapItem(placemark: MKPlacemark(coordinate: endLocation))
        request.transportType = .walking
        
        Task {
            let directions = MKDirections(request: request)
            if let response = try? await directions.calculate() {
                withAnimation {
                    self.route = response.routes.first
                    // Centers the camera exactly on the calculated route
                    if let boundingBox = self.route?.polyline.boundingMapRect {
                        self.cameraPosition = .rect(boundingBox)
                    }
                }
            }
        }
    }
}
