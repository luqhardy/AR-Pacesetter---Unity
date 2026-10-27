import Foundation

// MARK: - XREAL One のIMUストリーム(開発者モードの試験用プローブ)
//
// XREAL One は USB-C の上に映像(DisplayPort Alt Mode)と一緒に USBネットワーク(CDC-NCM)を出していて、
// TCP 169.254.2.1:52998 で生のIMUを流している(非公式・コミュニティの解析)。Air 系の USB-HID と違い、
// iOS アプリからも普通のソケットで届く可能性がある — それを実機で確かめるためのもの。
//
// 形式(2026-10-27 に base One を Mac / Windows で取得して解析):
//   134バイト固定のレコード・リトルエンディアン。先頭はマジック 28 36 00 00 00 80
//   0x0E u64  タイムスタンプ(ナノ秒)
//   0x1E u32  種別: 11 = IMU(1000Hz) / 4 = 地磁気(400Hz)
//   0x22 f32×3 ジャイロ rad/s
//   0x2E f32×3 加速度 m/s²(重力の反力 — 正立で y が約 −9.8)
//   0x46 f32  温度 °C(IMU のレコードだけ)
// 軸は右手系 x=右・y=下・z=前。右を向くと gyro y が正(Unity のヨーと同じ向き)。
//
// **読むだけ**。52999 は設定(表示モード・明るさ・較正)の口なので触らない。
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// IMU の1レコード(種別 11)
nonisolated struct XrealImuRecord: Equatable {
    let timestampNanoseconds: UInt64
    /// rad/s(x=右・y=下・z=前、右手系)
    let gyro: (x: Float, y: Float, z: Float)
    /// m/s²
    let accel: (x: Float, y: Float, z: Float)
    let temperatureCelsius: Float

    /// 頭のヨーの角速度(度/秒・右を向くと正 = Unity のヨーと同じ向き)
    var yawRateDegreesPerSecond: Double { Double(gyro.y) * 180.0 / .pi }

    static func == (a: XrealImuRecord, b: XrealImuRecord) -> Bool {
        a.timestampNanoseconds == b.timestampNanoseconds
            && a.gyro == b.gyro && a.accel == b.accel && a.temperatureCelsius == b.temperatureCelsius
    }
}

/// TCP で細切れに届くバイト列をレコードへ切り分ける。途中から繋いだ・欠けたときはマジックで同期し直す
nonisolated struct XrealImuStream {
    static let recordSize = 134
    static let magic: [UInt8] = [0x28, 0x36, 0x00, 0x00, 0x00, 0x80]
    static let imuType: UInt32 = 11
    static let host = "169.254.2.1"
    static let port: UInt16 = 52998

    private var buffer: [UInt8] = []
    /// 同期し直すために捨てたバイト数(形式が想定と違うときの手がかり)
    private(set) var skippedBytes = 0
    /// IMU 以外(地磁気など)のレコード数
    private(set) var otherRecords = 0

    init() {}

    /// 届いたバイトを足し、切り出せた IMU レコードを返す(地磁気などは数えるだけ)
    mutating func append(_ bytes: [UInt8]) -> [XrealImuRecord] {
        buffer.append(contentsOf: bytes)
        var records: [XrealImuRecord] = []
        var offset = 0
        while buffer.count - offset >= Self.recordSize {
            guard hasMagic(at: offset) else {
                offset += 1
                skippedBytes += 1
                continue
            }
            let type = readUInt32(offset + 0x1E)
            if type == Self.imuType {
                records.append(XrealImuRecord(
                    timestampNanoseconds: readUInt64(offset + 0x0E),
                    gyro: (readFloat(offset + 0x22), readFloat(offset + 0x26), readFloat(offset + 0x2A)),
                    accel: (readFloat(offset + 0x2E), readFloat(offset + 0x32), readFloat(offset + 0x36)),
                    temperatureCelsius: readFloat(offset + 0x46)))
            } else {
                otherRecords += 1
            }
            offset += Self.recordSize
        }
        // 最後のレコードに足りない残りは次に届くバイトと繋ぐ
        buffer.removeFirst(offset)
        return records
    }

    private func hasMagic(at offset: Int) -> Bool {
        for i in 0..<Self.magic.count where buffer[offset + i] != Self.magic[i] { return false }
        return true
    }

    private func readUInt32(_ o: Int) -> UInt32 {
        (0..<4).reduce(UInt32(0)) { $0 | UInt32(buffer[o + $1]) << (8 * UInt32($1)) }
    }

    private func readUInt64(_ o: Int) -> UInt64 {
        (0..<8).reduce(UInt64(0)) { $0 | UInt64(buffer[o + $1]) << (8 * UInt64($1)) }
    }

    private func readFloat(_ o: Int) -> Float { Float(bitPattern: readUInt32(o)) }
}

/// 画面に出す集計。レコードのタイムスタンプ(グラスの時計)から受信レートを測る —
/// iPhone 側の受信時刻はTCPのまとめ届きで揺れるため使わない
nonisolated struct XrealImuRateMeter {
    /// レートを測る窓(秒)
    static let windowSeconds = 1.0

    private var windowStart: UInt64?
    private var windowCount = 0
    /// 直近の窓で測った IMU のレート(Hz)。まだ1窓たまっていなければ nil
    private(set) var rateHz: Double?

    init() {}

    mutating func add(_ record: XrealImuRecord) {
        guard let start = windowStart, record.timestampNanoseconds >= start else {
            // 最初の1件・グラスの時計が戻った(再起動)ときは測り直す
            windowStart = record.timestampNanoseconds
            windowCount = 0
            return
        }
        windowCount += 1
        let elapsed = Double(record.timestampNanoseconds - start) / 1e9
        if elapsed >= Self.windowSeconds {
            rateHz = Double(windowCount) / elapsed
            windowStart = record.timestampNanoseconds
            windowCount = 0
        }
    }
}
