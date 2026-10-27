import Foundation
import Combine
import Network
import QuartzCore

/// XREAL One のIMU(TCP 169.254.2.1:52998)を受け取る(形式は AppLogic/XrealImuStream.swift、
/// 頭のヨーと 100Hz の加速度への変換は AppLogic/XrealImuFusion.swift)。
///
/// 使い方は2つ:
/// - **走行用**(準備画面 S4 のグラスIMU行で「接続」)— 頭のヨーを Unity の UpdateHeadYaw へ送り
///   (AirPods の代わり。届いている間は AirPods の分を送らない)、重力を除いた 100Hz の加速度を
///   走行ログCSV(F-11)の imu_accel 列へ渡す。走行の終了・準備画面の「戻る」で止める
/// - **確かめるだけ**(開発者モード)— 画面に出すだけで Unity へは何も送らない
///
/// **既定では動かさない**。**読むだけ** — グラスへは1バイトも送らない(52999 の設定口は触らない)。
final class XrealImuReceiver: ObservableObject {

    static let shared = XrealImuReceiver()

    enum Status: Equatable {
        case off
        /// 繋いでいる途中
        case connecting
        /// 繋がったがまだIMUが届いていない
        case connected
        case streaming
        /// USBネットワークが上がっていない・ローカルネットワークの許可が無いと、ここで再試行を待ち続ける
        case waiting(String)
        case failed(String)
    }

    @Published private(set) var status: Status = .off
    @Published private(set) var snapshot = XrealImuSnapshot()
    /// 準備画面の行の状態(走行用に使っているときだけ off 以外)
    @Published private(set) var runStatus: GlassImuStatus = .off

    private var reader: XrealImuReader?
    /// 走行用に使っているか(頭のヨー・CSV へ送る)
    private(set) var isForRun = false
    private var requestedAt: Date?
    private var lastSampleAt: Date?
    private var statusTimer: Timer?

    private init() {}

    var isRunning: Bool { reader != nil }

    /// 頭のヨーをグラスから送っているか。AirPods はこの間 Unity へ送らない(二つの基準がぶつからないように)
    var isDrivingHeadYaw: Bool { isForRun && runStatus == .active }

    /// 走行用に始める(準備画面の「接続」)。開発者モードで動いていれば走行用へ切り替える
    func startForRun() {
        isForRun = true
        requestedAt = Date()
        lastSampleAt = nil
        if reader == nil { connect() } else { reader?.setForRun(true) }
        statusTimer?.invalidate()
        statusTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refreshRunStatus() }
        }
        refreshRunStatus()
    }

    /// 見つからなかったときの「再接続」
    func retryForRun() {
        stop()
        startForRun()
    }

    /// 確かめるだけ(開発者モード)。走行用に動いていればそのまま
    func startForInspection() {
        guard reader == nil else { return }
        connect()
    }

    /// 開発者モードを閉じたとき。走行用に動いているなら止めない
    func stopInspection() {
        guard !isForRun else { return }
        stop()
    }

    /// 止める(走行の終了・準備画面の「戻る」・開発者モードのスイッチ)
    func stop() {
        reader?.stop()
        reader = nil
        isForRun = false
        requestedAt = nil
        lastSampleAt = nil
        statusTimer?.invalidate()
        statusTimer = nil
        status = .off
        runStatus = .off
    }

    private func connect() {
        status = .connecting
        snapshot = XrealImuSnapshot()
        let reader = XrealImuReader(forRun: isForRun)
        self.reader = reader
        reader.start(
            onState: { [weak self] state in
                Task { @MainActor in self?.handle(state, from: reader) }
            },
            onSnapshot: { [weak self] snapshot in
                Task { @MainActor in
                    guard let self, self.reader === reader else { return }
                    self.snapshot = snapshot
                    self.status = .streaming
                    self.lastSampleAt = Date()
                    if self.runStatus != .active && self.isForRun { self.refreshRunStatus() }
                }
            },
            onHeadYaw: { degrees in
                Task { @MainActor in UnityBridge.shared.updateHeadYaw(degrees: degrees) }
            })
    }

    private func handle(_ state: XrealImuReader.State, from reader: XrealImuReader) {
        guard self.reader === reader else { return }   // 止めた後に届いた古い通知
        switch state {
        case .ready:
            if status != .streaming { status = .connected }
        case .waiting(let message):
            status = .waiting(message)
        case .ended(let message):
            status = .failed(message)
            self.reader = nil
            lastSampleAt = nil
            // 走行中にグラスを挿し直した(§8.3)・USBネットワークが張り直された: 少し待って繋ぎ直す
            if isForRun {
                Task { @MainActor in
                    try? await Task.sleep(nanoseconds: 2_000_000_000)
                    if self.isForRun && self.reader == nil { self.connect() }
                }
            }
        }
        refreshRunStatus()
    }

    private func refreshRunStatus() {
        let now = Date()
        runStatus = .resolve(requested: isForRun && requestedAt != nil,
                             lastSampleAge: lastSampleAt.map { now.timeIntervalSince($0) },
                             secondsSinceRequest: requestedAt.map { now.timeIntervalSince($0) } ?? 0)
    }
}

