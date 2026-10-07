/// <summary>
/// 展示ブース用「立ったまま体験」の台本(Kobe Calling)。Unity非依存の純ロジック。
///
/// <para>来場者は実物のARグラスを掛けてその場に立つ。走るのは入力だけ — ペース・距離を
/// 時刻の関数として作り、本番と同じブリッジ経路へ流す(<c>BoothDemoController</c>)。
/// 描画・色・オーラは本番の実装がそのまま反応する。</para>
///
/// <para>立ったままではユーザーとアバターの距離が変わらず、色は常に「ジャスト(緑)」になる。
/// そこで「遅れた」「追い抜いた」を見せる区間だけ、アバターの追従位置を前後へずらす
/// (<see cref="LeadOffsetAt"/>。色の判定基準の3.0mはずらさない)。</para>
///
/// <para><b>アバターは体験中に一度も消さない</b>(2026-10-07 チーム判断)。以前は GPSロスト
/// (F-09 慣性 → F-10 フェードアウト → 警告)の区間を見せていたが、来場者には
/// 「アバターが消えた=壊れた」に見えた。GPSは常に良好として流す。走行本番の F-10 は仕様どおり。</para>
///
/// 時刻 t はカウントダウンの START からの経過秒。
/// </summary>
public static class BoothDemoScript
{
    public enum Beat
    {
        OnPace,         // ジャスト(緑)
        FallingBehind,  // ランナーが遅れる → アバターが離れる(橙→赤・オーラ)、HUDペース赤
        CatchingUp,     // 追い上げてジャストへ戻る
        Overtaking,     // ランナーが追い抜きかける → アバターが寄って速まる(緑→青)
        Settling,       // ジャストへ戻る
        FinalStretch,   // ゴールラインが見える最後の直線
        Finished
    }

    // ── 区間の境界(秒) ────────────────────────────────────────────────────
    public const float FallingBehindStart = 10f;
    public const float CatchingUpStart    = 22f;
    public const float OvertakingStart    = 28f;
    public const float SettlingStart      = 35f;
    public const float FinalStretchStart  = 40f;
    public const float TotalSeconds       = 52f;

    // ── アバター位置のずらし(m)。目標リード3.0mへの加算 ──────────────────────
    /// <summary>遅れ区間の最大。リード9.0m: 色は赤(4.5m+3m以上)、オーラ発動(遅延5m以上)。
    /// 離隔待機(10m)には届かせない — 届くと手招きに変わる</summary>
    public const float BehindLeadOffsetMeters = 6.0f;
    /// <summary>追い抜き区間。リード1.2m: 色は緑→青側(1.5m未満)。0m以下にすると
    /// アバターがグラスの視野から消えるので、青一色までは振らない。
    /// 本番の「右へ譲る」動作(0.8m横へ)は使わない — 1.2m先で0.8m横へ出ると約34°外れ、
    /// グラスの水平画角の外へ出てしまう</summary>
    public const float OvertakeLeadOffsetMeters = -1.8f;

    private const float BehindRampSeconds = 4f;
    private const float CatchUpRampSeconds = 4f;
    private const float OvertakeRampSeconds = 2f;
    private const float SettleRampSeconds = 3f;

    // ── 区間ごとの実ペース(目標ペースに対する速度比) ────────────────────────
    // HUD右上の現在ペースは PaceHudDisplay が目標との比較で赤/緑にする
    public const float BehindPaceRatio = 0.85f;
    public const float CatchUpPaceRatio = 1.10f;
    public const float OvertakePaceRatio = 1.15f;

    public static Beat BeatAt(float t)
    {
        if (t < FallingBehindStart) return Beat.OnPace;
        if (t < CatchingUpStart)    return Beat.FallingBehind;
        if (t < OvertakingStart)    return Beat.CatchingUp;
        if (t < SettlingStart)      return Beat.Overtaking;
        if (t < FinalStretchStart)  return Beat.Settling;
        if (t < TotalSeconds)       return Beat.FinalStretch;
        return Beat.Finished;
    }

    /// <summary>
    /// アバター追従位置の前後ずらし(m、正=前へ)。区間の境界で連続になるよう
    /// smoothstep でつなぐ — 段差があるとアバターが瞬間移動して見える。
    /// </summary>
    public static float LeadOffsetAt(float t)
    {
        switch (BeatAt(t))
        {
            case Beat.FallingBehind:
                return BehindLeadOffsetMeters * Ramp(t - FallingBehindStart, BehindRampSeconds);
            case Beat.CatchingUp:
                return BehindLeadOffsetMeters * (1f - Ramp(t - CatchingUpStart, CatchUpRampSeconds));
            case Beat.Overtaking:
                return OvertakeLeadOffsetMeters * Ramp(t - OvertakingStart, OvertakeRampSeconds);
            case Beat.Settling:
                return OvertakeLeadOffsetMeters * (1f - Ramp(t - SettlingStart, SettleRampSeconds));
            default:
                return 0f;
        }
    }

    /// <summary>目標ペースに対する実ペースの速度比。</summary>
    public static float PaceRatioAt(float t) => PaceRatioOf(BeatAt(t));

    /// <summary>START からの走行距離(m)。速度比の区分定数を積分する。</summary>
    public static float DistanceAt(float t, float targetSpeedMetersPerSecond)
    {
        if (t <= 0f || targetSpeedMetersPerSecond <= 0f) return 0f;
        if (t > TotalSeconds) t = TotalSeconds;

        float meters = 0f;
        float segmentStart = 0f;
        for (int i = 0; i < SegmentEnds.Length && segmentStart < t; i++)
        {
            float segmentEnd = SegmentEnds[i] < t ? SegmentEnds[i] : t;
            meters += (segmentEnd - segmentStart) * PaceRatioOf(BeatAt(segmentStart));
            segmentStart = SegmentEnds[i];
        }
        return meters * targetSpeedMetersPerSecond;
    }

    /// <summary>台本を最後まで走ったときの距離(m)= ゴール距離。</summary>
    public static float RouteDistanceMeters(float targetSpeedMetersPerSecond)
        => DistanceAt(TotalSeconds, targetSpeedMetersPerSecond);

    private static readonly float[] SegmentEnds =
    {
        FallingBehindStart, CatchingUpStart, OvertakingStart, SettlingStart,
        FinalStretchStart, TotalSeconds
    };

    private static float PaceRatioOf(Beat beat)
    {
        switch (beat)
        {
            case Beat.FallingBehind: return BehindPaceRatio;
            case Beat.CatchingUp:    return CatchUpPaceRatio;
            case Beat.Overtaking:    return OvertakePaceRatio;
            default:                 return 1f;
        }
    }

    // 0→1 の smoothstep。始点・終点で傾き0なので区間の継ぎ目で速度も跳ねない
    private static float Ramp(float elapsed, float duration)
    {
        if (duration <= 0f) return 1f;
        float x = elapsed / duration;
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        return x * x * (3f - 2f * x);
    }
}
