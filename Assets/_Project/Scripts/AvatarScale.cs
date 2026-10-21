using System;

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

    // ── サイズの異常系(タスク表 ARグラス「サイズの異常系」2026-10-19) ─────────────

    /// <summary>
    /// 要求身長として受け付ける範囲(cm)。等身大のペーサーとして成り立つ大きさに限る。
    /// Swift の StartSession やデモ設定から 1cm・9999cm・NaN が来ても、豆粒や巨人にしない
    /// </summary>
    public const float MinRequestedHeightCm = 100f;
    public const float MaxRequestedHeightCm = 230f;

    /// <summary>
    /// グラスの画角に合わせた身長の上限の下限(cm)。前方距離が極端に短い構成で上限を素直に求めると
    /// 数十cmの人形になる — そこまで縮めるより頭か足元が少し欠けるほうがペーサーとして読める
    /// </summary>
    public const float MinHeightLimitCm = 100f;

    /// <summary>適用後の身長が目標からこの割合以上ずれたら異常(倍率の上下限に当たった等)</summary>
    public const float AppliedHeightTolerance = 0.05f;

    /// <summary>
    /// 要求身長を受け付ける範囲へ寄せる。数でない・0以下は既定(175cm)、範囲外は端へ寄せる。
    /// </summary>
    /// <param name="corrected">入力をそのまま使えなかったか(ログ・診断用)</param>
    public static float SanitizeRequestedHeightCm(float heightCm, out bool corrected)
    {
        if (!IsUsable(heightCm) || heightCm <= 0f)
        {
            corrected = true;
            return BaselineHeightCm;
        }
        float clamped = heightCm < MinRequestedHeightCm ? MinRequestedHeightCm
                      : (heightCm > MaxRequestedHeightCm ? MaxRequestedHeightCm : heightCm);
        corrected = clamped != heightCm;
        return clamped;
    }

    /// <summary>
    /// グラスの画角で全身が収まる最大身長(m)から、アバターへ設定する上限(cm)を決める。
    /// 0 は上限なし(計算できない構成)。下限 <see cref="MinHeightLimitCm"/> を割らない。
    /// </summary>
    public static float ResolveHeightLimitCm(double fitHeightMeters, double marginMeters)
    {
        double limited = fitHeightMeters - marginMeters;
        if (double.IsNaN(limited) || double.IsInfinity(limited) || limited <= 0.0) return 0f;
        return (float)Math.Max(limited * 100.0, MinHeightLimitCm);
    }

    /// <summary>
    /// 適用した倍率で実際にその身長になったか。倍率が <see cref="MinScale"/>/<see cref="MaxScale"/> に
    /// 当たった(モデルの素の大きさが桁外れ)ときは false — 呼び出し側は異常として知らせる
    /// </summary>
    public static bool IsAppliedHeightAcceptable(float unitHeightMeters, float scale, float targetHeightCm)
    {
        if (!IsUsable(unitHeightMeters) || !IsUsable(scale) || !IsUsable(targetHeightCm) || targetHeightCm <= 0f)
            return false;
        float achievedCm = unitHeightMeters * scale * 100f;
        return Math.Abs(achievedCm - targetHeightCm) <= targetHeightCm * AppliedHeightTolerance;
    }

    /// <summary>
    /// 演出(起動のスケールイン)の外でルートの倍率が本来の値からずれているか。
    /// スケールインの途中でアバターごと非アクティブになる(GPSロストのスタンバイ等)とコルーチンが止まり、
    /// 0.15倍などの途中の大きさのまま走り続けていた — これを検知して戻す
    /// </summary>
    public static bool IsRootScaleDeviated(float currentUniformScale, float expectedUniformScale,
                                           float relativeTolerance = 0.01f)
    {
        if (!IsUsable(expectedUniformScale) || expectedUniformScale <= 0f) return false;
        if (!IsUsable(currentUniformScale)) return true;
        return Math.Abs(currentUniformScale - expectedUniformScale) > expectedUniformScale * relativeTolerance;
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
