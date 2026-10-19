import Foundation
import Combine
import CoreMotion

/// Screen-owned probe. Never accesses the application's motion/Unity services.
@MainActor
final class HeadTrackingProbe: NSObject, ObservableObject, CMHeadphoneMotionManagerDelegate {
    enum ForwardAxis: String, CaseIterable, Identifiable {
        case x = "+X", negativeX = "−X", y = "+Y", negativeY = "−Y", z = "+Z", negativeZ = "−Z" // TODO: 文言の集約対象
        var id: String { rawValue }
        var vector: SIMD3<Double> {
            switch self {
            case .x: return SIMD3(1, 0, 0)
            case .negativeX: return SIMD3(-1, 0, 0)
            case .y: return SIMD3(0, 1, 0)
            case .negativeY: return SIMD3(0, -1, 0)
            case .z: return SIMD3(0, 0, 1)
            case .negativeZ: return SIMD3(0, 0, -1)
            }
        }
    }

    @Published private(set) var connected: Bool? = nil
    @Published private(set) var authorization = CMHeadphoneMotionManager.authorizationStatus()
    @Published private(set) var headphoneAvailable = false
    @Published private(set) var phoneAvailable = false
    @Published private(set) var reason: String?
    @Published private(set) var recordingError: String?
    @Published private(set) var headYaw: Double?
    @Published private(set) var phoneYaw: Double?
    @Published private(set) var neckYaw: Double?
    @Published private(set) var frequencyHz = 0.0
    @Published private(set) var maximumInterval: Double?
    @Published private(set) var intervalDeviation: Double?
    @Published private(set) var receiveDelay: Double?
    @Published private(set) var isRecording = false
    @Published private(set) var recordCount = 0
    @Published private(set) var interruptionCount = 0
    @Published private(set) var csvURL: URL?
    @Published private(set) var calibrated = false
    @Published var axis: ForwardAxis = .negativeZ {
        didSet {
            invalidateZero()
            if let motion = latestHead { headYaw = heading(motion, axis: axis.vector) }
        }
    }

    private let headphones = CMHeadphoneMotionManager()
    private let phone = CMMotionManager()
    private var active = false
    private var refreshTimer: Timer?
    private var latestHead: CMDeviceMotion?
    private var phoneSampleTime: Double?
    private var previousHeadTime: Double?
    private var samples: [(time: Double, interval: Double?)] = []
    private var baseline: (head: Double, phone: Double)?
    private var file: FileHandle?

