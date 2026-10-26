import SwiftUI
import UIKit
import Combine

// MARK: - 展示ブースの体験モード (Kobe Calling) — スタッフ画面
//
// 来場者は実物のARグラスを掛け、走らずに「走っているときの見え方」を体験する。
// アバター・HUD・色・オーラは Unity の本番実装そのもので、偽物なのは入力だけ。アバターは体験中に消さない
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

    /// 進行の状態遷移は AppLogic/BoothDemoFlow.swift(`swift test` で検証)。
    /// 画面は Flow が返す Effect(Unity への送信)を実行するだけ
    @State private var flow = BoothDemoFlow()
    private var phase: BoothDemoFlow.Phase { flow.phase }
    @State private var mode: UnityBridge.BoothDemoMode = .standing
    @AppStorage("boothDemoStartDelaySeconds") private var startDelaySeconds = 10
    @State private var showExitConfirm = false

    /// 猶予のカウントダウン用。経過バーは TimelineView で描き、画面全体(Unityのビューを含む)を
    /// 毎回描き直さない
    private let tick = Timer.publish(every: 0.25, on: .main, in: .common).autoconnect()
    /// 電池・発熱(AppLogic/BoothPowerStatus)。iOS の電池は5%刻み・発熱の段階もゆっくり変わるので5秒ごとで足りる
    private let powerTick = Timer.publish(every: 5, on: .main, in: .common).autoconnect()
    @State private var power = BoothPowerStatus(PowerMonitor.shared.sample())
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
            perform(flow.tick(now: date))
        }
        .onReceive(powerTick) { _ in
            power = BoothPowerStatus(PowerMonitor.shared.sample())
        }
        .onReceive(NotificationCenter.default.publisher(for: ProcessInfo.thermalStateDidChangeNotification)
            .receive(on: RunLoop.main)) { _ in
            power = BoothPowerStatus(PowerMonitor.shared.sample())
        }
        .onReceive(bridge.$boothDemoProgress) { progress in
            guard progress != nil else { return }
            flow.progressReceived()
        }
        .onReceive(bridge.$lastBoothDemoEnd) { end in
            guard let end else { return }
            flow.endReceived(.init(completed: end.completed, reason: end.reason))
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
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                chip(icon: "eyeglasses", text: glassesStatusText, ok: external.isGlassesConnected)
                if !hasUnityView && !unity.isPreparing {
                    chip(icon: "exclamationmark.triangle", text: "Unity未起動", ok: false)
                }
                Spacer()
            }
            HStack(spacing: 8) {
                powerChip(icon: "battery.75percent", text: power.batteryText, level: power.batteryLevel)
                powerChip(icon: "thermometer.medium", text: power.thermalText, level: power.thermalLevel)
                Spacer()
            }
            if let advice = power.advice {
                HStack(alignment: .top, spacing: 8) {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .font(.system(size: 14, weight: .bold))
                    Text(advice)
                        .font(.system(size: 14, weight: .semibold))
                        .fixedSize(horizontal: false, vertical: true)
                }
                .foregroundColor(.white)
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(color(for: power.level).opacity(0.85), in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    /// 電池・発熱のチップ。平常はグラスのチップ(黄=良い)と紛れないよう灰色の枠だけ
    private func powerChip(icon: String, text: String, level: BoothPowerStatus.Level) -> some View {
        HStack(spacing: 6) {
            Image(systemName: icon).font(.system(size: 11, weight: .bold))
            Text(text).font(.system(size: 12, weight: .semibold)).monospacedDigit()
        }
        .foregroundColor(.white)
        .padding(.horizontal, 10)
        .frame(height: 28)
        .background(level == .ok ? Color.arCard : color(for: level), in: Capsule())
        .overlay(Capsule().stroke(level == .ok ? Color.arBorder : .clear, lineWidth: 1))
    }

    private func color(for level: BoothPowerStatus.Level) -> Color {
        switch level {
        case .ok:      return .arCard
        case .caution: return Color(red: 0.85, green: 0.5, blue: 0.1)
        case .danger:  return danger
        }
    }

    /// ミラーリングを「未接続」と分けて出す。ミラーリング中はグラスに iPhone の画面
    /// (この操作パネルとカメラ映像)がそのまま映るので、体験には使えない
    private var glassesStatusText: String {
        if external.isGlassesConnected { return "グラス接続中" }
        if external.isMirroring { return "ミラーリング中 — グラス専用の画面になっていません" }
        return "グラス未接続(iPhoneに表示)"
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
            if let resultMessage = flow.resultMessage {
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

            Button { perform(flow.begin(delaySeconds: startDelaySeconds, now: Date())) } label: {
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
            return "その場に立ったまま約55秒。遅れ・追い抜き・ゴールを順に見せます。アバターは体験中ずっと正面に見えています。"
        case .walking:
            return "来場者が数歩あるくと、アバターが3m前を追従します。向きを変えても正面に回り込みます。60秒で終了。"
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
            cancelButton(title: "取り消す") { stop() }
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

    private var isActive: Bool { flow.isActive }

    /// Flow が決めた Unity への送信を実行する
    private func perform(_ effect: BoothDemoFlow.Effect?) {
        switch effect {
        case .sendStart?: bridge.startBoothDemo(mode: mode)
        case .sendStop?:  bridge.stopBoothDemo()
        case nil: break
        }
    }

    private func stop() {
        perform(flow.stop())
    }

    private func exit() {
        if isActive { perform(flow.stop()) }
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
    case finalStretch = "FinalStretch"
    case finished = "Finished"
    case walking = "Walking"

    static let standingOrder: [BoothBeat] = [
        .onPace, .fallingBehind, .catchingUp, .overtaking, .settling, .finalStretch
    ]

    var order: Int { BoothBeat.allCases.firstIndex(of: self) ?? 0 }

    var title: String {
        switch self {
        case .onPace:        return "ジャスト"
        case .fallingBehind: return "遅れ"
        case .catchingUp:    return "追い上げ"
        case .overtaking:    return "追い抜き"
        case .settling:      return "ジャストへ"
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
        case .finalStretch:  return "最後の直線です。前方にゴールラインが見えてきます。"
        case .finished:      return "ゴールです。"
        case .walking:       return "ゆっくり歩いてみてください。アバターが3m前を追従します。向きを変えるとアバターも曲がります。"
        }
    }

    var color: Color {
        switch self {
        case .fallingBehind: return Color(red: 1.0, green: 0.35, blue: 0.25)
        case .overtaking:    return Color(red: 0.35, green: 0.65, blue: 1.0)
        default:             return .arYellow
        }
    }
}

#Preview {
    BoothDemoView(onExit: {})
}
