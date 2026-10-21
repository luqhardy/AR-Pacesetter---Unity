import Foundation

// MARK: - 身体情報(S9-2)
//
// hsuyaminmyat625/AR_project の身体情報画面(2026-10-21)の型と入力の判断を移植。
// 性別・身長・体重はすべて任意(空欄のまま進める・保存できる)。初回はオンボーディングと免責の後に1度出し、
// 以降はスマホ設定から直す。今のところ保存するだけで、走行(アバターの身長など)には使っていない。
//
// UIに依存しない純ロジック(ios/AppLogicTests で `swift test` できる)。

nonisolated enum Gender: String, Codable, CaseIterable, Identifiable, Sendable {
    case male
    case female
    case other

    var id: String { rawValue }
}

nonisolated struct BodyInfo: Codable, Equatable, Sendable {
    var heightCm: Int?
    var weightKg: Int?
    var gender: Gender?

    static let heightRange = 50...300   // cm
    static let weightRange = 10...300   // kg

    static let empty = BodyInfo(heightCm: nil, weightKg: nil, gender: nil)
}

/// 数値の入力欄(身長・体重)
nonisolated enum BodyField: Hashable, Identifiable, Sendable {
    case height
    case weight

    var id: Self { self }
    var range: ClosedRange<Int> { self == .height ? BodyInfo.heightRange : BodyInfo.weightRange }
    /// ドラムロールの初期値(入力が空・範囲外のとき)
    var pickerDefault: Int { self == .height ? 170 : 60 }
}

/// 入力欄の誤り。文言は画面が AppStrings で引く
nonisolated enum BodyInputError: Equatable, Sendable {
    /// 数字以外が混ざっていた(取り除いた)
    case invalidCharacter
    /// 範囲外(上限を超えた桁は入力中に取り除く。下限は欄を離れたとき・保存時に判定)
    case outOfRange
}

nonisolated enum BodyInfoInput {
    /// 1文字打つたびの補正。全角の数字は半角へ、数字以外と先頭の0は取り除き、上限を超える桁は入れさせない
    /// (999cm の3つ目の「9」は出ない)。下限は打っている途中の「1」(→170)を止められないのでここでは見ない。
    static func correct(_ text: String, field: BodyField) -> (text: String, error: BodyInputError?) {
        let normalized = NumericInput.normalized(text)
        var digits = normalized.filter { $0.isASCII && $0.isNumber }
        var error: BodyInputError? = digits.count != normalized.count ? .invalidCharacter : nil

        while digits.hasPrefix("0") { digits.removeFirst() }

        if digits.count > 3 {
            digits = String(digits.prefix(3))
            error = .outOfRange
        }
        while let value = Int(digits), value > field.range.upperBound {
            digits.removeLast()
            error = .outOfRange
        }
        return (digits, error)
    }

    /// 欄を離れたとき・保存の前。空欄は誤りではない(任意)
    static func validate(_ text: String, field: BodyField) -> BodyInputError? {
        guard let value = Int(text) else { return nil }
        return field.range.contains(value) ? nil : .outOfRange
    }

    /// ドラムロールの初期値。入力が範囲内ならその値
    static func pickerStartValue(_ text: String, field: BodyField) -> Int {
        if let value = Int(text), field.range.contains(value) { return value }
        return field.pickerDefault
    }
}

/// 身体情報の保存(UserDefaults)と、初回の入力画面を通ったかどうか
nonisolated struct BodyInfoStore: @unchecked Sendable {
    static let shared = BodyInfoStore()

    private let defaults: UserDefaults
    private let infoKey = "bodyInfo"
    private let completedKey = "hasCompletedBodyInfoSetup"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    /// 初回の身体情報画面を通ったか(全部空欄で「次へ」でも true)
    var hasCompletedInitialSetup: Bool {
        get { defaults.bool(forKey: completedKey) }
        nonmutating set { defaults.set(newValue, forKey: completedKey) }
    }

    func load() -> BodyInfo {
        guard let data = defaults.data(forKey: infoKey),
              let info = try? JSONDecoder().decode(BodyInfo.self, from: data)
        else { return .empty }
        return info
    }

    func save(_ info: BodyInfo) throws {
        defaults.set(try JSONEncoder().encode(info), forKey: infoKey)
    }
}
