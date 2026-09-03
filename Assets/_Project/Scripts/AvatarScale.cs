/// <summary>
/// アバターを実寸へ合わせるスケール計算 (企画書 §4.1 身長スケール)。Unity非依存の純ロジック。
///
/// 従来は `localScale = 指定身長cm / 175` としていたが、これは
/// **「モデルはスケール1のとき正確に175cmである」という検証されていない前提**に立っている。
/// FBXの単位(Mixamoはcm原点)やインポート設定(useFileScale)次第で実寸は簡単にずれ、
/// ずれたままでも指定175cm → scale 1.0 となるため誰も気づかない。
/// 実際に「アバターが実物より大きい」状態になっていた。
///
/// ここでは**実測した描画高さ**から必要な倍率を逆算する。モデルを差し替えても
/// インポート設定が変わっても、指定した身長どおりに表示される。
/// </summary>
public static class AvatarScale
{
    /// <summary>企画書 §4.1 の基準身長(cm)。指定が無いときの既定値。</summary>
    public const float BaselineHeightCm = 175f;

    /// <summary>これ未満の実測高さは計測失敗とみなす(m)。</summary>
    public const float MinMeasuredHeightMeters = 0.01f;

    // 異常値でアバターを消し飛ばさないための保険。通常はここに当たらない
    public const float MinScale = 0.001f;
    public const float MaxScale = 1000f;

    /// <summary>
    /// 実測値から目標身長に必要な localScale を求める。
    /// </summary>
    /// <param name="measuredHeightMeters">現在の <paramref name="currentScale"/> における実測身長(m)</param>
    /// <param name="currentScale">計測時に適用されていた一様スケール</param>
    /// <param name="targetHeightCm">目標身長(cm)</param>
    /// <param name="scale">求めた一様スケール。失敗時は currentScale のまま</param>
    /// <returns>計算できたか。入力が不正なら false(呼び出し側はスケールを変えない)</returns>
    public static bool TryComputeScale(float measuredHeightMeters, float currentScale,
                                       float targetHeightCm, out float scale)
    {
        scale = currentScale;

        if (!IsUsable(measuredHeightMeters) || measuredHeightMeters < MinMeasuredHeightMeters)
            return false;
        if (!IsUsable(currentScale) || currentScale <= 0f)
            return false;
        if (!IsUsable(targetHeightCm) || targetHeightCm <= 0f)
            return false;

        // スケール1のときの素の高さへ戻してから、目標身長に必要な倍率を出す
        float unitHeight = measuredHeightMeters / currentScale;
        if (unitHeight < 0.0001f)
            return false;

        float target = targetHeightCm / 100f;
        float computed = target / unitHeight;

        if (!IsUsable(computed) || computed <= 0f)
            return false;

        scale = computed < MinScale ? MinScale : (computed > MaxScale ? MaxScale : computed);
        return true;
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
