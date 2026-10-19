import Foundation

// MARK: - AirPods による頭の向き(タスク表 ARグラス「首を振り向けた時に想定する場所を走る」)
//
// グラスは Follow(固定)なので、首を振ると画面ごと顔に付いて回り、アバターも視線の先へ付いてくる。
// グラスの姿勢は iOS から取れない(XREAL SDK は Android 専用)が、AirPods(Pro・第3世代以降・Max)の
// 頭の向きは CMHeadphoneMotionManager で取れる。頭のヨーを Unity へ送り、Unity が進行方向との差を
// 首振りとして描画の向きへ足す(HeadPoseMath / HeadYawBaseline)。
//
// AirPods は第1期のスコープ外の機器なので**既定では使わない**。準備画面(S4)の AirPods 行で
// 「接続」を押したときだけ動かす(AGENTS.md §6「スコープ外のセンサーは既定OFF」)。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

/// 準備画面の AirPods 行の状態
nonisolated enum HeadTrackingStatus: Equatable {
    /// 使っていない(既定)
    case off
    /// 頭の向きを待っている(押した直後)
    case searching
    /// 頭の向きが届いている
    case active
    /// 待っても届かず、Bluetooth のイヤホンも音の出力先に無い
    /// (AirPods がつながっていない・耳に着けていない・Bluetooth がオフ・機内モード)
    case notFound
    /// Bluetooth のイヤホンはつながっているのに頭の向きが届かない(頭の向きに対応していない機種)
    case noHeadMotion
    /// この iPhone が頭の向きの取得に対応していない
    case unsupported
    /// モーションの許可が無い(設定アプリで許可する)
    case denied

    /// 押した後、これだけ待っても届かなければ見つからない扱いにする
    static let searchTimeoutSeconds: TimeInterval = 8

    /// 頭の向きが途切れたとみなす秒数(AirPods を外した・接続が切れた)
    static let staleSeconds: TimeInterval = 2

    /// - Parameter bluetoothAudioConnected: 音の出力先に Bluetooth のイヤホンがあるか(分からなければ nil)。
    ///   Bluetooth の許可を求めずに「つながっていない」と「つながっているが頭の向きが来ない」を分けるために使う
    ///   (タスク表 S4「Bluetooth が OFF」「機内モード」 — Bluetooth の状態そのものは許可ダイアログ無しには読めない)
    static func resolve(requested: Bool, supported: Bool, authorized: Bool,
                        lastSampleAge: TimeInterval?, secondsSinceRequest: TimeInterval,
                        bluetoothAudioConnected: Bool? = nil) -> HeadTrackingStatus {
        guard requested else { return .off }
        guard supported else { return .unsupported }
        guard authorized else { return .denied }
        if let age = lastSampleAge, age <= staleSeconds { return .active }
        if secondsSinceRequest < searchTimeoutSeconds && lastSampleAge == nil { return .searching }
        return bluetoothAudioConnected == true ? .noHeadMotion : .notFound
    }

    /// 任意の機器なので、失敗しても先へ進める(タスク表 S4「Apple Watch・AirPods の失敗はエラー表示はするが進める」)
    var isError: Bool { self == .notFound || self == .noHeadMotion || self == .unsupported || self == .denied }

    /// 使っていたのに頭の向きが届かなくなった(走行中の警告に使う)
    var isLost: Bool { self == .notFound || self == .noHeadMotion }
}

/// CoreMotion の姿勢から Unity へ送る頭のヨーを作る
nonisolated enum HeadYawSample {
    /// Unity へ送る間隔(秒)。グラスは60fpsだが、頭の向きは30Hzで足りる(Unity は毎フレーム最新値を使う)
    static let sendInterval: TimeInterval = 1.0 / 30.0
    private static let sendTolerance: TimeInterval = 0.001

    /// CoreMotion のヨー(ラジアン・上から見て反時計回りが正)を Unity のヨー(度・右回りが正)へ。
    /// 基準(0°の向き)は AirPods 任せで任意 — Unity が進行方向との差の平均を「まっすぐ前」とする
    static func unityYawDegrees(attitudeYawRadians yaw: Double) -> Double? {
        guard yaw.isFinite else { return nil }
        return -yaw * 180.0 / .pi
    }

    /// 前回送ってから間隔が空いたか
    static func shouldSend(now: TimeInterval, lastSent: TimeInterval?) -> Bool {
        guard let lastSent else { return true }
        // 1ms の余裕: 浮動小数の誤差でちょうど間隔ぶんの差が僅かに足りず、60Hz の受信が
        // 30Hz ではなく 20Hz(3回に1回)でしか送られなくなるのを防ぐ
        return now - lastSent >= sendInterval - sendTolerance || now < lastSent
    }
}
