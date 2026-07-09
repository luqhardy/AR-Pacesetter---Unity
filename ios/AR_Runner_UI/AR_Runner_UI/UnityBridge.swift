import Foundation
import Combine
#if canImport(UnityFramework)
import UnityFramework
#endif

// MARK: - Unity Bridge
// Handles bidirectional communication between Swift UI and Unity AR engine.
//
// ★ AR Pacesetter (Unity) 連携済み完成版 ★
// このファイルで AR_Runner_UI/UnityBridge.swift を置き換えてください。
//
// Unity側の対応コンポーネント (すべて起動時に自動生成されます):
//   - GameObject "ARSessionManager" + ARSessionManagerBridge.cs
//       OnSwiftCommand: StartSession / UpdateMetrics / EndSession
//   - GameObject "DeviceManager" + DeviceManagerBridge.cs
//       OnSwiftCommand: ConnectXREAL
//   - Unity → Swift: Plugins/iOS/UnitySwiftBridge.mm が
//       NSNotification "UnityToSwiftMessage" (userInfo["json"]) を発行

final class UnityBridge: NSObject, ObservableObject {

    static let shared = UnityBridge()

    // MARK: Published State (Swift ← Unity)
    @Published var avatarSyncRate: Int = 0       // 0–100%
    @Published var avatarState: AvatarState = .idle
    @Published var gpsStatus: GPSStatus = .searching
    @Published var motionToPhotonMs: Double = 0  // latency monitor
    @Published var lastResult: SessionResult?    // EndSession後にUnityから届く

    // MARK: Types
    enum AvatarState: String {
        case idle = "Idle"
        case run  = "Run"
        case slow = "Slow"
        case fast = "Fast"
        case goal = "Goal"
        case lost = "Lost"   // GPS lost fade-out
    }

    enum GPSStatus {
        case searching, active, lost, recovered
    }

    struct SessionResult {
        let grade: String        // S / A / B / C / D
        let rank: String         // PERFECT / GREAT / GOOD / TRY AGAIN
        let averageSync: Double  // %
        let distanceKm: Double
        let elapsedSeconds: Double
    }

    // MARK: Init
    private override init() {
        super.init()

        // Unity → Swift: UnitySwiftBridge.mm からの通知を購読
        NotificationCenter.default.addObserver(
            forName: Notification.Name("UnityToSwiftMessage"),
            object: nil,
            queue: .main
        ) { [weak self] note in
            if let json = note.userInfo?["json"] as? String {
                self?.onUnityMessage(json)
            }
        }
    }

    // MARK: Swift → Unity Commands

    /// Start the AR running session with pace settings.
    func startSession(targetPaceKmH: Double, distanceKm: Double) {
        let payload: [String: Any] = [
            "command": "StartSession",
            "targetPaceKmH": targetPaceKmH,
            "distanceKm": distanceKm,
            "avatarHeightCm": 175,
            "forwardOffsetM": 3.0
        ]
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand", payload: payload)
    }

    /// Update real-time runner metrics so Unity can adjust avatar behavior.
    func updateRunnerMetrics(paceKmH: Double, heartRate: Int, distanceKm: Double) {
        let payload: [String: Any] = [
            "command": "UpdateMetrics",
            "paceKmH": paceKmH,
            "heartRate": heartRate,
            "distanceKm": distanceKm
        ]
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand", payload: payload)
    }

    /// End the session and request result data from Unity.
    func endSession() {
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand",
                    payload: ["command": "EndSession"])
    }

    /// Connect XREAL glasses (triggers Unity AR initialization).
    func connect() {
        sendToUnity(object: "DeviceManager", method: "OnSwiftCommand",
                    payload: ["command": "ConnectXREAL"])
    }

    // MARK: Unity → Swift Callbacks

    @objc func onUnityMessage(_ jsonString: String) {
        guard
            let data = jsonString.data(using: .utf8),
            let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
            let event = dict["event"] as? String
        else { return }

        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            switch event {
            case "SyncRateUpdated":
                self.avatarSyncRate = dict["value"] as? Int ?? 0
            case "AvatarStateChanged":
                if let s = dict["state"] as? String, let state = AvatarState(rawValue: s) {
                    self.avatarState = state
                }
            case "GPSLost":
                self.gpsStatus = .lost
            case "GPSRecovered":
                self.gpsStatus = .recovered
            case "LatencyReport":
                self.motionToPhotonMs = dict["ms"] as? Double ?? 0
            case "SessionEnded":
                self.lastResult = SessionResult(
                    grade: dict["grade"] as? String ?? "D",
                    rank: dict["rank"] as? String ?? "TRY AGAIN",
                    averageSync: dict["averageSync"] as? Double ?? 0,
                    distanceKm: dict["distanceKm"] as? Double ?? 0,
                    elapsedSeconds: dict["elapsedSeconds"] as? Double ?? 0
                )
            default:
                break
            }
        }
    }

    // MARK: Private

    private func sendToUnity(object: String, method: String, payload: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let json = String(data: data, encoding: .utf8) else { return }

#if canImport(UnityFramework)
        // Production: Unity as a Library
        UnityFramework.getInstance()?.sendMessageToGO(
            withName: object, functionName: method, message: json)
#else
        // Development / simulator fallback (UnityFramework not linked)
        print("[UnityBridge → Unity] \(object).\(method)(\(json))")
        simulateUnityResponse(event: payload["command"] as? String ?? "")
#endif
    }

    /// Simulate Unity responses during development (inactive in production builds)
    private func simulateUnityResponse(event: String) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
            guard let self else { return }
            switch event {
            case "StartSession":
                self.avatarState = .run
                self.gpsStatus = .active
            case "UpdateMetrics":
                self.avatarSyncRate = Int.random(in: 78...96)
                self.motionToPhotonMs = Double.random(in: 14...19)
            case "EndSession":
                self.avatarState = .goal
            case "ConnectXREAL":
                self.gpsStatus = .searching
            default: break
            }
        }
    }
}

// MARK: - AR Session Manager (coordinates Unity + sensors)
final class ARSessionManager: ObservableObject {
    static let shared = ARSessionManager()
    private let bridge = UnityBridge.shared

    @Published var isSessionActive = false
    @Published var currentDistance: Double = 0
    @Published var elapsedSeconds: Int = 0

    private var timer: Timer?

    func start(paceKmH: Double, distanceKm: Double) {
        isSessionActive = true
        bridge.startSession(targetPaceKmH: paceKmH, distanceKm: distanceKm)
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            guard let self else { return }
            self.elapsedSeconds += 1
            self.currentDistance += paceKmH / 3600
            // TODO: 実機ではCoreLocation/HealthKitの実測値に置き換える
            self.bridge.updateRunnerMetrics(
                paceKmH: paceKmH,
                heartRate: Int.random(in: 138...152),
                distanceKm: self.currentDistance
            )
        }
    }

    func end() {
        endLocally()
        bridge.endSession()
    }

    /// Unity側が先に終了した場合(目標距離到達の自動ゴール)用。
    /// EndSessionを再送せずローカルのタイマー/状態のみ停止する。
    func endLocally() {
        timer?.invalidate()
        timer = nil
        isSessionActive = false
    }
}
