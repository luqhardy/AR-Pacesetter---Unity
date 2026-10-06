import SwiftUI
import UIKit
import Combine

// MARK: - 展示ブースの体験モード (Kobe Calling) — スタッフ画面
//
// 来場者は実物のARグラスを掛け、走らずに「走っているときの見え方」を体験する。
// アバター・HUD・色・GPSロストのフェードは Unity の本番実装そのもので、偽物なのは入力だけ
// (Unity BoothDemoController。詳細は Docs/KOBE_CALLING_DEMO.md)。
//
// この画面はスタッフが操作する。iPhone は体験中、来場者の**胸のマウント**に入る
// (グラスに映るのはiPhoneのカメラ視点のため)。だから:
//   - 開始は遅延つき — 押してからマウントへ入れる時間をとる
//   - 実行中の画面は遠目でも読める大きさ(区間・残り時間・中断)
//   - 体験中に画面が消えないよう自動ロックを止める
//
// 体験モードの走行は履歴・CSV・HealthKit に残らない(Unity が SessionEnded を送らない)。
// 走行用の ARSessionManager(CoreLocation / HealthKit)も起動しない。

struct BoothDemoView: View {
    let onExit: () -> Void

    @ObservedObject private var bridge = UnityBridge.shared
    @ObservedObject private var unity = UnityLauncher.shared
    @ObservedObject private var external = ExternalDisplayManager.shared

    private enum Phase: Equatable {
        case idle
        case countingDown(remaining: Int)   // 開始ボタン後、iPhoneをマウントへ入れる猶予
        case starting                       // 送信済み。グラスで 3-2-1-START 中
        case running
        case finished
    }

    @State private var phase: Phase = .idle
    @State private var mode: UnityBridge.BoothDemoMode = .standing
    @AppStorage("boothDemoStartDelaySeconds") private var startDelaySeconds = 10
    /// 猶予の終了時刻。残り秒はタイマーの発火回数ではなく時刻から出す(発火が遅れても伸びない)
    @State private var countdownDeadline: Date?
    @State private var showExitConfirm = false
    /// 待機画面に出す直前の結果(完走 / 中断 / 開始できなかった理由)
    @State private var resultMessage: String?

    /// 猶予のカウントダウン用。経過バーは TimelineView で描き、画面全体(Unityのビューを含む)を
    /// 毎回描き直さない
    private let tick = Timer.publish(every: 0.25, on: .main, in: .common).autoconnect()
    private let danger = Color(red: 0.85, green: 0.25, blue: 0.2)

    private var hasUnityView: Bool { unity.isRunning && unity.unityRootView != nil }

