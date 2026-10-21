import SwiftUI

// MARK: - 身体情報(S9-2)
// hsuyaminmyat625/AR_project の BodyInfoView(2026-10-21)を移植。文言は AppStrings(日本語・英語)へ。
// 性別・身長・体重はすべて任意。初回はオンボーディングと免責の後(「次へ」・戻るなし)、
// 以降はスマホ設定から開いて直す(「保存」・戻るあり)
struct BodyInfoView: View {
    /// true: 初回の流れ / false: スマホ設定から編集
    let isInitialSetup: Bool
    let onNext: () -> Void
    let onBack: () -> Void

    @StateObject private var vm = BodyInfoViewModel()
    @FocusState private var focusedField: BodyField?
    @State private var pickerField: BodyField?

    var body: some View {
        ARScreen {
            VStack(spacing: 0) {
                navBar
                header

                ScrollView(showsIndicators: false) {
                    VStack(spacing: 16) {
                        genderSection
                        measurementCard(field: .height, text: $vm.heightText, error: vm.heightError)
                        measurementCard(field: .weight, text: $vm.weightText, error: vm.weightError)
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 24)
                }
                .scrollDismissesKeyboard(.interactively)

                footer
            }
        }
        .onChange(of: vm.heightText) { _, newValue in
            vm.handleInput(.height, newValue: newValue)
        }
        .onChange(of: vm.weightText) { _, newValue in
            vm.handleInput(.weight, newValue: newValue)
        }
        .onChange(of: focusedField) { oldValue, _ in
            if let oldValue { vm.revalidate(oldValue) }
        }
        .sheet(item: $pickerField) { field in
            WheelPickerSheet(
                title: title(for: field),
                unit: unit(for: field),
                range: field.range,
                initial: vm.pickerStartValue(for: field),
                onCancel: { pickerField = nil },
                onDone: { value in
                    vm.apply(pickerValue: value, to: field)
                    pickerField = nil
                }
            )
        }
    }

    // MARK: - Sections

