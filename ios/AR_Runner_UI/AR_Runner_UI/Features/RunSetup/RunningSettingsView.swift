import SwiftUI

// MARK: - Running Settings Screen
struct RunningSettingsView: View {
    // 値はこの画面のローカル状態で持ち、「次へ」で RunSettings.shared へ確定する。
    @State private var timeSeconds: Int
    @State private var distanceKm: Double
    @State private var paceKmh: Double
    let onNext: () -> Void
    let onBack: () -> Void

    /// 画面を開くたびに前回「次へ」で確定した値から始める(タスク表 S5)。
    init(onNext: @escaping () -> Void, onBack: @escaping () -> Void) {
        self.onNext = onNext
        self.onBack = onBack
        let confirmed = RunSettings.shared
        let linked = RunTargetLink.keepingPace(distanceKm: confirmed.distanceKm, paceKmH: confirmed.paceKmH)
        _timeSeconds = State(initialValue: linked.timeSeconds)
        _distanceKm = State(initialValue: linked.distanceKm)
        _paceKmh = State(initialValue: linked.paceKmH)
    }

    // MARK: State

    /// 「次へ」の最終チェックで引っかかった理由(タスク表 S5)。nil なら出さない
    @State private var targetIssue: RunTargetIssue?

    /// ホイールを開いているカード(同時に1枚だけ)
    @State private var expanded: Part?
    enum Part { case time, distance, pace }

    private let timeRange = RunSettingRanges.timeSeconds
    private let distanceRange = RunSettingRanges.distanceKm
    private let paceRange = RunSettingRanges.paceKmH

    // MARK: 3値の相互依存(RunTargetLink)
    // 距離は自動では変えない。距離・ペースを動かしたら時間を、時間を動かしたらペースを合わせる

    private var current: RunTargetSettings {
        RunTargetSettings(paceKmH: paceKmh, distanceKm: distanceKm, timeSeconds: timeSeconds)
    }

    private func apply(_ linked: RunTargetSettings) {
        timeSeconds = linked.timeSeconds
        distanceKm = linked.distanceKm
        paceKmh = linked.paceKmH
    }

    private func setTime(_ seconds: Int) { apply(RunTargetLink.keepingTime(distanceKm: distanceKm, timeSeconds: seconds)) }
    private func setDistance(_ km: Double) { apply(RunTargetLink.keepingPace(distanceKm: km, paceKmH: paceKmh)) }
    private func setPace(_ kmh: Double) { apply(RunTargetLink.keepingPace(distanceKm: distanceKm, paceKmH: kmh)) }

    // MARK: 値を桁ごとに分解(表示の丸めと同じ基準で分ける)

    // 時間: 時 / 分 / 秒
    private var hours: Int { min(23, timeSeconds / 3600) }
    private var minutes: Int { (timeSeconds % 3600) / 60 }
    private var seconds: Int { timeSeconds % 60 }

    // 距離: 整数部 / 小数部(0.01 km 単位。表示は %.2f)
    private var distanceHundredths: Int { Int((distanceKm * 100).rounded()) }
    private var distanceWhole: Int { min(99, distanceHundredths / 100) }
    private var distanceFraction: Int { distanceHundredths % 100 }

    // ペース: 整数部 / 小数部(0.1 km/h 単位。表示は %.1f)
    private var paceTenths: Int { Int((paceKmh * 10).rounded()) }
    private var paceWhole: Int { min(30, paceTenths / 10) }
    private var paceFraction: Int { paceTenths % 10 }

    // MARK: 1つの桁だけ変えて、他の桁はそのまま組み立て直す
    // (範囲外になった場合だけ clamped で丸められる)

    private func setTimeParts(h: Int, m: Int, s: Int) {
        let total = h * 3600 + m * 60 + s
        setTime(Int(timeRange.clamped(Double(total))))
    }

    private func setDistanceParts(whole: Int, fraction: Int) {
        let km = Double(whole) + Double(fraction) / 100.0
        setDistance(distanceRange.clamped(km))
    }

    private func setPaceParts(whole: Int, fraction: Int) {
        let kmh = Double(whole) + Double(fraction) / 10.0
        setPace(paceRange.clamped(kmh))
    }

    private func toggle(_ part: Part) {
        withAnimation(.easeInOut(duration: 0.2)) {
            expanded = (expanded == part) ? nil : part
        }
    }

    var timeDisplay: String {
        String(format: "%02d:%02d:%02d", timeSeconds / 3600, (timeSeconds % 3600) / 60, timeSeconds % 60)
    }

    // MARK: Body

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                // Nav bar
                HStack {
                    ARBackButton(action: onBack)
                    Spacer()
                }
                .padding(.horizontal, 20)
                .padding(.top, 56)
                .padding(.bottom, 16)