    var body: some View {
        ZStack {
            // ARビューはここに載せる。グラス接続中は ExternalDisplayManager がグラス側へ移す。
            // 未接続(iPhoneでのリハーサル)なら操作パネルの裏に見える
            UnityContainerView()
                .ignoresSafeArea()
            Color.black.opacity(external.isGlassesConnected ? 0.92 : 0.55)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                topBar
                statusChips
                    .padding(.top, 12)

                Spacer(minLength: 16)

                switch phase {
                case .idle, .finished:     setupPanel
                case .countingDown(let r): countdownPanel(remaining: r)
                case .starting:            startingPanel
                case .running:             runningPanel
                }

                Spacer(minLength: 16)
            }
            .padding(.horizontal, 24)
            .padding(.top, 56)
            .padding(.bottom, 36)

            if unity.isPreparing {
                // 初期化中はメインスレッドが塞がり何も動かないので、静止表示にする(RunningView と同じ)
                ZStack {
                    Color.black.opacity(0.92).ignoresSafeArea()
                    VStack(spacing: 12) {
                        Image(systemName: "arkit")
                            .font(.system(size: 44, weight: .light))
                            .foregroundColor(.arYellow)
                        Text("AR環境を準備しています")
                            .font(.system(size: 18, weight: .bold))
                            .foregroundColor(.white)
                    }
                }
            }
        }
        .preferredColorScheme(.dark)
        .onAppear {
            UnityLauncher.shared.prepare()
            // 来場者が体験している間に画面が消えるとグラスへの出力も止まる
            UIApplication.shared.isIdleTimerDisabled = true
        }
        .onDisappear {
            UIApplication.shared.isIdleTimerDisabled = false
        }
        .onReceive(tick) { date in
            advanceCountdown(now: date)
        }
        .onReceive(bridge.$boothDemoProgress) { progress in
            guard progress != nil else { return }
            if phase == .starting || phase == .running { phase = .running }
        }
        .onReceive(bridge.$lastBoothDemoEnd) { end in
            guard let end else { return }
            handleEnd(end)
        }
        .alert("体験モードを終了しますか？", isPresented: $showExitConfirm) {
            Button("終了", role: .destructive) { exit() }
            Button("続ける", role: .cancel) {}
        } message: {
            Text("体験中の場合は中断します。")
        }
    }

    // MARK: - 上部

    private var topBar: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 2) {
                ARLabel(text: "KOBE CALLING")
                Text("体験モード")
                    .font(.system(size: 26, weight: .bold))
                    .foregroundColor(.white)
            }
            Spacer()
            Button {
                if isActive { showExitConfirm = true } else { exit() }
            } label: {
                Text("終了")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(.white)
                    .padding(.horizontal, 18)
                    .frame(height: 40)
                    .background(Color.arCard, in: Capsule())
                    .overlay(Capsule().stroke(Color.arBorder, lineWidth: 1))
            }
        }
    }

    private var statusChips: some View {
        HStack(spacing: 8) {
            chip(icon: "eyeglasses",
                 text: external.isGlassesConnected ? "グラス接続中" : "グラス未接続(iPhoneに表示)",
                 ok: external.isGlassesConnected)
            if !hasUnityView && !unity.isPreparing {
                chip(icon: "exclamationmark.triangle", text: "Unity未起動", ok: false)
            }
            Spacer()
        }
    }

    private func chip(icon: String, text: String, ok: Bool) -> some View {
        HStack(spacing: 6) {
            Image(systemName: icon).font(.system(size: 11, weight: .bold))
            Text(text).font(.system(size: 12, weight: .semibold))
        }
        .foregroundColor(ok ? .black : .white)
        .padding(.horizontal, 10)
        .frame(height: 28)
        .background(ok ? Color.arYellow : Color.arCard, in: Capsule())
        .overlay(Capsule().stroke(ok ? .clear : Color.arBorder, lineWidth: 1))
    }

    // MARK: - 待機(モード選択・開始)

    private var setupPanel: some View {
        VStack(alignment: .leading, spacing: 18) {
            if let resultMessage {
                Text(resultMessage)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(.white)
                    .padding(14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.arCard, in: RoundedRectangle(cornerRadius: 14))
            }

            VStack(alignment: .leading, spacing: 8) {
                ARLabel(text: "モード")
                Picker("モード", selection: $mode) {
                    Text("立ったまま").tag(UnityBridge.BoothDemoMode.standing)
                    Text("数歩あるく").tag(UnityBridge.BoothDemoMode.walking)
                }
                .pickerStyle(.segmented)
                Text(modeDescription)
                    .font(.system(size: 13))
                    .foregroundColor(.arGrayText)
                    .fixedSize(horizontal: false, vertical: true)
            }

            VStack(alignment: .leading, spacing: 8) {
                ARLabel(text: "開始までの猶予")
                Picker("開始までの猶予", selection: $startDelaySeconds) {
                    Text("すぐ").tag(0)
                    Text("5秒").tag(5)
                    Text("10秒").tag(10)
                    Text("15秒").tag(15)
                }
                .pickerStyle(.segmented)
            }

            checklist

            Button { beginCountdown() } label: {
                HStack(spacing: 10) {
                    Image(systemName: "play.fill").font(.system(size: 18, weight: .bold))
                    Text(phase == .finished ? "次の方を開始" : "開始")
                        .font(.system(size: 20, weight: .bold))
                }
                .foregroundColor(.black)
                .frame(maxWidth: .infinity, minHeight: 72)
                .background(Color.arYellow, in: RoundedRectangle(cornerRadius: 18))
            }
        }
    }

    private var modeDescription: String {
        switch mode {
        case .standing:
            return "その場に立ったまま約70秒。遅れ・追い抜き・GPSロスト・ゴールを順に見せます。"
        case .walking:
            return "来場者が数歩あるくと、アバターが3m前を追従します。60秒で終了。"
        }
    }

    private var checklist: some View {
        VStack(alignment: .leading, spacing: 6) {
            checkRow("iPhoneは胸のマウントへ(カメラを前向き)")
            checkRow("ブースの開けた方向を向いて立つ")
            if mode == .walking {
                checkRow("歩く先2〜3mに人や物が無いこと", warning: true)
            }
        }
    }

    private func checkRow(_ text: String, warning: Bool = false) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: warning ? "exclamationmark.triangle.fill" : "checkmark.circle")
                .font(.system(size: 13))
                .foregroundColor(warning ? .orange : .arGrayText)
            Text(text)
                .font(.system(size: 13))
                .foregroundColor(warning ? .white : .arGrayText)
        }
    }

    // MARK: - 開始までの猶予

    private func countdownPanel(remaining: Int) -> some View {
        VStack(spacing: 16) {
            Text("\(remaining)")
                .font(.system(size: 120, weight: .black, design: .rounded))
                .foregroundColor(.arYellow)
                .monospacedDigit()
            Text("iPhoneを胸のマウントへ入れてください")
                .font(.system(size: 17, weight: .semibold))
                .foregroundColor(.white)
                .multilineTextAlignment(.center)
            cancelButton(title: "取り消す") { phase = .idle }
        }
        .frame(maxWidth: .infinity)
    }

    private var startingPanel: some View {
        VStack(spacing: 14) {
            Text("グラスでカウントダウン中")
                .font(.system(size: 22, weight: .bold))
                .foregroundColor(.white)
            Text(mode == .standing ? "3・2・1・START のあと台本が始まります"
                                   : "3・2・1・START のあと歩き始めてください")
                .font(.system(size: 14))
                .foregroundColor(.arGrayText)
            cancelButton(title: "中断") { stop() }
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: - 実行中

    private var runningPanel: some View {
        let progress = bridge.boothDemoProgress
        let beat = BoothBeat(rawValue: progress?.beat ?? "") ?? .onPace
        let total = max(progress?.totalSeconds ?? 0, 1)

        return VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 6) {
                ARLabel(text: "いまの区間")
                Text(beat.title)
                    .font(.system(size: 34, weight: .black))
                    .foregroundColor(beat.color)
                Text(beat.narration)
                    .font(.system(size: 16, weight: .medium))
                    .foregroundColor(.white)
                    .fixedSize(horizontal: false, vertical: true)
            }

            TimelineView(.periodic(from: .now, by: 0.25)) { context in
                let elapsed = progress?.elapsed(at: context.date) ?? 0
                let remaining = max(0, Int((total - elapsed).rounded(.up)))
                VStack(alignment: .leading, spacing: 6) {
                    ProgressView(value: elapsed, total: total)
                        .tint(.arYellow)
                    Text("残り \(remaining) 秒")
                        .font(.system(size: 14, weight: .semibold, design: .monospaced))
                        .foregroundColor(.arGrayText)
                }
            }

            if mode == .standing {
                beatList(current: beat)
            }

            cancelButton(title: "中断", prominent: true) { stop() }
        }
    }

    private func beatList(current: BoothBeat) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(BoothBeat.standingOrder, id: \.self) { b in
                let state = b.order < current.order ? "done" : (b == current ? "now" : "next")
                HStack(spacing: 10) {
                    Image(systemName: state == "done" ? "checkmark.circle.fill"
                                      : (state == "now" ? "largecircle.fill.circle" : "circle"))
                        .font(.system(size: 13))
                        .foregroundColor(state == "next" ? .arBorder : b.color)
                    Text(b.title)
                        .font(.system(size: 14, weight: state == "now" ? .bold : .regular))
                        .foregroundColor(state == "next" ? .arGrayText : .white)
                }
            }
        }
    }

    private func cancelButton(title: String, prominent: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: "stop.fill").font(.system(size: 16, weight: .bold))
                Text(title).font(.system(size: 18, weight: .bold))
            }
            .foregroundColor(.white)
            .frame(maxWidth: .infinity, minHeight: prominent ? 72 : 56)
            .background(danger, in: RoundedRectangle(cornerRadius: 18))
        }
    }

    // MARK: - 操作

    private var isActive: Bool {
        switch phase {
        case .countingDown, .starting, .running: return true
        case .idle, .finished: return false
        }
    }

    private func beginCountdown() {
        resultMessage = nil
        if startDelaySeconds <= 0 {
            sendStart()
        } else {
            phase = .countingDown(remaining: startDelaySeconds)
            countdownDeadline = Date().addingTimeInterval(TimeInterval(startDelaySeconds))
        }
    }

    private func advanceCountdown(now: Date) {
        guard case .countingDown(let shown) = phase, let deadline = countdownDeadline else { return }
        let remaining = Int(deadline.timeIntervalSince(now).rounded(.up))
        if remaining <= 0 {
            countdownDeadline = nil
            sendStart()
        } else if remaining != shown {
            phase = .countingDown(remaining: remaining)
        }
    }

    private func sendStart() {
        phase = .starting
        bridge.startBoothDemo(mode: mode)
    }

    private func stop() {
        // 猶予中はまだ Unity へ何も送っていない
        if phase == .starting || phase == .running {
            bridge.stopBoothDemo()
        }
        countdownDeadline = nil
        phase = .idle
        resultMessage = "中断しました。"
    }

    private func handleEnd(_ end: UnityBridge.BoothDemoEnd) {
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

    private func exit() {
        if isActive { bridge.stopBoothDemo() }
        UnityLauncher.shared.pause()
        onExit()
    }
}

