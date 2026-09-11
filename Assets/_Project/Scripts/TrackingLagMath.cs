/// <summary>
/// 追従の<b>定常遅れ</b>の補正 (§10 位置誤差1.0m以内 / F-03 3.0m前方維持)。
/// Unity非依存の純ロジック。
///
/// <para><b>問題</b>: アバターの位置は指数平滑 <c>p += (target − p) · dt · k</c> で
/// 目標アンカー(ユーザーの3.0m前方)へ寄せている。手ブレを消すために必要な処理だが、
/// <b>目標が動き続けている間は必ず一定量だけ後ろに取り残される</b>。
/// 目標が速度 v で動くとき、定常状態では</para>
///
/// <code>v · dt = (target − p) · dt · k  ⇒  target − p = v / k</code>
///
/// <para>つまり遅れは <b>v / k</b>。k = 2.5 で 3.6m/s(≒4分37秒/km)なら <b>1.44m</b> —
/// アバターは3.0m前方ではなく実質1.6m前方に居続けることになり、
/// §10の位置誤差1.0mを走行速度域で常に超える。フレームレートにも
/// 手ブレの大きさにも依存しない、**構造的な**ずれである。</para>
///
/// <para><b>対処</b>: 遅れる量が分かっているのだから、その分だけ目標を先回りさせる。
/// 平滑の強さ(k)は変えないので手ブレ除去性能は落ちず、定常成分だけが消える。
/// 過渡応答(急な方向転換など)は従来どおり k の速さで追従する。</para>
/// </summary>
public static class TrackingLagMath
{
    /// <summary>先回り量の上限(m)。速度スパイクで飛びすぎないための安全弁。</summary>
    public const float DefaultMaxFeedForwardMeters = 3.0f;

    /// <summary>
    /// 指数平滑が速度 <paramref name="targetSpeedMetersPerSecond"/> の目標を追うときの
    /// 定常遅れ(m) = v / k。止まっている・平滑が無効なら0。
    /// </summary>
    public static float SteadyStateLagMeters(float targetSpeedMetersPerSecond, float lerpSpeed)
    {
        if (!IsUsable(targetSpeedMetersPerSecond) || !IsUsable(lerpSpeed)) return 0f;
        if (targetSpeedMetersPerSecond <= 0f || lerpSpeed <= 0f) return 0f;

        return targetSpeedMetersPerSecond / lerpSpeed;
    }

    /// <summary>
    /// 定常遅れを打ち消すために目標を進める量(m)。上限で頭打ちにする。
    /// </summary>
    public static float FeedForwardMeters(float targetSpeedMetersPerSecond, float lerpSpeed,
                                          float maxMeters)
    {
        if (!IsUsable(maxMeters) || maxMeters <= 0f) return 0f;

        float lag = SteadyStateLagMeters(targetSpeedMetersPerSecond, lerpSpeed);
        return lag > maxMeters ? maxMeters : lag;
    }

    /// <summary>既定の上限での先回り量。</summary>
    public static float FeedForwardMeters(float targetSpeedMetersPerSecond, float lerpSpeed)
        => FeedForwardMeters(targetSpeedMetersPerSecond, lerpSpeed, DefaultMaxFeedForwardMeters);

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
