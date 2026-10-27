import XCTest
@testable import ARRunnerAppLogic

final class XrealImuStreamTests: XCTestCase {

    /// base XREAL One から実際に取れた先頭レコード(2026-10-27・Mac で取得・机の上で静止)
    private static let realImuRecordHex =
        "283600000080b880730000000000a73b8a6325000000b3dd0000000000000b0000007eb6c83b8470513b8fe4e23b"
        + "0050223f00ca0e4166d78b40000048c5000048c5000048c500c00742023c690200401f00004000000000000000"
        + "00000000010000000000000000000000000000200f000c7f000000cff753e3a59b0000db34b6d782de1b43"

    private static func bytes(hex: String) -> [UInt8] {
        var out: [UInt8] = []
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            out.append(UInt8(hex[index..<next], radix: 16)!)
            index = next
        }
        return out
    }

    /// 種別・タイムスタンプ・ジャイロ y だけ差し替えたレコードを作る
    private static func record(type: UInt32 = 11, timestampNs: UInt64 = 0, gyroY: Float = 0) -> [UInt8] {
        var r = bytes(hex: realImuRecordHex)
        withUnsafeBytes(of: timestampNs.littleEndian) { r.replaceSubrange(0x0E..<0x16, with: $0) }
        withUnsafeBytes(of: type.littleEndian) { r.replaceSubrange(0x1E..<0x22, with: $0) }
        withUnsafeBytes(of: gyroY.bitPattern.littleEndian) { r.replaceSubrange(0x26..<0x2A, with: $0) }
        return r
    }

    func testRealRecordDecodes() {
        let raw = Self.bytes(hex: Self.realImuRecordHex)
        XCTAssertEqual(raw.count, XrealImuStream.recordSize)
        var stream = XrealImuStream()
        let records = stream.append(raw)
        XCTAssertEqual(records.count, 1)
        let r = records[0]
        XCTAssertEqual(r.timestampNanoseconds, 0x25_638A_3BA7)
        XCTAssertEqual(r.gyro.x, 0.0061, accuracy: 0.0001)
        XCTAssertEqual(r.accel.y, 8.924, accuracy: 0.001)
        XCTAssertEqual(r.temperatureCelsius, 33.94, accuracy: 0.01)
        // 静止中なので重力ぶんだけ
        let g = sqrt(r.accel.x * r.accel.x + r.accel.y * r.accel.y + r.accel.z * r.accel.z)
        XCTAssertEqual(g, 9.8, accuracy: 0.3)
    }

    func testYawRateIsGyroYInDegrees() {
        var stream = XrealImuStream()
        let r = stream.append(Self.record(gyroY: .pi / 2))[0]
        XCTAssertEqual(r.yawRateDegreesPerSecond, 90, accuracy: 0.001)   // 右を向くと正
    }

    func testMagnetometerRecordsAreCountedNotReturned() {
        var stream = XrealImuStream()
        let records = stream.append(Self.record(type: 4) + Self.record(type: 11))
        XCTAssertEqual(records.count, 1)
        XCTAssertEqual(stream.otherRecords, 1)
    }

    func testRecordSplitAcrossChunksIsJoined() {
        var stream = XrealImuStream()
        let raw = Self.record(timestampNs: 42)
        XCTAssertTrue(stream.append(Array(raw[..<50])).isEmpty)
        let records = stream.append(Array(raw[50...]))
        XCTAssertEqual(records.map(\.timestampNanoseconds), [42])
    }

    func testResyncsOnMagicAfterGarbage() {
        var stream = XrealImuStream()
        // 途中から繋いだ(前のレコードの尻尾7バイト)
        let records = stream.append([1, 2, 3, 4, 5, 6, 7] + Self.record(timestampNs: 1) + Self.record(timestampNs: 2))
        XCTAssertEqual(records.map(\.timestampNanoseconds), [1, 2])
        XCTAssertEqual(stream.skippedBytes, 7)
    }

    func testRateMeterMeasuresGlassClock() {
        var stream = XrealImuStream()
        var meter = XrealImuRateMeter()
        var raw: [UInt8] = []
        for i in 0..<1_500 { raw += Self.record(timestampNs: UInt64(i) * 1_000_000) }   // 1kHz
        XCTAssertNil(meter.rateHz)
        stream.append(raw).forEach { meter.add($0) }
        XCTAssertEqual(meter.rateHz ?? 0, 1000, accuracy: 1)
    }

    func testRateMeterRestartsWhenGlassClockGoesBack() {
        var meter = XrealImuRateMeter()
        var stream = XrealImuStream()
        var raw: [UInt8] = []
        for i in 0..<1_100 { raw += Self.record(timestampNs: 5_000_000_000 + UInt64(i) * 1_000_000) }
        for i in 0..<500 { raw += Self.record(timestampNs: UInt64(i) * 2_000_000) }   // グラスを挿し直した
        stream.append(raw).forEach { meter.add($0) }
        // 戻った後は 1窓たまっていないので、前の窓のレートのまま(マイナスや巨大値にならない)
        XCTAssertEqual(meter.rateHz ?? 0, 1000, accuracy: 1)
    }
}