    func start() {
        guard !active else { return }
        active = true
        previousHeadTime = nil
        phoneSampleTime = nil
        latestHead = nil
        headYaw = nil
        phoneYaw = nil
        connected = nil
        samples.removeAll()
        interruptionCount = 0
        receiveDelay = nil
        invalidateZero()
        headphones.delegate = self
        headphones.startConnectionStatusUpdates()
        phoneAvailable = phone.isDeviceMotionAvailable &&
            CMMotionManager.availableAttitudeReferenceFrames().contains(.xArbitraryZVertical)
        if phoneAvailable {
            phone.deviceMotionUpdateInterval = 1.0 / 100.0
            phone.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: .main) { [weak self] motion, error in
                // The explicitly selected main queue serializes state and CSV writes.
                MainActor.assumeIsolated {
                    guard let self, self.active else { return }
                    if let error {
                        self.phoneYaw = nil
                        self.phoneSampleTime = nil
                        self.reason = error.localizedDescription // TODO: 文言の集約対象
                    } else if let motion {
                        self.phoneYaw = self.heading(motion, axis: SIMD3(0, 0, -1))
                        self.phoneSampleTime = motion.timestamp
                    }
                }
            }
        }
        startHeadphonesIfPossible()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, self.active else { return }
                self.updateWindow(now: ProcessInfo.processInfo.systemUptime)
                self.startHeadphonesIfPossible()
                if let time = self.phoneSampleTime,
                   ProcessInfo.processInfo.systemUptime - time >= 0.5 {
                    self.phoneYaw = nil
                    self.neckYaw = nil
                }
                if let last = self.previousHeadTime,
                   ProcessInfo.processInfo.systemUptime - last > 1 {
                    self.headYaw = nil
                    self.neckYaw = nil
                }
            }
        }
    }

    func stop() {
        active = false
        refreshTimer?.invalidate()
        refreshTimer = nil
        headphones.stopDeviceMotionUpdates()
        headphones.stopConnectionStatusUpdates()
        headphones.delegate = nil
        phone.stopDeviceMotionUpdates()
        stopRecording()
        invalidateZero()
    }

    private func startHeadphonesIfPossible() {
        authorization = CMHeadphoneMotionManager.authorizationStatus()
        headphoneAvailable = headphones.isDeviceMotionAvailable
        guard authorization != .denied && authorization != .restricted else {
            reason = "モーションの利用が許可されていません。設定を確認してください。" // TODO: 文言の集約対象
            return
        }
        guard phoneAvailable else {
            reason = "iPhone の deviceMotion を利用できません。" // TODO: 文言の集約対象
            return
        }
        guard headphoneAvailable else {
            reason = "対応する AirPods を接続・装着してください。" // TODO: 文言の集約対象
            return
        }
        guard !headphones.isDeviceMotionActive else { return }
        reason = "AirPods の許可・サンプルを待っています。" // TODO: 文言の集約対象
        headphones.startDeviceMotionUpdates(to: .main) { [weak self] motion, error in
            let received = ProcessInfo.processInfo.systemUptime
            MainActor.assumeIsolated {
                guard let self, self.active else { return }
                self.authorization = CMHeadphoneMotionManager.authorizationStatus()
                if let error {
                    self.reason = error.localizedDescription // TODO: 文言の集約対象
                    self.headYaw = nil
                    self.neckYaw = nil
                } else if let motion {
                    self.receive(motion, received: received)
                }
            }
        }
    }

    nonisolated func headphoneMotionManagerDidConnect(_ manager: CMHeadphoneMotionManager) {
        Task { @MainActor [weak self] in
            guard let self, self.active else { return }
            self.connected = true
            self.invalidateZero()
            self.startHeadphonesIfPossible()
        }
    }

    nonisolated func headphoneMotionManagerDidDisconnect(_ manager: CMHeadphoneMotionManager) {
        Task { @MainActor [weak self] in
            guard let self, self.active else { return }
            self.connected = false
            self.headphones.stopDeviceMotionUpdates()
            self.headYaw = nil
            self.latestHead = nil
            self.invalidateZero()
            self.reason = "AirPods が切断されました。再接続後はゼロ合わせしてください。" // TODO: 文言の集約対象
            // Keep previousHeadTime so the next sample exposes the disconnect gap.
        }
    }

    private func heading(_ motion: CMDeviceMotion, axis: SIMD3<Double>) -> Double? {
        let r = motion.attitude.rotationMatrix
        return HeadTrackingProbeMath.heading(
            rows: [SIMD3(r.m11, r.m12, r.m13), SIMD3(r.m21, r.m22, r.m23), SIMD3(r.m31, r.m32, r.m33)],
            axis: axis, gravity: SIMD3(motion.gravity.x, motion.gravity.y, motion.gravity.z))
    }

    private func invalidateZero() {
        baseline = nil
        calibrated = false
        neckYaw = nil
    }

    func zero() {
        let now = ProcessInfo.processInfo.systemUptime
        guard let head = headYaw, let body = phoneYaw,
              let headTime = previousHeadTime, let phoneTime = phoneSampleTime,
              now - headTime >= 0, now - headTime < 0.5,
              now - phoneTime >= 0, now - phoneTime < 0.5 else {
            reason = "新しい頭・iPhone の姿勢が揃ってからゼロ合わせしてください。" // TODO: 文言の集約対象
            return
        }
        baseline = (head, body)
        calibrated = true
        neckYaw = 0
        reason = nil
    }

    private func receive(_ motion: CMDeviceMotion, received: Double) {
        let timestamp = motion.timestamp
        var interval: Double?
        if let previous = previousHeadTime {
            let delta = timestamp - previous
            if delta > 0 {
                interval = delta
                if delta > 1 { interruptionCount += 1 }
            } else {
                samples.removeAll()
                invalidateZero()
            }
        }
        previousHeadTime = timestamp
        latestHead = motion
        headYaw = heading(motion, axis: axis.vector)
        receiveDelay = received - timestamp
        samples.append((timestamp, interval))
        updateWindow(now: received)
        let phoneFresh = phoneSampleTime.map { received - $0 >= 0 && received - $0 < 0.5 } ?? false
        if let head = headYaw, let body = phoneYaw, let baseline, phoneFresh {
            neckYaw = HeadTrackingProbeMath.wrap((head - baseline.head) - (body - baseline.phone))
        } else {
            neckYaw = nil
        }
        if headYaw == nil {
            reason = "選択軸が鉛直に近いためヨーを計算できません。軸を変更してください。" // TODO: 文言の集約対象
        } else if !phoneFresh || phoneYaw == nil {
            reason = "iPhone の姿勢が古い、または前方軸が鉛直に近いため首角度は無効です。" // TODO: 文言の集約対象
        } else {
            reason = nil
        }
        // Pitch/roll are raw Core Motion Euler angles in degrees; only yaw uses projection.
        if isRecording {
            let fields = [number(timestamp), number(received), number(headYaw),
                          number(motion.attitude.pitch * 180 / .pi), number(motion.attitude.roll * 180 / .pi),
                          number(phoneYaw), number(neckYaw), connected.map { $0 ? "1" : "0" } ?? ""]
            do {
                guard let file else { isRecording = false; return }
                try file.write(contentsOf: Data((fields.joined(separator: ",") + "\n").utf8))
                recordCount += 1
            } catch {
                recordingError = error.localizedDescription // TODO: 文言の集約対象
                stopRecording()
            }
        }
    }

    private func updateWindow(now: Double) {
        samples.removeAll { $0.time < now - 5 }
        frequencyHz = Double(samples.count) / 5
        let stats = HeadTrackingProbeMath.intervalStatistics(samples.compactMap(\.interval))
        maximumInterval = stats.maximum
        intervalDeviation = stats.deviation
    }

    func startRecording() {
        guard !isRecording else { return }
        recordingError = nil
        do {
            let directory = try FileManager.default.url(for: .documentDirectory, in: .userDomainMask,
                                                       appropriateFor: nil, create: true)
                .appendingPathComponent("HeadTracking", isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "en_US_POSIX")
            formatter.dateFormat = "yyyyMMdd_HHmmss_SSS"
            let url = directory.appendingPathComponent("HeadTracking_\(formatter.string(from: Date()))_\(UUID().uuidString).csv")
            let header = "sampleTime,receivedTime,headYaw,headPitch,headRoll,phoneYaw,neckYaw,connected\n"
            try Data(header.utf8).write(to: url, options: .atomic)
            let handle = try FileHandle(forWritingTo: url)
            try handle.seekToEnd()
            file = handle
            csvURL = url
            recordCount = 0
            isRecording = true
        } catch {
            recordingError = error.localizedDescription // TODO: 文言の集約対象
        }
    }

    func stopRecording() {
        isRecording = false
        do {
            try file?.synchronize()
        } catch {
            recordingError = error.localizedDescription // TODO: 文言の集約対象
        }
        do {
            try file?.close()
        } catch {
            recordingError = error.localizedDescription // TODO: 文言の集約対象
        }
        file = nil
    }

    private func number(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "" }
        return String(format: "%.6f", locale: Locale(identifier: "en_US_POSIX"), value)
    }
}
