import SwiftUI

// MARK: - Running Settings Screen
struct RunningSettingsView: View {
    let onNext: () -> Void
    let onBack: () -> Void

    @State private var timeSeconds: Int = 3600
    @State private var distanceKm: Double = 10.0
    @State private var paceKmh: Double = 8.0

    @State private var editingTime = false
    @State private var editingDistance = false
    @State private var editingPace = false

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
                    ARLabel(text: "SETTINGS")
                    Text("ランニング設定")
                        .font(.system(size: 30, weight: .bold))
                        .foregroundColor(.white)
                    Text("目標を設定してください")
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
                        label: "時間",
                        displayValue: timeDisplay,
                        inputText: $timeInput,
                        isEditing: $editingTime,
                        isFocused: focusedField == .time,
                        onMinus: { timeSeconds = max(60, timeSeconds - 60) },
                        onPlus: { timeSeconds = min(86400, timeSeconds + 60) },
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
                        label: "距離",
                        displayValue: String(format: "%.2f km", distanceKm),
                        inputText: $distanceInput,
                        isEditing: $editingDistance,
                        isFocused: focusedField == .distance,
                        onMinus: { distanceKm = max(0.5, round((distanceKm - 0.5) * 100) / 100) },
                        onPlus: { distanceKm = min(200.0, round((distanceKm + 0.5) * 100) / 100) },
                        onTapValue: {
                            distanceInput = String(format: "%.2f", distanceKm)
                            editingDistance = true
                            editingTime = false
                            editingPace = false
                            focusedField = .distance
                        },
                        onCommit: {
                            if let v = Double(distanceInput), v > 0 { distanceKm = min(200.0, max(0.5, v)) }
                            editingDistance = false
                            focusedField = nil
                        }
                    )

                    // Pace
                    SettingCard(
                        icon: "speedometer",
                        label: "ペース（速度）",
                        displayValue: String(format: "%.1f km/h", paceKmh),
                        inputText: $paceInput,
                        isEditing: $editingPace,
                        isFocused: focusedField == .pace,
                        onMinus: { paceKmh = max(1.0, round((paceKmh - 0.5) * 10) / 10) },
                        onPlus: { paceKmh = min(30.0, round((paceKmh + 0.5) * 10) / 10) },
                        onTapValue: {
                            paceInput = String(format: "%.1f", paceKmh)
                            editingPace = true
                            editingTime = false
                            editingDistance = false
                            focusedField = .pace
                        },
                        onCommit: {
                            if let v = Double(paceInput), v > 0 { paceKmh = min(30.0, max(1.0, v)) }
                            editingPace = false
                            focusedField = nil
                        }
                    )
                }
                .padding(.horizontal, 24)

                Spacer()

                // Next button
                Button(action: {
                    // 設定値を共有ストアへ反映（RunningViewがUnityへ引き渡す）
                    RunSettings.shared.paceKmH = paceKmh
                    RunSettings.shared.distanceKm = distanceKm
                    RunSettings.shared.timeSeconds = timeSeconds
                    onNext()
                }) {
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
    }

    private func applyTimeInput() {
        let parts = timeInput.split(separator: ":").map { Int($0) ?? 0 }
        switch parts.count {
        case 3: timeSeconds = max(60, parts[0] * 3600 + parts[1] * 60 + parts[2])
        case 2: timeSeconds = max(60, parts[0] * 60 + parts[1])
        case 1: timeSeconds = max(60, parts[0] * 60)
        default: break
        }
    }
}

// MARK: - Setting Card Component
struct SettingCard: View {
    let icon: String
    let label: String
    let displayValue: String
    @Binding var inputText: String
    @Binding var isEditing: Bool
    let isFocused: Bool
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

                StepButton(icon: "minus", action: onMinus)
                StepButton(icon: "plus", action: onPlus)
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
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(.arYellow)
                .frame(width: 48, height: 48)
                .background(Color.arBG)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.arYellow.opacity(0.5), lineWidth: 1.5)
                )
        }
        .buttonStyle(.plain)
    }
}
