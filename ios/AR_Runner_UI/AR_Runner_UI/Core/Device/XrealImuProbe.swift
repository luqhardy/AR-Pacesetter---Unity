import Foundation
import Combine
import Network

/// XREAL One のIMU(TCP 169.254.2.1:52998)を iPhone から読めるか確かめる開発者モードの試験用プローブ
/// (形式と軸は AppLogic/XrealImuStream.swift)。
///
/// **既定では動かさない**。開発者モードのスイッチを入れている間だけ繋ぎ、画面を閉じたら切る。
/// **読むだけ** — グラスへは1バイトも送らない。値は画面に出すだけで、Unity へはまだ流さない。
final class XrealImuProbe: ObservableObject {

    static let shared = XrealImuProbe()

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

    private var reader: XrealImuReader?

    private init() {}

    var isRunning: Bool { reader != nil }

    func start() {
        guard reader == nil else { return }
        status = .connecting
        snapshot = XrealImuSnapshot()
        let reader = XrealImuReader()
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
                }
            })
    }

    func stop() {
        reader?.stop()
        reader = nil
        status = .off
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
        }
    }
}

/// 画面に出す値(約0.1秒ごとに更新)
nonisolated struct XrealImuSnapshot: Sendable {
    /// グラスの時計で測った IMU のレート(Hz)
    var rateHz: Double?
    /// 頭のヨーの角速度(度/秒・右を向くと正)。直近の約0.1秒の平均
    var yawRateDegreesPerSecond: Double?
    var temperatureCelsius: Float?
    var imuRecords = 0
    var otherRecords = 0
    /// 同期し直すために捨てたバイト(0 でなければ形式が想定とずれている)
    var skippedBytes = 0
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

    private let queue = DispatchQueue(label: "XrealImuProbe")
    private let connection = NWConnection(host: NWEndpoint.Host(XrealImuStream.host),
                                          port: NWEndpoint.Port(rawValue: XrealImuStream.port)!,
                                          using: .tcp)

    // 以下は queue の上だけで触る
    private var stream = XrealImuStream()
    private var meter = XrealImuRateMeter()
    private var yawSum = 0.0
    private var yawSamples = 0
    private var imuRecords = 0
    private var lastTemperature: Float?
    private var lastPublishUptime: TimeInterval = 0
    private var onSnapshot: (@Sendable (XrealImuSnapshot) -> Void)?

    func start(onState: @escaping @Sendable (State) -> Void,
               onSnapshot: @escaping @Sendable (XrealImuSnapshot) -> Void) {
        queue.async { self.onSnapshot = onSnapshot }
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
        let records = stream.append(bytes)
        for record in records {
            meter.add(record)
            yawSum += record.yawRateDegreesPerSecond
            yawSamples += 1
        }
        imuRecords += records.count
        if let last = records.last { lastTemperature = last.temperatureCelsius }

        let now = ProcessInfo.processInfo.systemUptime
        guard now - lastPublishUptime >= Self.publishInterval, yawSamples > 0 else { return }
        lastPublishUptime = now
        onSnapshot?(XrealImuSnapshot(rateHz: meter.rateHz,
                                     yawRateDegreesPerSecond: yawSum / Double(yawSamples),
                                     temperatureCelsius: lastTemperature,
                                     imuRecords: imuRecords,
                                     otherRecords: stream.otherRecords,
                                     skippedBytes: stream.skippedBytes))
        yawSum = 0
        yawSamples = 0
    }
}
