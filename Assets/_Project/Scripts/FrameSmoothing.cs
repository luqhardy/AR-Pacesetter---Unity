using System;

/// <summary>
/// フレーム時間に依存する平滑化を「長いフレームで瞬間移動しない」形にする純ロジック(依存ゼロ)。
///
/// <para><b>何が壊れていたか</b>: 位置追従は <c>Lerp(現在, 目標, Time.deltaTime * k)</c> で書かれていた。
/// Unityの <c>Lerp</c> は補間係数 t を [0,1] へ丸めるため、<b>1フレームが 1/k 秒を超えると
/// t が1に飽和し、補間ではなく目標への瞬間移動になる</b>。k=2.5 なら 400ms、
/// 追い抜き中の k=4.0 なら 250ms。</para>
///
/// <para><b>現実に起きている</b>: E2Eの計測では走行中の最大 <c>Time.deltaTime</c> が 330ms に達し、
/// 実際に 702ms(t=1.75)のフレームで飽和した。実機でもGCヒッチやARKitの再localizeで
/// 数百msのフレームは起こりうる。コーナー追従テストが3〜4回に1回
/// 「ワープ(最大フレーム跳び5.1m)」で落ちていたのはこれが原因。</para>
///
/// <para><b>直し方</b>: 平滑化に使う経過時間に上限を設ける(既定100ms = 10fps相当)。
/// 長いフレームではアバターが少し遅れるだけで、次のフレーム以降で自然に追いつく。
/// 通常のフレーム(100ms以下)では従来と完全に同一の挙動になる。</para>
/// </summary>
public static class FrameSmoothing
{
    /// <summary>平滑化に使う経過時間の上限(秒)。10fps相当。</summary>
    public const float MaxSmoothingDeltaSeconds = 0.1f;

    /// <summary>経過時間を平滑化用に丸める。</summary>
    public static float ClampDelta(float deltaSeconds, float maxSeconds = MaxSmoothingDeltaSeconds)
    {
        if (deltaSeconds <= 0f) return 0f;
        if (maxSeconds <= 0f) return deltaSeconds;
        return deltaSeconds < maxSeconds ? deltaSeconds : maxSeconds;
    }

    /// <summary>
    /// <c>Lerp</c> に渡す補間係数。経過時間を丸めたうえで [0,1] に収める。
    /// 通常フレームでは <c>deltaSeconds * ratePerSecond</c> と一致する。
    /// </summary>
    public static float Factor(float deltaSeconds, float ratePerSecond,
                               float maxSeconds = MaxSmoothingDeltaSeconds)
    {
        if (ratePerSecond <= 0f) return 0f;

        float t = ClampDelta(deltaSeconds, maxSeconds) * ratePerSecond;
        if (t < 0f) return 0f;
        return t > 1f ? 1f : t;
    }

    /// <summary>
    /// この経過時間・この追従速度で補間係数が飽和する(=瞬間移動になる)か。
    /// 上限を掛ける<b>前</b>の生の経過時間で判定する — 診断用。
    /// </summary>
    public static bool WouldSaturate(float deltaSeconds, float ratePerSecond)
        => ratePerSecond > 0f && deltaSeconds * ratePerSecond >= 1f;

    /// <summary>飽和し始めるフレーム時間(秒)。k=2.5 なら0.4秒。</summary>
    public static float SaturationThresholdSeconds(float ratePerSecond)
        => ratePerSecond > 0f ? 1f / ratePerSecond : float.PositiveInfinity;
}