// MARK: - 区間(Unity BoothDemoScript.Beat と同じ名前)

private enum BoothBeat: String, CaseIterable {
    case onPace = "OnPace"
    case fallingBehind = "FallingBehind"
    case catchingUp = "CatchingUp"
    case overtaking = "Overtaking"
    case settling = "Settling"
    case gpsLost = "GpsLost"
    case gpsRecovering = "GpsRecovering"
    case finalStretch = "FinalStretch"
    case finished = "Finished"
    case walking = "Walking"

    static let standingOrder: [BoothBeat] = [
        .onPace, .fallingBehind, .catchingUp, .overtaking, .settling,
        .gpsLost, .gpsRecovering, .finalStretch
    ]

    var order: Int { BoothBeat.allCases.firstIndex(of: self) ?? 0 }

    var title: String {
        switch self {
        case .onPace:        return "ジャスト"
        case .fallingBehind: return "遅れ"
        case .catchingUp:    return "追い上げ"
        case .overtaking:    return "追い抜き"
        case .settling:      return "ジャストへ"
        case .gpsLost:       return "GPSロスト"
        case .gpsRecovering: return "GPS復帰"
        case .finalStretch:  return "ゴール前"
        case .finished:      return "ゴール"
        case .walking:       return "歩行体験"
        }
    }

