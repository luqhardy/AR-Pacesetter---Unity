/// <summary>
/// F-09/F-10 GPSロストの判定と、判定結果からのFSM遷移の決定 (基本設計書 §8.1 / AGENTS.md §5)。
/// Unity非依存の純ロジック。<see cref="GpsSignalMonitor"/> が委譲する。
///
/// <para><b>以前の不具合</b>: 監視は InertialMovement からしか復帰させていなかった。
/// FadeOut→Normal と Standby→ReAccumulation はエディタのRキーにしか無く、実機では
/// 5秒を超えるロストの後、<b>アバターがその走行中ずっと戻らなかった</b>
/// (HUDの赤字警告もSwiftの「GPS喪失」も出たまま)。復帰の判断をここへ集め、
/// 状態ごとに何をするかを表で決める。</para>
///
/// <para><b>ロストと復帰は別のしきい値</b>(ヒステリシス): 精度10m以上 or 1.5秒途絶でロスト、
/// 新鮮な測位で精度5m以内になって初めて復帰。5〜10mの間はどちらにも動かない —
/// 境界でアバターが出たり消えたりしないため。</para>
/// </summary>
public static class GpsSignalPolicy
{
    /// <summary>判断に必要なFSMの局面。Standby は原因で分ける(グラス切断のスタンバイはGPSで戻さない — §8.3)。</summary>
    public enum Phase
    {
        Normal,
        InertialMovement,
        FadeOut,
        /// <summary>F-10のフェード完了によるスタンバイ。GPS復帰で ReAccumulation へ進む。</summary>
        StandbyAfterGpsLoss,
        /// <summary>
        /// START の時点でGPSが使えず、HUDだけで始めたスタンバイ(<see cref="ShouldStartHudOnly"/>)。
        /// GPSロスト後のスタンバイと同じく、復帰で ReAccumulation へ進んでアバターが現れる。
        /// </summary>
        StandbyAwaitingGps,
        /// <summary>それ以外(グラス切断・低バッテリーのスタンバイ、ReAccumulation中など)。監視は触らない。</summary>
        Other
    }

    public enum Action
    {
        None,
        EnterInertialMovement,
        ReturnToNormal,
        BeginReaccumulation
    }

    /// <summary>
    /// ロスト判定(§8.1): 更新が <paramref name="staleTimeoutSeconds"/> 以上途絶、
    /// または水平精度が <paramref name="lostThresholdMeters"/> 以上。
    /// 良好な測位を一度も得ていない間は(既定で)ロストにしない — 掴んでいない信号は失えない。
    /// </summary>
    public static bool IsLost(bool hasHadGoodFix, bool requireInitialFix,
                              float secondsSinceUpdate, float staleTimeoutSeconds,
                              float accuracyMeters, float lostThresholdMeters)
    {
        if (requireInitialFix && !hasHadGoodFix) return false;
        return secondsSinceUpdate >= staleTimeoutSeconds || accuracyMeters >= lostThresholdMeters;
    }

    /// <summary>
    /// 復帰判定: 新鮮な測位があり、かつ精度が <paramref name="recoveredThresholdMeters"/> 以内。
    /// </summary>
    public static bool IsRecovered(float secondsSinceUpdate, float staleTimeoutSeconds,
                                   float accuracyMeters, float recoveredThresholdMeters)
    {
        return secondsSinceUpdate < staleTimeoutSeconds
            && accuracyMeters >= 0f
            && accuracyMeters <= recoveredThresholdMeters;
    }

    /// <summary>
    /// 走行開始(START)の時点でGPSが使えなければ、アバターを出さずHUDだけで始めるか
    /// (タスク表 ARグラス「GPSが使えない場合はHUDのみ」2026-10-19・AR設定のスイッチ・<b>既定OFF</b>)。
    /// 使えない = 新鮮な測位が無い、または精度がロストのしきい値(10m)以上。
    ///
    /// <para>既定(OFF)では従来どおり、良好な測位を一度も得ていない間はロスト判定せずアバターを出す
    /// (<see cref="IsLost"/> の requireInitialFix)。GPSロストの自動判定を止めている間(屋内テスト)は
    /// 復帰を判定する者がいない — HUDだけで始めると走行中ずっとアバターが出ないので、始めない。</para>
    /// </summary>
    public static bool ShouldStartHudOnly(bool optedIn, bool lostHandlingEnabled,
                                          float secondsSinceUpdate, float staleTimeoutSeconds,
                                          float accuracyMeters, float lostThresholdMeters)
    {
        if (!optedIn || !lostHandlingEnabled) return false;
        bool fresh = secondsSinceUpdate < staleTimeoutSeconds; // NaN・未受信(∞)は新鮮でない
        return !fresh || accuracyMeters < 0f || accuracyMeters >= lostThresholdMeters;
    }

    /// <summary>局面と判定結果から、監視が起こすべき遷移を決める(AGENTS.md §5 の図そのもの)。</summary>
    public static Action Decide(Phase phase, bool lost, bool recovered)
    {
        switch (phase)
        {
            case Phase.Normal:
                return lost ? Action.EnterInertialMovement : Action.None;
            case Phase.InertialMovement:
            case Phase.FadeOut: // 1秒のフェード完了前に戻れば Normal へ直接
                return recovered ? Action.ReturnToNormal : Action.None;
            case Phase.StandbyAfterGpsLoss:
            case Phase.StandbyAwaitingGps:
                return recovered ? Action.BeginReaccumulation : Action.None;
            default:
                return Action.None;
        }
    }
}
