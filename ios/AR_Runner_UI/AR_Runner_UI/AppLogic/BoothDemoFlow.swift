import Foundation

// MARK: - 体験モード(展示ブース)のスタッフ画面の進行
//
// BoothDemoView の状態遷移だけを取り出した純ロジック(ios/AppLogicTests で `swift test` できる)。
// 画面は Unity への送信を自分では決めず、ここが返す `Effect` を実行するだけにする。
//
// ここで縛っている、実際に踏んだ/踏みうる不具合:
//   - 画面を開いた瞬間に届く「前回の終了」を今回の結果として出す(購読開始時の再送)
//   - 中断 → すぐ開始 のとき、前の体験の中断応答が新しい開始の後に届いて新しい体験を「終了」扱いにする
//   - 猶予中(まだ Unity へ何も送っていない)の取り消しで、不要な StopBoothDemo を送る

nonisolated struct BoothDemoFlow: Equatable {

    enum Phase: Equatable {
        case idle
        case countingDown(remaining: Int)   // 開始ボタン後、iPhoneを胸のマウントへ入れる猶予
        case starting                       // 送信済み。グラスで 3-2-1-START 中
        case running
        case finished
    }

    /// 画面が実行すべき Unity への送信
    enum Effect: Equatable {
        case sendStart
        case sendStop
    }

    /// Unity の BoothDemoEnded
    struct End: Equatable {
        let completed: Bool
        let reason: String
        var wasStoppedByStaff: Bool { !completed && reason == "stopped" }
    }

    private(set) var phase: Phase = .idle
    /// 待機画面に出す直前の結果(完走 / 中断 / 開始できなかった理由)
    private(set) var resultMessage: String?
    /// 猶予の終了時刻。残り秒はタイマーの発火回数ではなく時刻から出す(発火が遅れても伸びない)
    private(set) var countdownDeadline: Date?

    /// Unity へ送った体験が進行中か(猶予中を含む)
    var isActive: Bool {
        switch phase {
        case .countingDown, .starting, .running: return true
        case .idle, .finished: return false
        }
    }

    /// 開始ボタン。猶予0ならすぐ送る
    mutating func begin(delaySeconds: Int, now: Date) -> Effect? {
        resultMessage = nil
        if delaySeconds <= 0 {
            phase = .starting
            countdownDeadline = nil
            return .sendStart
        }
        phase = .countingDown(remaining: delaySeconds)
        countdownDeadline = now.addingTimeInterval(TimeInterval(delaySeconds))
        return nil
    }

    /// 猶予の時計(画面のタイマーから呼ぶ)
    mutating func tick(now: Date) -> Effect? {
        guard case .countingDown(let shown) = phase, let deadline = countdownDeadline else { return nil }
        let remaining = Int(deadline.timeIntervalSince(now).rounded(.up))
        if remaining <= 0 {
            countdownDeadline = nil
            phase = .starting
            return .sendStart
        }
        if remaining != shown { phase = .countingDown(remaining: remaining) }
        return nil
    }

    /// Unity の BoothDemoProgress が届いた
    mutating func progressReceived() {
        if phase == .starting || phase == .running { phase = .running }
    }

    /// Unity の BoothDemoEnded が届いた
    mutating func endReceived(_ end: End) {
        // 意味があるのは「送った体験」の終了だけ。購読開始時に届く前回の値や、
        // stop() 後の中断応答(表示済み)は無視する
        guard phase == .starting || phase == .running else { return }
        // 直前の体験の中断応答が、新しい開始の後に届いた。新しい体験は続いている
        if phase == .starting && end.wasStoppedByStaff { return }

        phase = .finished
        if end.completed {
            resultMessage = "完走しました。グラスを外して、次の方へ。"
        } else if end.wasStoppedByStaff {
            resultMessage = "中断しました。"
        } else {
            resultMessage = "開始できませんでした: \(end.reason)"
        }
    }

    /// 中断・取り消し。猶予中はまだ Unity へ何も送っていないので Stop は送らない
    mutating func stop() -> Effect? {
        let wasSent = phase == .starting || phase == .running
        countdownDeadline = nil
        phase = .idle
        resultMessage = "中断しました。"
        return wasSent ? .sendStop : nil
    }
}