                // Header
                VStack(alignment: .leading, spacing: 4) {
                    ARLabel(text: AppStrings.Common.settingsEyebrow)
                    Text(AppStrings.Settings.title)
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text(AppStrings.Settings.subtitle)
                        .font(.system(size: 14))
                        .foregroundColor(.arGrayText)
                        .padding(.top, 2)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28)
                .padding(.bottom, 28)

                // Settings cards(ホイールを開くと縦に伸びるので、小さい画面用にスクロール可能にする)
                ScrollView(showsIndicators: false) {
                    VStack(spacing: 14) {
                        // Time: h / min / sec
                        WheelSettingCard(
                            icon: "clock",
                            label: AppStrings.Settings.duration,
                            displayValue: timeDisplay,
                            isExpanded: expanded == .time,
                            onToggle: { toggle(.time) }
                        ) {
                            WheelColumn(title: "h", range: 0...23,
                                        selection: Binding(get: { hours },
                                                           set: { setTimeParts(h: $0, m: minutes, s: seconds) }))
                            WheelSeparator(text: ":")
                            WheelColumn(title: "min", range: 0...59,
                                        selection: Binding(get: { minutes },
                                                           set: { setTimeParts(h: hours, m: $0, s: seconds) }))
                            WheelSeparator(text: ":")
                            WheelColumn(title: "sec", range: 0...59,
                                        selection: Binding(get: { seconds },
                                                           set: { setTimeParts(h: hours, m: minutes, s: $0) }))
                        }

                        // Distance: 整数部 . 小数部(0.01 km)
                        WheelSettingCard(
                            icon: "mappin.and.ellipse",
                            label: AppStrings.Settings.distance,
                            displayValue: String(format: "%.2f km", distanceKm),
                            isExpanded: expanded == .distance,
                            onToggle: { toggle(.distance) }
                        ) {
                            WheelColumn(title: "km", range: 0...99,
                                        selection: Binding(get: { distanceWhole },
                                                           set: { setDistanceParts(whole: $0, fraction: distanceFraction) }))
                            WheelSeparator(text: ".")
                            WheelColumn(title: "decimal", range: 0...99, digits: 2,
                                        selection: Binding(get: { distanceFraction },
                                                           set: { setDistanceParts(whole: distanceWhole, fraction: $0) }))
                        }

                        // Pace: 整数部 . 小数部(0.1 km/h)
                        WheelSettingCard(
                            icon: "speedometer",
                            label: AppStrings.Settings.pace,
                            displayValue: String(format: "%.1f km/h", paceKmh),
                            isExpanded: expanded == .pace,
                            onToggle: { toggle(.pace) }
                        ) {
                            WheelColumn(title: "km/h", range: 0...30,
                                        selection: Binding(get: { paceWhole },
                                                           set: { setPaceParts(whole: $0, fraction: paceFraction) }))
                            WheelSeparator(text: ".")
                            WheelColumn(title: "decimal", range: 0...9, digits: 1,
                                        selection: Binding(get: { paceFraction },
                                                           set: { setPaceParts(whole: paceWhole, fraction: $0) }))
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 16)
                }

                // Next button
                Button {
                    // 最終チェック(タスク表 S5): 走れない値ではルート選択へ進まず、理由をダイアログで出す
                    if let issue = RunTargetIssue.check(current) {
                        targetIssue = issue
                        return
                    }

                    // 確定後の値を共有ストアへ反映(RunningViewがUnityへ引き渡す・端末にも保存)。
                    // 順序が逆だと確定前の古い値がUnityへ流れる
                    RunSettings.shared.confirm(paceKmH: paceKmh, distanceKm: distanceKm,
                                               timeSeconds: timeSeconds)
                    onNext()
                } label: {
                    ZStack {
                        Circle()
                            .fill(Color.arYellow)
                            .frame(width: 64, height: 64)
                            .shadow(color: Color.arYellow.opacity(0.45), radius: 18, x: 0, y: 0)
                        Image(systemName: "arrow.right")
                            .font(.system(size: 22, weight: .bold))
                            .foregroundColor(.black)
                    }
                }
                .padding(.top, 8)
                .padding(.bottom, 52)
            }
        }
        // 初回起動の待ち時間を、ユーザーが数値を入力しているこの画面へ前倒しする。
        // 既定では何もしない(UnityLauncher.prewarmEnabled = false)。
        .onAppear { UnityLauncher.shared.prewarm() }
        .alert(targetIssueTitle, isPresented: Binding(
            get: { targetIssue != nil },
            set: { if !$0 { targetIssue = nil } }
        )) {
            Button(AppStrings.Common.close, role: .cancel) {}
        } message: {
            Text(targetIssueMessage)
        }
    }

    private var targetIssueTitle: LocalizedStringKey {
        targetIssue == .paceTooFast ? AppStrings.Settings.tooLargeTitle : AppStrings.Settings.tooSmallTitle
    }

    private var targetIssueMessage: LocalizedStringKey {
        switch targetIssue {
        case .paceTooSlow: return AppStrings.Settings.paceTooSlow
        case .paceTooFast: return AppStrings.Settings.paceTooFast
        case .inconsistent, nil: return AppStrings.Settings.inconsistent
        }
    }
}