/// 画面に出す値(約0.1秒ごとに更新)
nonisolated struct XrealImuSnapshot: Sendable {
    /// グラスの時計で測った IMU のレート(Hz)
    var rateHz: Double?
    /// 頭のヨーの角速度(度/秒・右を向くと正)。直近の約0.1秒の平均
    var yawRateDegreesPerSecond: Double?
    /// 積分した頭のヨー(度・右が正・基準は任意)
    var yawDegrees: Double?
    var temperatureCelsius: Float?
    var imuRecords = 0
    var otherRecords = 0
    /// 同期し直すために捨てたバイト(0 でなければ形式が想定とずれている)
    var skippedBytes = 0
    /// 走行ログへ渡した 100Hz の加速度の件数(Unity の受け口が無いビルドでは 0 のまま)
    var accelSamplesPushed = 0
}

/// 受信と解析。専用のキューの上だけで動く(1kHz をメインスレッドへ持ち込まない)
private nonisolated final class XrealImuReader: @unchecked Sendable {

    enum State: Sendable {
        case ready
        case waiting(String)
        case ended(String)
    }

    /// 画面へ出す間隔(秒)。1kHz をそのまま @Published に流すと SwiftUI が追いつかない
    private static let publishInterval: TimeInterval = 0.1

    /// Unity の iOS プラグイン(ARVisionSensorTiming.mm)の受け口。UnityFramework を直接リンクしない
    /// 開発ビルドでも落ちないよう、実行時に名前で引く。見つからなければ CSV へは渡さない
    private typealias PushGlassImu = @convention(c) (Double, Float, Float, Float) -> Void
    private static func lookupPush() -> PushGlassImu? {
        guard let symbol = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "ARV_PushGlassImuSample") else { return nil }
        return unsafeBitCast(symbol, to: PushGlassImu.self)
    }

    private let queue = DispatchQueue(label: "XrealImuReceiver")
    private let connection = NWConnection(host: NWEndpoint.Host(XrealImuStream.host),
                                          port: NWEndpoint.Port(rawValue: XrealImuStream.port)!,
                                          using: .tcp)

    // 以下は queue の上だけで触る
    private var forRun: Bool
    private var stream = XrealImuStream()
    private var meter = XrealImuRateMeter()
    private var fusion = XrealImuFusion()
    private var push: PushGlassImu?
    private var lastPushLookupUptime: TimeInterval = -.infinity
    private var accelSamplesPushed = 0
    private var yawSum = 0.0
    private var yawSamples = 0
    private var imuRecords = 0
    private var lastTemperature: Float?
    private var lastPublishUptime: TimeInterval = 0
    private var lastYawSentUptime: TimeInterval?
    private var onSnapshot: (@Sendable (XrealImuSnapshot) -> Void)?
    private var onHeadYaw: (@Sendable (Double) -> Void)?

    init(forRun: Bool) {
        self.forRun = forRun
    }

    func setForRun(_ value: Bool) {
        queue.async { self.forRun = value }
    }

    func start(onState: @escaping @Sendable (State) -> Void,
               onSnapshot: @escaping @Sendable (XrealImuSnapshot) -> Void,
               onHeadYaw: @escaping @Sendable (Double) -> Void) {
        queue.async {
            self.onSnapshot = onSnapshot
            self.onHeadYaw = onHeadYaw
        }
        connection.stateUpdateHandler = { state in
            switch state {
            case .ready: onState(.ready)
            case .waiting(let error): onState(.waiting(error.localizedDescription))
            case .failed(let error): onState(.ended(error.localizedDescription))
            default: break
            }
        }
        connection.start(queue: queue)
        receive(onState: onState)
    }

    func stop() {
        connection.cancel()
    }

    private func receive(onState: @escaping @Sendable (State) -> Void) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [self] data, _, isComplete, error in
            if let data, !data.isEmpty { consume([UInt8](data)) }
            if isComplete || error != nil {
                onState(.ended(error?.localizedDescription ?? "グラスが接続を閉じました"))
                return
            }
            receive(onState: onState)
        }
    }

    private func consume(_ bytes: [UInt8]) {
        let receivedAt = CACurrentMediaTime()
        let records = stream.append(bytes)
        if forRun { resolvePushIfNeeded() }
        for record in records {
            meter.add(record)
            yawSum += record.yawRateDegreesPerSecond
            yawSamples += 1
            if let accel = fusion.add(record, receivedAt: receivedAt), forRun, let push {
                push(accel.mediaTime, accel.x, accel.y, accel.z)
                accelSamplesPushed += 1
            }
        }
        imuRecords += records.count
        if let last = records.last { lastTemperature = last.temperatureCelsius }

        let now = ProcessInfo.processInfo.systemUptime
        if forRun, let yaw = fusion.yawDegrees, HeadYawSample.shouldSend(now: now, lastSent: lastYawSentUptime) {
            lastYawSentUptime = now
            onHeadYaw?(yaw)
        }

        guard now - lastPublishUptime >= Self.publishInterval, yawSamples > 0 else { return }
        lastPublishUptime = now
        onSnapshot?(XrealImuSnapshot(rateHz: meter.rateHz,
                                     yawRateDegreesPerSecond: yawSum / Double(yawSamples),
                                     yawDegrees: fusion.yawDegrees,
                                     temperatureCelsius: lastTemperature,
                                     imuRecords: imuRecords,
                                     otherRecords: stream.otherRecords,
                                     skippedBytes: stream.skippedBytes,
                                     accelSamplesPushed: accelSamplesPushed))
        yawSum = 0
        yawSamples = 0
    }

    /// Unity が後から起動することもあるので、見つかるまで1秒おきに引き直す
    private func resolvePushIfNeeded() {
        guard push == nil else { return }
        let now = ProcessInfo.processInfo.systemUptime
        guard now - lastPushLookupUptime >= 1 else { return }
        lastPushLookupUptime = now
        push = Self.lookupPush()
    }
}
