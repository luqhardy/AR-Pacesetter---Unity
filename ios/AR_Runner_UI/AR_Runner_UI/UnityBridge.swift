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
    @Published var history: [HistoryEntry] = []  // RequestHistory応答(新しい順)
    @Published var lowBatteryMode = false        // Unity側が低バッテリー退避したら true

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
        let calories: Double     // Unity側でオンボーディング体重から算出
    }

    struct HistoryEntry: Identifiable {
        let id = UUID()
        let dateIso: String      // ISO8601 (Unity側 DateTime "o" 形式)
        let distanceKm: Double
        let elapsedSeconds: Double
        let averageSync: Double  // %
        let grade: String

        /// "6月15日" 形式の表示用日付
        var dateLabel: String {
            let isoDay = String(dateIso.prefix(10)) // yyyy-MM-dd
            let parser = DateFormatter()
            parser.dateFormat = "yyyy-MM-dd"
            guard let date = parser.date(from: isoDay) else { return isoDay }
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "ja_JP")
            formatter.dateFormat = "M月d日"
            return formatter.string(from: date)
        }

        var timeLabel: String {
            let total = Int(elapsedSeconds)
            return String(format: "%02d:%02d", total / 60, total % 60)
        }
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
    /// ghostDateIso を渡すと過去セッションと競走するゴーストモードになる。
    func startSession(targetPaceKmH: Double, distanceKm: Double, ghostDateIso: String? = nil) {
        var payload: [String: Any] = [
            "command": "StartSession",
            "targetPaceKmH": targetPaceKmH,
            "distanceKm": distanceKm,
            "avatarHeightCm": 175,
            "forwardOffsetM": 3.0
        ]
        if let ghost = ghostDateIso, !ghost.isEmpty {
            payload["mode"] = "ghost"
            payload["ghostDateIso"] = ghost
        }
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand", payload: payload)
    }

    /// Update real-time runner metrics so Unity can adjust avatar behavior.
    /// gpsAccuracy > 0 のときUnity側が有効な測位サンプルとして扱い、
    /// GPSロスト自動判定(§8.1)と走行ログCSVのGPS列(§5.2)に使用する。
    func updateRunnerMetrics(paceKmH: Double, heartRate: Int, distanceKm: Double,
                             gpsLatitude: Double = 0, gpsLongitude: Double = 0,
                             gpsAccuracy: Double = -1,
                             locationSampleFresh: Bool = false,
                             speedSampleValid: Bool = false) {
        var payload: [String: Any] = [
            "command": "UpdateMetrics",
            "paceKmH": paceKmH,
            "heartRate": heartRate,
            "distanceKm": distanceKm,
            "locationSampleFresh": locationSampleFresh,
            "speedSampleValid": speedSampleValid
        ]
        if gpsAccuracy >= 0 {
            payload["gpsLatitude"] = gpsLatitude
            payload["gpsLongitude"] = gpsLongitude
            payload["gpsAccuracy"] = gpsAccuracy
        }
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

    /// ARグラス切断 (§8.3): Unityをスタンバイへ移行させアバターを消去する。
    /// 走行セッションは終了させないため、CSVログ書き出しは継続する。
    func disconnectGlass() {
        sendToUnity(object: "DeviceManager", method: "OnSwiftCommand",
                    payload: ["command": "DisconnectXREAL"])
    }

    /// 準備画面からの再スタート (§8.3): スタンバイ中の走行表示を復帰させる。
    /// 新規セッションは開始しない(記録は継続)。
    func resumeSession() {
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand",
                    payload: ["command": "ResumeSession"])
    }

    /// Request past run history from Unity's session store (HistoryData event).
    func requestHistory() {
        sendToUnity(object: "ARSessionManager", method: "OnSwiftCommand",
                    payload: ["command": "RequestHistory"])
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
                let result = SessionResult(
                    grade: dict["grade"] as? String ?? "D",
                    rank: dict["rank"] as? String ?? "TRY AGAIN",
                    averageSync: dict["averageSync"] as? Double ?? 0,
                    distanceKm: dict["distanceKm"] as? Double ?? 0,
                    elapsedSeconds: dict["elapsedSeconds"] as? Double ?? 0,
                    calories: dict["calories"] as? Double ?? 0
                )
                self.lastResult = result

                // デュアル・データ保存(企画書§4): HealthKitへワークアウト書き込み
                HealthKitWorkoutSaver.shared.saveWorkout(
                    distanceKm: result.distanceKm,
                    elapsedSeconds: result.elapsedSeconds,
                    calories: result.calories
                )
            case "LowBattery":
                self.lowBatteryMode = true
            case "VoiceAlert":
                // 企画書4.3: 赤信号・交差点の音声警告(TTC短い方を優先)
                VoiceAlertSpeaker.shared.speak(
                    kind: dict["kind"] as? String ?? "",
                    ttcSeconds: dict["ttc"] as? Double ?? .infinity
                )
            case "HistoryData":
                if let sessions = dict["sessions"] as? [[String: Any]] {
                    self.history = sessions.map { s in
                        HistoryEntry(
                            dateIso: s["dateIso"] as? String ?? "",
                            distanceKm: s["distanceKm"] as? Double ?? 0,
                            elapsedSeconds: s["elapsedSeconds"] as? Double ?? 0,
                            averageSync: s["averageSync"] as? Double ?? 0,
                            grade: s["grade"] as? String ?? "-"
                        )
                    }
                }
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
            case "RequestHistory":
                // シミュレータ用のダミー履歴(実機ではUnityのJSON DBから届く)
                self.history = [
                    HistoryEntry(dateIso: "2026-07-08T07:12:00", distanceKm: 5.0,
                                 elapsedSeconds: 1610, averageSync: 91.2, grade: "S"),
                    HistoryEntry(dateIso: "2026-07-05T18:40:00", distanceKm: 3.2,
                                 elapsedSeconds: 1064, averageSync: 83.5, grade: "A"),
                    HistoryEntry(dateIso: "2026-07-01T06:55:00", distanceKm: 2.1,
                                 elapsedSeconds: 705, averageSync: 76.8, grade: "B"),
                ]
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

    /// 画面表示用の距離(km)。GPS未取得の間は設定ペースからの推定で連続性を保つ。
    /// **推定値はUnityのゴール判定やHealthKitへは渡さない**(H3)
    @Published var currentDistance: Double = 0

    /// 実測(CoreLocation)のみの距離(km)。Unityへ送るのはこちら
    @Published var measuredDistanceKm: Double = 0

    /// currentDistance に推定分が含まれているか(HUDの「推定」表示用)
    @Published var isDistanceEstimated = false

    @Published var elapsedSeconds: Int = 0

    private var timer: Timer?

    /// 経過時間の基準となる実時刻。タイマーの発火回数を数えると
    /// バックグラウンド・タイマー合体で過少カウントになるため壁時計で測る(H4)
    private var startDate: Date?

    /// 最後にUnityへ送った測位サンプルの時刻。同じfixを再送しないための番兵(C2)
    private var lastSentFixDate: Date?

    /// 設定ペース(推定距離の算出に使う)
    private var configuredPaceKmH: Double = 0

    func start(paceKmH: Double, distanceKm: Double, ghostDateIso: String? = nil) {
        isSessionActive = true
        startDate = Date()
        elapsedSeconds = 0
        currentDistance = 0
        measuredDistanceKm = 0
        isDistanceEstimated = false
        lastSentFixDate = nil
        configuredPaceKmH = paceKmH

        // 実測センサー起動: CoreLocation(距離/速度) + HealthKit(心拍・Watch経由)
        LocationTracker.shared.start()
        HeartRateMonitor.shared.start()

        bridge.startSession(targetPaceKmH: paceKmH, distanceKm: distanceKm, ghostDateIso: ghostDateIso)

        // 実測が届くたびに送る — 画面ロック中もCoreLocationは動き続けるため、
        // タイマーが止まってもUnityへの供給が途切れない(M3)
        LocationTracker.shared.onNewFix = { [weak self] in
            self?.pumpMetrics(estimateWhenNoGps: false)
        }

        // タイマーはHUD更新と、GPSが無い環境での推定距離の前進を担当する
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            self?.pumpMetrics(estimateWhenNoGps: true)
        }
    }

    /// Unityへメトリクスを1回送る。
    /// - Parameter estimateWhenNoGps: GPS未取得時に表示用距離を推定で前進させるか
    ///   (タイマー駆動のときだけ true。実測駆動では二重に進めない)
    private func pumpMetrics(estimateWhenNoGps: Bool) {
        guard isSessionActive else { return }
        let tracker = LocationTracker.shared

        // 経過時間は壁時計から算出(タイマーの発火回数に依存しない — H4)
        if let start = startDate {
            elapsedSeconds = max(0, Int(Date().timeIntervalSince(start)))
        }

        // 距離: 実測と表示用を分離する(H3)
        measuredDistanceKm = tracker.totalDistanceKm
        if measuredDistanceKm > 0.001 {
            currentDistance = measuredDistanceKm
            isDistanceEstimated = false
        } else if estimateWhenNoGps {
            // 表示の連続性のためだけの推定。Unityへは送らない
            currentDistance += configuredPaceKmH / 3600
            isDistanceEstimated = true
        }

        // ペース: GPS実測だけをUnityへ送る。設定ペースは「目標」であって実測ではない。
        // GPS未取得時に設定値を送ると、静止中でもSync 100%と誤判定される。
        let gpsSpeed = tracker.currentSpeedKmH
        let speedIsMeasured = tracker.hasValidSpeedMeasurement
        let measuredPace = speedIsMeasured ? gpsSpeed : 0

        // 心拍: HealthKit実測(Watch装着時)のみ。未取得は0=不明として送る(H2)
        let bpm = HeartRateMonitor.shared.latestBpm

        // 測位サンプルは「前回送ったfixと違う」ときだけ添付する。
        // 同じfixを毎秒再送すると、Unity側の更新途絶タイマーが永久にリセットされ
        // F-09のロスト判定(1.5秒途絶)が成立しなくなる(C2)
        let fixDate = tracker.latestFixDate
        let isNewFix = fixDate != nil && fixDate != lastSentFixDate

        if isNewFix {
            lastSentFixDate = fixDate
            let acceptedForMetrics = tracker.latestFixAcceptedForMetrics
            bridge.updateRunnerMetrics(
                paceKmH: acceptedForMetrics ? measuredPace : 0,
                heartRate: bpm,
                distanceKm: measuredDistanceKm,
                gpsLatitude: tracker.latestLatitude,
                gpsLongitude: tracker.latestLongitude,
                gpsAccuracy: tracker.latestAccuracyMeters,
                locationSampleFresh: acceptedForMetrics,
                speedSampleValid: acceptedForMetrics && speedIsMeasured
            )
        } else {
            // 測位は添付せず、実測ペースも0(不明)にする。キャッシュ距離は画面の
            // 連続性用に送るが locationSampleFresh=false のためUnityの鮮度は更新しない。
            bridge.updateRunnerMetrics(
                paceKmH: 0,
                heartRate: bpm,
                distanceKm: measuredDistanceKm
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
        startDate = nil
        isSessionActive = false
        LocationTracker.shared.onNewFix = nil
        LocationTracker.shared.stop()
        HeartRateMonitor.shared.stop()
    }
}
