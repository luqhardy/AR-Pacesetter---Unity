import SwiftUI

// MARK: - Running Settings Screen
struct RunningSettingsView: View {
    // 値はこの画面のローカル状態で持ち、「次へ」で RunSettings.shared へ確定する。
    // hsu/main は RunConfiguration という別の器を導入していたが、Unityへ引き渡す
    // 共有ストアが既に RunSettings.shared にあるため、同じ3値の置き場を2つ作らない
    @State private var timeSeconds: Int
    @State private var distanceKm: Double
    @State private var paceKmh: Double
    let onNext: () -> Void
    let onBack: () -> Void

    /// 画面を開くたびに**前回「次へ」で確定した値**から始める(タスク表 S5。アプリを閉じても残る)。
    /// 以前は毎回 60分・10km・8km/h に戻っていた。「戻る」で離れた未確定の変更は捨てる
    init(onNext: @escaping () -> Void, onBack: @escaping () -> Void) {
        self.onNext = onNext
        self.onBack = onBack
        let confirmed = RunSettings.shared
        _timeSeconds = State(initialValue: confirmed.timeSeconds)
        _distanceKm = State(initialValue: confirmed.distanceKm)
        _paceKmh = State(initialValue: confirmed.paceKmH)
    }

    private let timeRange = RunSettingRanges.timeSeconds
    private let distanceRange = RunSettingRanges.distanceKm
    private let paceRange = RunSettingRanges.paceKmH

    @State private var editingTime = false
    @State private var editingDistance = false
    @State private var editingPace = false

    // ドラムロール(ホイール)入力: 4.0〜16.0 km/h を0.5刻み
    private var paceWheelValues: [Double] {
        stride(from: 4.0, through: 16.0, by: 0.5).map { $0 }
    }

    // ±/直接入力とホイールを双方向同期(0.5刻みへ丸めて選択位置を決める)
    private var paceWheelBinding: Binding<Double> {
        Binding(
            get: { min(16.0, max(4.0, (paceKmh * 2).rounded() / 2)) },
            set: { paceKmh = $0 }
        )
    }

    private func paceLabel(kmh: Double) -> String {
        guard kmh > 0 else { return "--'--\"" }
        let minPerKm = 60.0 / kmh
        let minutes = Int(minPerKm)
        let seconds = Int((minPerKm - Double(minutes)) * 60.0)
        return String(format: "%d'%02d\"", minutes, seconds)
    }

    @State private var timeInput = ""
    @State private var distanceInput = ""
    @State private var paceInput = ""

    @FocusState private var focusedField: Field?
    enum Field { case time, distance, pace }

    var timeDisplay: String {
        let h = timeSeconds / 3600
        let m = (timeSeconds % 3600) / 60
        let s = timeSeconds % 60
        return String(format: "%02d:%02d:%02d", h, m, s)
    }

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

