/// <summary>
/// Motion-to-Photon (M2P) 遅延の算出 (基本設計書 §10: 20ms以内・最大許容30ms)。
/// Unity非依存の純ロジック。
///
/// <para><b>何を測っているのか</b>: 厳密なM2Pには「光子がユーザーの目に届いた時刻」が要り、
/// ソフトウェアだけでは取得できない。代わりに測れるのは
/// <b>センサー時刻 → 提示予定時刻</b>で、これが実用上のM2P近似として一般的に使われる。</para>
///
/// <list type="bullet">
/// <item><b>センサー時刻</b>: ARKitフレームのタイムスタンプ
///   (AR Foundation の <c>ARCameraFrameEventArgs.timestampNs</c>)。
///   カメラ/IMUがその姿勢を観測した時刻。</item>
/// <item><b>提示予定時刻</b>: <c>CADisplayLink.targetTimestamp</c>。
///   いま描いているフレームがディスプレイへ出る予定時刻。</item>
/// </list>
///
/// <para>どちらも iOS の <c>CACurrentMediaTime()</c>(mach_absolute_time)と同じ時間軸なので
/// 直接引き算できる。**これが成立することがこの計算の前提**で、別クロック(例:
/// <c>Time.realtimeSinceStartup</c>)を混ぜると無意味な値になる。</para>
/// </summary>
public static class MotionToPhotonMath
{
    /// <summary>§10 の要求値(ms)。</summary>
    public const double BudgetMs = 20.0;

    /// <summary>§10 の最大許容値(ms)。これを超えたフレームは不合格。</summary>
    public const double MaxAllowedMs = 30.0;

    /// <summary>これを超える値はクロックのずれ・停止フレームとみなして棄却する(ms)。</summary>
    public const double ImplausibleMs = 1000.0;

    /// <summary>未計測を表す値。CSVの <c>latency_m2p</c> 列にもこの値を書く。</summary>
    public const double Unmeasured = -1.0;

    /// <summary>
    /// センサー時刻と提示予定時刻からM2P遅延(ms)を求める。
    /// </summary>
    /// <param name="sensorSeconds">ARKitフレームのタイムスタンプ(秒・CACurrentMediaTime軸)</param>
    /// <param name="presentSeconds">提示予定時刻(秒・同じ軸)</param>
    /// <param name="latencyMs">算出された遅延(ms)。失敗時は <see cref="Unmeasured"/></param>
    /// <returns>妥当な実測が得られたか</returns>
    public static bool TryComputeLatencyMs(double sensorSeconds, double presentSeconds,
                                           out double latencyMs)
    {
        latencyMs = Unmeasured;

        if (!IsUsable(sensorSeconds) || !IsUsable(presentSeconds)) return false;
        // 0以下は「まだ供給されていない」— プラグイン未リンク時に0が返る
        if (sensorSeconds <= 0.0 || presentSeconds <= 0.0) return false;

        double ms = (presentSeconds - sensorSeconds) * 1000.0;

        // 負 = 提示予定がセンサー時刻より前。時間軸が違うか、フレームの対応付けが壊れている
        if (ms < 0.0) return false;
        // 極端に大きい = 停止・スリープ明け。実測として記録すると平均を壊す
        if (ms > ImplausibleMs) return false;

        latencyMs = ms;
        return true;
    }

    /// <summary>§10 の要求(20ms以内)を満たすか。</summary>
    public static bool MeetsBudget(double latencyMs)
        => IsUsable(latencyMs) && latencyMs >= 0.0 && latencyMs <= BudgetMs;

    /// <summary>§10 の最大許容(30ms)以内か。</summary>
    public static bool WithinMaxAllowed(double latencyMs)
        => IsUsable(latencyMs) && latencyMs >= 0.0 && latencyMs <= MaxAllowedMs;

    private static bool IsUsable(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}

/// <summary>
/// M2P実測の集計 (走行1本ぶん)。§11.2「CSV解析で20msを評価する」の
/// 結果をアプリ内でも即座に言えるようにするための軽量な集計器。Unity非依存。
///
/// <para>平均だけでは合否を言えない — 20msを超えるフレームが何割あったかが本質なので、
/// 超過率と最悪値を持つ。</para>
/// </summary>
public class MotionToPhotonStats
{
    public int SampleCount { get; private set; }
    public double MaxMs { get; private set; }
    public double MinMs { get; private set; }

    private double _sumMs;
    private int _overBudget;    // > 20ms
    private int _overMaxAllowed; // > 30ms

    public MotionToPhotonStats() => Reset();

    public void Reset()
    {
        SampleCount = 0;
        _sumMs = 0.0;
        MaxMs = 0.0;
        MinMs = 0.0;
        _overBudget = 0;
        _overMaxAllowed = 0;
    }

    /// <summary>実測値を1件加える。未計測(<see cref="MotionToPhotonMath.Unmeasured"/>)は無視。</summary>
    public void Add(double latencyMs)
    {
        if (double.IsNaN(latencyMs) || double.IsInfinity(latencyMs)) return;
        if (latencyMs < 0.0) return; // 未計測

        if (SampleCount == 0)
        {
            MinMs = latencyMs;
            MaxMs = latencyMs;
        }
        else
        {
            if (latencyMs < MinMs) MinMs = latencyMs;
            if (latencyMs > MaxMs) MaxMs = latencyMs;
        }

        SampleCount++;
        _sumMs += latencyMs;
        if (!MotionToPhotonMath.MeetsBudget(latencyMs)) _overBudget++;
        if (!MotionToPhotonMath.WithinMaxAllowed(latencyMs)) _overMaxAllowed++;
    }

    public double AverageMs => SampleCount > 0 ? _sumMs / SampleCount : MotionToPhotonMath.Unmeasured;

    /// <summary>20ms(§10要求)を超えたフレームの割合 0〜1。実測が無ければ -1。</summary>
    public double OverBudgetRatio => SampleCount > 0 ? (double)_overBudget / SampleCount : -1.0;

    /// <summary>30ms(§10最大許容)を超えたフレームの割合 0〜1。実測が無ければ -1。</summary>
    public double OverMaxAllowedRatio => SampleCount > 0 ? (double)_overMaxAllowed / SampleCount : -1.0;

    /// <summary>ログ・結果画面用の1行要約。</summary>
    public string Summarize()
    {
        if (SampleCount == 0) return "M2P: 実測なし (未計測)";
        return $"M2P: 平均{AverageMs:F1}ms / 最大{MaxMs:F1}ms / 最小{MinMs:F1}ms / " +
               $"20ms超過{OverBudgetRatio * 100.0:F1}% / 30ms超過{OverMaxAllowedRatio * 100.0:F1}% " +
               $"({SampleCount}フレーム)";
    }
}
