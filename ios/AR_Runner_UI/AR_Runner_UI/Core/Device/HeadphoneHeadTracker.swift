import Foundation
import Combine
import CoreMotion

/// AirPods(Pro・第3世代以降・Max)の頭の向きを Unity へ送る
/// (タスク表 ARグラス「首を振り向けた時に想定する場所を走る」— 仕組みは AppLogic/HeadTracking.swift)。
///
/// **既定では動かさない**。準備画面(S4)の AirPods 行で「接続」を押したときだけ始め、
/// 走行の終了・準備画面の「戻る」で止める。状態の判定は `HeadTrackingStatus`(テスト対象)。
final class HeadphoneHeadTracker: NSObject, ObservableObject, CMHeadphoneMotionManagerDelegate {

    static let shared = HeadphoneHeadTracker()

    @Published private(set) var status: HeadTrackingStatus = .off

    private let manager = CMHeadphoneMotionManager()
    private var requestedAt: Date?
    private var lastSampleAt: Date?
    private var lastSentUptime: TimeInterval?
    private var statusTimer: Timer?

    private override init() {
        super.init()
        manager.delegate = self
    }

    /// 「接続」を押して動いているか(見つかっていなくても true)
    var isRequested: Bool { requestedAt != nil }

    /// 頭の向きの受け取りを始める。動いている間に押されても二重には始めない(連打対策)
    func start() {
        guard requestedAt == nil else { refresh(); return }
        requestedAt = Date()
        lastSampleAt = nil
        lastSentUptime = nil

        if manager.isDeviceMotionAvailable && isAuthorized {
            // 許可がまだなら、ここで iOS がモーションの許可を尋ねる
            manager.startDeviceMotionUpdates(to: .main) { [weak self] motion, _ in
                guard let self, let motion, self.requestedAt != nil else { return }
                self.lastSampleAt = Date()
                let now = ProcessInfo.processInfo.systemUptime
                if HeadYawSample.shouldSend(now: now, lastSent: self.lastSentUptime),
                   let yaw = HeadYawSample.unityYawDegrees(attitudeYawRadians: motion.attitude.yaw) {
                    self.lastSentUptime = now
                    UnityBridge.shared.updateHeadYaw(degrees: yaw)
                }
                if self.status != .active { self.refresh() }
            }
        }

        // 届かない(タイムアウト)・途切れた(外した)を拾うため、受信とは別に状態を見直す
        statusTimer?.invalidate()
        statusTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            self?.refresh()
        }
        refresh()
    }

    /// 止める(走行の終了・準備画面の「戻る」)。待っている途中でも安全に止まる
    func stop() {
        manager.stopDeviceMotionUpdates()
        statusTimer?.invalidate()
        statusTimer = nil
        requestedAt = nil
        lastSampleAt = nil
        lastSentUptime = nil
        status = .off
    }

    /// 見つからなかったときの「再接続」
    func retry() {
        stop()
        start()
    }

    /// アプリへ戻ったとき(電話・他アプリの後)に今の状態を取り直す
    func refreshAfterForeground() {
        refresh()
    }

    private var isAuthorized: Bool {
        switch CMHeadphoneMotionManager.authorizationStatus() {
        case .denied, .restricted: return false
        default: return true   // 未決定は開始時に iOS が尋ねる
        }
    }

    private func refresh() {
        let now = Date()
        status = .resolve(requested: requestedAt != nil,
                          supported: manager.isDeviceMotionAvailable,
                          authorized: isAuthorized,
                          lastSampleAge: lastSampleAt.map { now.timeIntervalSince($0) },
                          secondsSinceRequest: requestedAt.map { now.timeIntervalSince($0) } ?? 0)
    }

    // MARK: CMHeadphoneMotionManagerDelegate

    func headphoneMotionManagerDidConnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async { self.refresh() }
    }

    func headphoneMotionManagerDidDisconnect(_ manager: CMHeadphoneMotionManager) {
        DispatchQueue.main.async {
            self.lastSampleAt = nil
            self.refresh()
        }
    }
}