                // Settings cards
                VStack(spacing: 14) {
                    // Time
                    SettingCard(
                        icon: "clock",
                        label: AppStrings.Settings.duration,
                        displayValue: timeDisplay,
                        inputText: $timeInput,
                        isEditing: $editingTime,
                        isFocused: focusedField == .time,
                        canMinus: timeRange.canDecrement(Double(timeSeconds)),
                        canPlus: timeRange.canIncrement(Double(timeSeconds)),
                        onMinus: { timeSeconds = Int(timeRange.decremented(Double(timeSeconds))) },
                        onPlus: { timeSeconds = Int(timeRange.incremented(Double(timeSeconds))) },
                        onTapValue: {
                            timeInput = timeDisplay
                            editingTime = true
                            editingDistance = false
                            editingPace = false
                            focusedField = .time
                        },
                        onCommit: {
                            applyTimeInput()
                            editingTime = false
                            focusedField = nil
                        }
                    )

                    // Distance
                    SettingCard(
                        icon: "mappin.and.ellipse",
                        label: AppStrings.Settings.distance,
                        displayValue: String(format: "%.2f km", distanceKm),
                        inputText: $distanceInput,
                        isEditing: $editingDistance,
                        isFocused: focusedField == .distance,
                        canMinus: distanceRange.canDecrement(distanceKm),
                        canPlus: distanceRange.canIncrement(distanceKm),
                        onMinus: { distanceKm = distanceRange.decremented(distanceKm) },
                        onPlus: { distanceKm = distanceRange.incremented(distanceKm) },
                        onTapValue: {
                            distanceInput = String(format: "%.2f", distanceKm)
                            editingDistance = true
                            editingTime = false
                            editingPace = false
                            focusedField = .distance
                        },
                        onCommit: {
                            applyDistanceInput()
                            editingDistance = false
                            focusedField = nil
                        }
                    )

                    // Pace
                    SettingCard(
                        icon: "speedometer",
                        label: AppStrings.Settings.pace,
                        displayValue: String(format: "%.1f km/h", paceKmh),
                        inputText: $paceInput,
                        isEditing: $editingPace,
                        isFocused: focusedField == .pace,
                        canMinus: paceRange.canDecrement(paceKmh),
                        canPlus: paceRange.canIncrement(paceKmh),
                        onMinus: { paceKmh = paceRange.decremented(paceKmh) },
                        onPlus: { paceKmh = paceRange.incremented(paceKmh) },
                        onTapValue: {
                            paceInput = String(format: "%.1f", paceKmh)
                            editingPace = true
                            editingTime = false
                            editingDistance = false
                            focusedField = .pace
                        },
                        onCommit: {
                            applyPaceInput()
                            editingPace = false
                            focusedField = nil
                        }
                    )

                    // ドラムロール入力 (企画書§5 ハイブリッド入力: ドラムロール・±・直接入力)
                    // スワイプで直感的にペースを選ぶホイールピッカー
                    VStack(alignment: .leading, spacing: 4) {
                        Text(AppStrings.Settings.wheel)
                            .font(.system(size: 12))
                            .foregroundColor(.arGrayText)
                            .padding(.leading, 4)
                        Picker(AppStrings.Settings.wheelLabel, selection: paceWheelBinding) {
                            ForEach(paceWheelValues, id: \.self) { value in
                                Text(AppStrings.Formats.paceWheel(value, paceLabel(kmh: value)))
                                    .font(.system(size: 16, weight: .medium))
                                    .foregroundColor(.white)
                                    .tag(value)
                            }
                        }
                        .pickerStyle(.wheel)
                        .frame(height: 96)
                        .clipped()
                        .background(Color.arCard)
                        .clipShape(RoundedRectangle(cornerRadius: 16))
                        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.arBorder, lineWidth: 1))
                    }
                }
                .padding(.horizontal, 24)

                Spacer()

                // Next button
                Button {
                    // 入力中の値を先に確定する。キーボードを開いたまま「次へ」を押すと
                    // 打ち込んだ数値が捨てられていた(hsu/main 由来の修正)
                    finishEditing()

                    // 確定後の値を共有ストアへ反映（RunningViewがUnityへ引き渡す・端末にも保存）。
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
                .padding(.bottom, 52)
            }
            .contentShape(Rectangle())
            .onTapGesture {
                editingTime = false
                editingDistance = false
                editingPace = false
                focusedField = nil
            }
        }
        // 初回起動の待ち時間を、ユーザーが数値を入力しているこの画面へ前倒しする。
        // 既定では何もしない（UnityLauncher.prewarmEnabled = false）。
        // 有効化の判断材料と確認事項は UnityLauncher.prewarmEnabled のコメントに記載
        .onAppear { UnityLauncher.shared.prewarm() }
    }

    private func finishEditing() {
        switch focusedField {
        case .time:
            applyTimeInput()
        case .distance:
            applyDistanceInput()
        case .pace:
            applyPaceInput()
        case nil:
            break
        }

        editingTime = false
        editingDistance = false
        editingPace = false
        focusedField = nil
    }

    // 直接入力は半角・全角のどちらでも読む(NumericInput — 以前は全角の「５」が黙って捨てられていた)。
    // 読めない入力は今の値のまま(エラー表示はタスク表 S5「不正な文字列」の行で別途)

    private func applyTimeInput() {
        if let seconds = NumericInput.parseDuration(timeInput) {
            timeSeconds = Int(timeRange.clamped(Double(seconds)))
        }
    }

    private func applyDistanceInput() {
        if let value = NumericInput.parseDecimal(distanceInput) {
            distanceKm = distanceRange.clamped(value)
        }
    }

    private func applyPaceInput() {
        if let value = NumericInput.parseDecimal(paceInput) {
            paceKmh = paceRange.clamped(value)
        }
    }
}

// MARK: - Setting Card Component
struct SettingCard: View {
    let icon: String
    let label: LocalizedStringKey
    let displayValue: String
    @Binding var inputText: String
    @Binding var isEditing: Bool
    let isFocused: Bool
    /// 境界値に達したら押せなくする(タスク表 S5)
    var canMinus: Bool = true
    var canPlus: Bool = true
    let onMinus: () -> Void
    let onPlus: () -> Void
    let onTapValue: () -> Void
    let onCommit: () -> Void

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
                ZStack(alignment: .leading) {
                    if isEditing {
                        TextField("", text: $inputText)
                            .keyboardType(.decimalPad)
                            .font(.system(size: 22, weight: .bold, design: .monospaced))
                            .foregroundColor(.arYellow)
                            .onSubmit { onCommit() }
                            .frame(maxWidth: .infinity)
                            .padding(.horizontal, 14)
                            .frame(height: 48)
                            .background(Color.arBG)
                            .clipShape(RoundedRectangle(cornerRadius: 12))
                            .overlay(
                                RoundedRectangle(cornerRadius: 12)
                                    .stroke(Color.arYellow, lineWidth: 1.5)
                            )
                    } else {
                        Button(action: onTapValue) {
                            Text(displayValue)
                                .font(.system(size: 22, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 14)
                                .frame(height: 48)
                                .background(Color.arBG)
                                .clipShape(RoundedRectangle(cornerRadius: 12))
                                .overlay(
                                    RoundedRectangle(cornerRadius: 12)
                                        .stroke(Color.arBorder, lineWidth: 1)
                                )
                        }
                        .buttonStyle(.plain)
                    }
                }

                StepButton(icon: "minus", isEnabled: canMinus, action: onMinus)
                StepButton(icon: "plus", isEnabled: canPlus, action: onPlus)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(
            RoundedRectangle(cornerRadius: 18)
                .stroke(isEditing ? Color.arYellow.opacity(0.4) : Color.arBorder, lineWidth: 1)
        )
        .animation(.easeInOut(duration: 0.15), value: isEditing)
    }
}

// MARK: - Step Button (+/-)
struct StepButton: View {
    let icon: String
    var isEnabled: Bool = true
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(isEnabled ? .arYellow : .arGrayText.opacity(0.5))
                .frame(width: 48, height: 48)
                .background(Color.arBG)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(isEnabled ? Color.arYellow.opacity(0.5) : Color.arBorder, lineWidth: 1.5)
                )
        }
        .buttonStyle(.plain)
        .disabled(!isEnabled)
        .animation(.easeInOut(duration: 0.15), value: isEnabled)
    }
}
