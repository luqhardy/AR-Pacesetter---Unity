/// <summary>
/// 同期率(AGENTS.md §4.3)の平均。<b>時間で重み付けし、double で積算する</b>。Unity非依存の純ロジック。
/// <see cref="AnalyticsManager"/> が走行全体と1km区間の集計に使う。
///
/// <para><b>以前の不具合</b>(2点):</para>
/// <para>① フレームごとの値を float で足していた。60分・60fpsで約21万フレーム、合計は約2,000万に達し、
/// float の有効桁(2^24 ≒ 1,677万)を超えると1回の加算が2単位に丸められる。</para>
/// <para>② フレーム数で割っていたため、フレームが落ちた区間(発熱・GPSロスト時など)ほど
/// 平均への寄与が小さかった。走行の評価(S〜D)は「時間あたり」でなければならない。</para>
/// </summary>
public sealed class SyncAverage
{
    private double _weightedSum;
    private double _seconds;

    /// <summary>1つでも有効なサンプルを積んだか。</summary>
    public bool HasSamples => _seconds > 0.0;

    /// <summary>積算した時間(秒)。</summary>
    public double Seconds => _seconds;

    /// <summary>時間重み付きの平均同期率(%)。サンプルが無ければ0。</summary>
    public float Average => _seconds > 0.0 ? (float)(_weightedSum / _seconds) : 0f;

    /// <summary>同期率 <paramref name="syncPercent"/> が <paramref name="deltaSeconds"/> 秒続いたとして積む。</summary>
    public void Add(float syncPercent, float deltaSeconds)
    {
        if (!(deltaSeconds > 0f) || float.IsNaN(syncPercent) || float.IsInfinity(syncPercent)) return;
        _weightedSum += (double)syncPercent * deltaSeconds;
        _seconds += deltaSeconds;
    }

    public void Reset()
    {
        _weightedSum = 0.0;
        _seconds = 0.0;
    }
}