    /// スタッフが来場者に声をかける一言(グラスで今見えているもの)
    var narration: String {
        switch self {
        case .onPace:        return "3m先を緑のアバターが走っています。目標ペースどおりの状態です。"
        case .fallingBehind: return "ペースが落ちた想定です。アバターが離れて赤くなり、足元から光のラインが伸びます。右上のペースも赤に。"
        case .catchingUp:    return "追い上げてジャストへ戻ります。"
        case .overtaking:    return "追い抜きかけている想定です。アバターが近づき、青みがかって脚が速まります。"
        case .settling:      return "ジャストの位置へ戻ります。"
        case .gpsLost:       return "GPSが途切れた想定です。5秒は惰性で走り、その後フェードアウト。下に赤字の警告が出ます。"
        case .gpsRecovering: return "GPSが戻りました。光の粒が集まってアバターが戻ってきます。"
        case .finalStretch:  return "最後の直線です。前方にゴールラインが見えてきます。"
        case .finished:      return "ゴールです。"
        case .walking:       return "ゆっくり歩いてみてください。アバターが3m前を追従します。向きを変えるとアバターも曲がります。"
        }
    }

    var color: Color {
        switch self {
        case .fallingBehind: return Color(red: 1.0, green: 0.35, blue: 0.25)
        case .overtaking:    return Color(red: 0.35, green: 0.65, blue: 1.0)
        case .gpsLost:       return .orange
        default:             return .arYellow
        }
    }
}

#Preview {
    BoothDemoView(onExit: {})
}
