import Foundation
import Combine

// MARK: - 身体情報(S9-2)の入力
// hsuyaminmyat625/AR_project の BodyInfoViewModel を移植。補正と判定は AppLogic/BodyInfo.swift(テスト可能)へ移し、
// ここは入力欄の文字と誤りの表示を持つだけ
final class BodyInfoViewModel: ObservableObject {

    @Published var heightText = ""
    @Published var weightText = ""
    @Published var gender: Gender?

    @Published private(set) var heightError: BodyInputError?
    @Published private(set) var weightError: BodyInputError?
    @Published private(set) var saveFailed = false

    private let store: BodyInfoStore

    /// 補正で自分が書き戻した文字。その onChange は無視する(今出した誤りを消さないため)
    private var pendingCorrection: [BodyField: String] = [:]

    init(store: BodyInfoStore = .shared) {
        self.store = store
        let info = store.load()
        heightText = info.heightCm.map { String($0) } ?? ""
        weightText = info.weightKg.map { String($0) } ?? ""
        gender = info.gender
    }

    /// 選び直すと外れる(未選択へ戻せる)
    func toggleGender(_ value: Gender) {
        gender = (gender == value) ? nil : value
    }

    /// 入力欄が変わるたびに呼ぶ
    func handleInput(_ field: BodyField, newValue: String) {
        if pendingCorrection[field] == newValue {
            pendingCorrection[field] = nil
            return
        }
        pendingCorrection[field] = nil

        let corrected = BodyInfoInput.correct(newValue, field: field)
        setError(corrected.error, for: field)
        if corrected.text != newValue {
            pendingCorrection[field] = corrected.text
            setText(corrected.text, for: field)
        }
    }

    /// 欄を離れたとき・保存の前
    func revalidate(_ field: BodyField) {
        setError(BodyInfoInput.validate(text(for: field), field: field), for: field)
    }

    func clear(_ field: BodyField) {
        setText("", for: field)
        setError(nil, for: field)
    }

    func pickerStartValue(for field: BodyField) -> Int {
        BodyInfoInput.pickerStartValue(text(for: field), field: field)
    }

    func apply(pickerValue: Int, to field: BodyField) {
        setText(String(pickerValue), for: field)
        setError(nil, for: field)
    }

    /// 保存できたら true。失敗したら画面に留まって誤りを出す
    func save(markSetupComplete: Bool) -> Bool {
        saveFailed = false
        revalidate(.height)
        revalidate(.weight)
        guard heightError == nil, weightError == nil else { return false }

        do {
            try store.save(BodyInfo(heightCm: Int(heightText), weightKg: Int(weightText), gender: gender))
            if markSetupComplete { store.hasCompletedInitialSetup = true }
            return true
        } catch {
            saveFailed = true
            return false
        }
    }

    private func text(for field: BodyField) -> String {
        field == .height ? heightText : weightText
    }

    private func setText(_ value: String, for field: BodyField) {
        if field == .height { heightText = value } else { weightText = value }
    }

    private func setError(_ error: BodyInputError?, for field: BodyField) {
        if field == .height { heightError = error } else { weightError = error }
    }
}
