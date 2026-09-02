using System.Globalization;

/// <summary>
/// F-07 右上「現在ペース」表示の純ロジック (Unity非依存)。
///
/// 設計書 F-07 は右上に<b>現在</b>ペースを出し、遅れ=赤 / 維持=緑 で示すと定める。
/// 従来のHUDは目標ペース(定数)を表示していたため、走行中に何のフィードバックにも
/// なっていなかった。ここでは速度→ペース換算・遅延判定・表示整形のみを担い、
/// 色の適用と値の供給元選択は MonoBehaviour 側が行う。
/// </summary>
public static class PaceHudDisplay
{
    public enum PaceState
    {
        /// <summary>ペース不明(停止中・実測なし)。中立色で表示する。</summary>
        Unknown = 0,
        /// <summary>目標を維持できている。緑。</summary>
        Maintaining = 1,
        /// <summary>目標より遅れている。赤。</summary>
        Behind = 2,
    }

    /// <summary>目標より何割遅いと「遅れ」とみなすか。</summary>
    public const float DefaultBehindTolerance = 0.05f;

    /// <summary>これ未満の速度はペース算出不能(停止中)として扱う。約1.1km/h。</summary>
    public const float MinimumTrackedSpeedMetersPerSecond = 0.3f;

    /// <summary>表示上限。これ以上遅いペースは「--」扱いにする(分/km)。</summary>
    public const float UndisplayablePaceMinutesPerKm = 100f;

    /// <summary>速度(m/s)をペース(分/km)へ。停止中は正の無限大を返す。</summary>
    public static float SpeedToPaceMinutesPerKm(float metersPerSecond)
    {
        if (!IsUsable(metersPerSecond) || metersPerSecond < MinimumTrackedSpeedMetersPerSecond)
            return float.PositiveInfinity;

        return 1000f / metersPerSecond / 60f;
    }

    /// <summary>km/h をペース(分/km)へ。ブリッジ境界がkm/hのため用意している。</summary>
    public static float KmhToPaceMinutesPerKm(float kilometersPerHour)
        => SpeedToPaceMinutesPerKm(kilometersPerHour / 3.6f);

    /// <summary>現在ペースと目標ペースから表示状態を判定する。</summary>
    public static PaceState Evaluate(float currentPaceMinutesPerKm,
                                     float targetPaceMinutesPerKm,
                                     float behindTolerance)
    {
        if (!IsUsable(currentPaceMinutesPerKm) || currentPaceMinutesPerKm <= 0f)
            return PaceState.Unknown;
        if (!IsUsable(targetPaceMinutesPerKm) || targetPaceMinutesPerKm <= 0f)
            return PaceState.Unknown;

        float tolerance = behindTolerance < 0f ? 0f : behindTolerance;
        float limit = targetPaceMinutesPerKm * (1f + tolerance);

        // ペースは小さいほど速い。目標+許容より大きい = 遅れている
        return currentPaceMinutesPerKm > limit ? PaceState.Behind : PaceState.Maintaining;
    }

    /// <summary>ペース(分/km)を M'SS"/km へ整形する。算出不能時は --'--"/km。</summary>
    public static string Format(float paceMinutesPerKm)
    {
        if (!IsUsable(paceMinutesPerKm)
            || paceMinutesPerKm <= 0f
            || paceMinutesPerKm >= UndisplayablePaceMinutesPerKm)
        {
            return "--'--\"/km";
        }

        // 秒へ丸めてから分解する(4.9999分を 4'60" と出さないため)
        int totalSeconds = (int)System.Math.Round(paceMinutesPerKm * 60.0);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return minutes.ToString(CultureInfo.InvariantCulture)
             + "'" + seconds.ToString("00", CultureInfo.InvariantCulture)
             + "\"/km";
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
