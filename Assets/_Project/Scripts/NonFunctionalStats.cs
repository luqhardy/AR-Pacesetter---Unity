/// <summary>
/// 位置精度の集計 (基本設計書 §10: 位置誤差 1.0m以内)。Unity非依存の純ロジック。
///
/// <para>ここでいう位置誤差は<b>リード距離の誤差</b> — アバターは常にユーザーの
/// 3.0m前方(F-03)にいるべきなので、実際の水平距離と目標との差がそのまま
/// 「意図した位置にどれだけ置けているか」になる。外部の基準点を持たない
/// 単体アプリで §10 を定量化できる唯一の量。</para>
///
/// <para>平均だけでは合否を言えないので、<b>許容を超えたフレームの割合</b>と
/// 最悪値も持つ。</para>
/// </summary>
public class PositionAccuracyStats
{
    /// <summary>§10 の許容誤差(m)。</summary>
    public const float ToleranceMeters = 1.0f;

    public int SampleCount { get; private set; }
    public float MaxErrorMeters { get; private set; }

    private double _sumMeters;
    private int _overTolerance;

    public PositionAccuracyStats() => Reset();

    public void Reset()
    {
        SampleCount = 0;
        MaxErrorMeters = 0f;
        _sumMeters = 0.0;
        _overTolerance = 0;
    }

    /// <summary>誤差(絶対値・m)を1件加える。負値・NaNは無視。</summary>
    public void Add(float errorMeters)
    {
        if (float.IsNaN(errorMeters) || float.IsInfinity(errorMeters)) return;
        if (errorMeters < 0f) return;

        SampleCount++;
        _sumMeters += errorMeters;
        if (errorMeters > MaxErrorMeters) MaxErrorMeters = errorMeters;
        if (errorMeters > ToleranceMeters) _overTolerance++;
    }

    /// <summary>平均誤差(m)。実測が無ければ -1。</summary>
    public float AverageErrorMeters => SampleCount > 0 ? (float)(_sumMeters / SampleCount) : -1f;

    /// <summary>許容(1.0m)を超えたフレームの割合 0〜1。実測が無ければ -1。</summary>
    public float OverToleranceRatio => SampleCount > 0 ? (float)_overTolerance / SampleCount : -1f;

    /// <summary>平均が許容以内か(実測が無ければ false)。</summary>
    public bool MeetsRequirement => SampleCount > 0 && AverageErrorMeters <= ToleranceMeters;

    public string Summarize()
    {
        if (SampleCount == 0) return "位置誤差: 実測なし";
        return $"位置誤差: 平均{AverageErrorMeters:F2}m / 最大{MaxErrorMeters:F2}m / " +
               $"1.0m超過{OverToleranceRatio * 100f:F1}% ({SampleCount}サンプル)";
    }
}

/// <summary>
/// 連続稼働の見積り (基本設計書 §10: 60分連続稼働でバッテリー30%以上維持)。
/// Unity非依存の純ロジック。
///
/// <para>60分走らないと測れない要求を毎回60分測るのは現実的でないため、
/// <b>短い走行の消費率から60分後を外挿</b>する。外挿である以上、
/// 十分な時間を測っていなければ「判定不能」と言わせる
/// (<see cref="IsReliableSampleDuration"/>) — 短すぎる区間の外挿は当たらない。</para>
/// </summary>
public static class BatteryEnduranceMath
{
    /// <summary>§10 の連続稼働要求(分)。</summary>
    public const float RequiredMinutes = 60f;

    /// <summary>§10 の残量要求(0〜1)。</summary>
    public const float RequiredRemaining01 = 0.30f;

    /// <summary>これ未満の計測時間では外挿を信用しない(秒)。</summary>
    public const float MinReliableSampleSeconds = 120f;

    /// <summary>外挿に足る時間を測ったか。</summary>
    public static bool IsReliableSampleDuration(float elapsedSeconds)
        => !float.IsNaN(elapsedSeconds) && elapsedSeconds >= MinReliableSampleSeconds;

    /// <summary>
    /// 1時間あたりの消費率(0〜1)を求める。バッテリー残量が取れない端末
    /// (<c>SystemInfo.batteryLevel</c> が負)や時間が0の場合は false。
    /// </summary>
    public static bool TryComputeDrainPerHour(float startLevel01, float endLevel01,
                                              float elapsedSeconds, out float drainPerHour)
    {
        drainPerHour = -1f;

        if (!IsLevelUsable(startLevel01) || !IsLevelUsable(endLevel01)) return false;
        if (float.IsNaN(elapsedSeconds) || elapsedSeconds <= 0f) return false;

        float drained = startLevel01 - endLevel01;
        if (drained < 0f) drained = 0f; // 充電しながらの走行。消費0として扱う

        drainPerHour = drained * (3600f / elapsedSeconds);
        return true;
    }

    /// <summary>この消費率のまま <paramref name="minutes"/> 分走った後の残量(0〜1)。</summary>
    public static float ProjectLevelAfterMinutes(float currentLevel01, float drainPerHour, float minutes)
    {
        if (!IsLevelUsable(currentLevel01) || drainPerHour < 0f) return -1f;

        float projected = currentLevel01 - drainPerHour * (minutes / 60f);
        return projected < 0f ? 0f : projected;
    }

    /// <summary>
    /// §10 を満たすか: <b>満充電から60分走って30%以上残る</b>消費率か。
    /// つまり 1時間あたりの消費が70%以下であること。
    /// </summary>
    public static bool MeetsEnduranceRequirement(float drainPerHour)
    {
        if (drainPerHour < 0f || float.IsNaN(drainPerHour)) return false;
        return ProjectLevelAfterMinutes(1.0f, drainPerHour, RequiredMinutes) >= RequiredRemaining01;
    }

    private static bool IsLevelUsable(float level01)
        => !float.IsNaN(level01) && !float.IsInfinity(level01) && level01 >= 0f && level01 <= 1f;
}