// MARK: - Setting Card (value + up/down button, expands to wheels)
struct WheelSettingCard<Wheels: View>: View {
    let icon: String
    let label: LocalizedStringKey
    let displayValue: String
    let isExpanded: Bool
    let onToggle: () -> Void
    let wheels: Wheels

    init(icon: String,
         label: LocalizedStringKey,
         displayValue: String,
         isExpanded: Bool,
         onToggle: @escaping () -> Void,
         @ViewBuilder wheels: () -> Wheels) {
        self.icon = icon
        self.label = label
        self.displayValue = displayValue
        self.isExpanded = isExpanded
        self.onToggle = onToggle
        self.wheels = wheels()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 7) {
                Image(systemName: icon)
                    .font(.system(size: 13))
                    .foregroundColor(.arYellow)
                Text(label)
                    .font(.system(size: 14, weight: .medium))
                    .foregroundColor(.arGrayText)
            }

            HStack(spacing: 10) {
                Button(action: onToggle) {
                    Text(displayValue)
                        .font(.system(size: 22, weight: .bold, design: .monospaced))
                        .foregroundColor(isExpanded ? .arYellow : .white)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 14)
                        .frame(height: 48)
                        .background(Color.arBG)
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                        .overlay(
                            RoundedRectangle(cornerRadius: 12)
                                .stroke(isExpanded ? Color.arYellow : Color.arBorder,
                                        lineWidth: isExpanded ? 1.5 : 1)
                        )
                }
                .buttonStyle(.plain)

                UpDownButton(isActive: isExpanded, action: onToggle)
            }

            if isExpanded {
                HStack(spacing: 0) {
                    wheels
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .background(Color.arBG)
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.arBorder, lineWidth: 1))
                .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(isExpanded ? Color.arYellow.opacity(0.4) : Color.arBorder, lineWidth: 1)
        )
    }
}

// MARK: - Up/Down button (参考画像の ⌃⌄ 丸ボタン)
struct UpDownButton: View {
    var isActive: Bool = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .fill(Color.arYellow.opacity(isActive ? 0.28 : 0.15))
                Image(systemName: "chevron.up.chevron.down")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundColor(.arYellow)
            }
            .frame(width: 48, height: 48)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Edit value")
        .animation(.easeInOut(duration: 0.15), value: isActive)
    }
}

// MARK: - One drum-roll column (1桁ぶん)
struct WheelColumn: View {
    /// 列の上に出す単位ラベル(必要なら AppStrings へ移してローカライズ)
    let title: LocalizedStringKey
    let range: ClosedRange<Int>
    /// ゼロ埋めの桁数(0 なら埋めない)
    var digits: Int = 2
    @Binding var selection: Int

    init(title: LocalizedStringKey,
         range: ClosedRange<Int>,
         digits: Int = 2,
         selection: Binding<Int>) {
        self.title = title
        self.range = range
        self.digits = digits
        self._selection = selection
    }

    private func label(_ value: Int) -> String {
        let s = String(value)
        return String(repeating: "0", count: max(0, digits - s.count)) + s
    }

    var body: some View {
        VStack(spacing: 2) {
            Text(title)
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(.arGrayText)

            Picker(title, selection: $selection) {
                ForEach(range, id: \.self) { value in
                    Text(label(value))
                        .font(.system(size: 22, weight: .semibold, design: .monospaced))
                        .foregroundColor(.white)
                        .tag(value)
                }
            }
            .labelsHidden()
            .pickerStyle(.wheel)
            .environment(\.colorScheme, .dark)   // ホイールの文字を白で表示
            .frame(height: 120)
            .clipped()
            .overlay(
                // 選択中の行をライムの枠で強調
                RoundedRectangle(cornerRadius: 10)
                    .stroke(Color.arYellow.opacity(0.6), lineWidth: 1.5)
                    .frame(height: 36)
                    .allowsHitTesting(false)
            )
        }
        .frame(maxWidth: .infinity)
    }
}

// MARK: - Separator between columns (":" or ".")
struct WheelSeparator: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 22, weight: .bold, design: .monospaced))
            .foregroundColor(.arGrayText)
            .padding(.top, 18)   // 列タイトルの分だけ下げて、ホイールの中央に合わせる
    }
}