    private var navBar: some View {
        HStack {
            if isInitialSetup {
                Color.clear.frame(width: 1, height: 44)
            } else {
                ARBackButton(action: onBack)
            }
            Spacer()
        }
        .padding(.horizontal, 20)
        .padding(.top, 56)
        .padding(.bottom, 16)
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            ARLabel(text: AppStrings.BodyInfo.eyebrow)
            Text(AppStrings.BodyInfo.title)
                .font(.system(size: 30, weight: .bold))
                .foregroundColor(.white)
            Text(isInitialSetup ? AppStrings.BodyInfo.subtitleInitial : AppStrings.BodyInfo.subtitleEdit)
                .font(.system(size: 14))
                .foregroundColor(.arGrayText)
                .padding(.top, 2)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 28)
        .padding(.bottom, 20)
    }

    private var genderSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(AppStrings.BodyInfo.gender)
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(.arGrayText)

            HStack(spacing: 10) {
                ForEach(Gender.allCases) { option in
                    let isSelected = vm.gender == option
                    Button {
                        vm.toggleGender(option)
                    } label: {
                        Text(AppStrings.BodyInfo.genderLabel(option))
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundColor(isSelected ? .black : .white)
                            .frame(maxWidth: .infinity)
                            .frame(height: 48)
                            .background(isSelected ? Color.arYellow : Color.arCard)
                            .clipShape(RoundedRectangle(cornerRadius: 16))
                            .overlay(
                                RoundedRectangle(cornerRadius: 16)
                                    .stroke(isSelected ? Color.clear : Color.arBorder, lineWidth: 1)
                            )
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(isSelected ? .isSelected : [])
                }
            }
        }
    }

    private func measurementCard(field: BodyField, text: Binding<String>, error: BodyInputError?) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(title(for: field))
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(.arGrayText)
                Spacer()
                Text(verbatim: "\(field.range.lowerBound)〜\(field.range.upperBound) \(unit(for: field))")
                    .font(.system(size: 12))
                    .foregroundColor(.arGrayText)
            }

            HStack(spacing: 10) {
                // 既定のキーボードのまま(.numberPad だと全角の数字が打てない)
                TextField(text: text, prompt: Text(verbatim: field == .height ? "170" : "60")) {
                    Text(title(for: field))
                }
                    .focused($focusedField, equals: field)
                    .submitLabel(.done)
                    .onSubmit { focusedField = nil }
                    .font(.system(size: 28, weight: .bold, design: .rounded))
                    .foregroundColor(.white)
                    .tint(Color.arYellow)

                Text(verbatim: unit(for: field))
                    .font(.system(size: 15, weight: .medium))
                    .foregroundColor(.arGrayText)

                if !text.wrappedValue.isEmpty {
                    Button {
                        vm.clear(field)
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundColor(.arGrayText)
                    }
                    .accessibilityLabel(Text(AppStrings.BodyInfo.clear))
                }

                Button {
                    focusedField = nil
                    pickerField = field
                } label: {
                    Image(systemName: "chevron.up.chevron.down")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(.arYellow)
                        .frame(width: 36, height: 36)
                        .background(Color.arYellow.opacity(0.12))
                        .clipShape(Circle())
                }
                .accessibilityLabel(Text(AppStrings.BodyInfo.choose))
            }

            if let error {
                Text(errorMessage(error, field: field))
                    .font(.system(size: 12))
                    .foregroundColor(.red)
            }
        }
        .padding(16)
        .background(Color.arCard)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(borderColor(for: field, hasError: error != nil), lineWidth: 1)
        )
    }

    private func borderColor(for field: BodyField, hasError: Bool) -> Color {
        if hasError { return .red }
        if focusedField == field { return .arYellow }
        return .arBorder
    }

    private var footer: some View {
        VStack(spacing: 12) {
            if vm.saveFailed {
                Text(AppStrings.BodyInfo.saveFailed)
                    .font(.system(size: 13))
                    .foregroundColor(.red)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            ARButton(isInitialSetup ? AppStrings.Common.next : AppStrings.BodyInfo.save) {
                focusedField = nil
                if vm.save(markSetupComplete: isInitialSetup) {
                    onNext()
                }
            }
        }
        .padding(.horizontal, 24)
        .padding(.top, 8)
        .padding(.bottom, 40)
    }

    // MARK: - 文言

    private func title(for field: BodyField) -> LocalizedStringKey {
        field == .height ? AppStrings.BodyInfo.height : AppStrings.BodyInfo.weight
    }

    /// 単位は言語によらず cm / kg
    private func unit(for field: BodyField) -> String {
        field == .height ? "cm" : "kg"
    }

    private func errorMessage(_ error: BodyInputError, field: BodyField) -> LocalizedStringKey {
        switch error {
        case .invalidCharacter: return AppStrings.BodyInfo.invalidCharacter
        case .outOfRange: return field == .height ? AppStrings.BodyInfo.heightRange : AppStrings.BodyInfo.weightRange
        }
    }
}

// MARK: - ドラムロール

private struct WheelPickerSheet: View {
    let title: LocalizedStringKey
    let unit: String
    let range: ClosedRange<Int>
    let onCancel: () -> Void
    let onDone: (Int) -> Void

    @State private var selection: Int

    init(
        title: LocalizedStringKey,
        unit: String,
        range: ClosedRange<Int>,
        initial: Int,
        onCancel: @escaping () -> Void,
        onDone: @escaping (Int) -> Void
    ) {
        self.title = title
        self.unit = unit
        self.range = range
        self.onCancel = onCancel
        self.onDone = onDone
        _selection = State(initialValue: initial)
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button(AppStrings.BodyInfo.cancel, action: onCancel)
                    .foregroundColor(.arGrayText)
                Spacer()
                Text(title)
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                Spacer()
                Button(AppStrings.BodyInfo.done) { onDone(selection) }
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.arYellow)
            }
            .padding(.horizontal, 24)
            .padding(.top, 24)

            Picker(selection: $selection) {
                ForEach(Array(range), id: \.self) { value in
                    Text(verbatim: "\(value) \(unit)").tag(value)
                }
            } label: {
                Text(title)
            }
            .pickerStyle(.wheel)
        }
        .background(Color.arBG.ignoresSafeArea())
        .presentationDetents([.height(320)])
        .preferredColorScheme(.dark)
    }
}

#Preview {
    BodyInfoView(isInitialSetup: true, onNext: {}, onBack: {})
}
