using System;

/// <summary>
/// 自走モード(タスク表 ARグラス「指定した速さで走る」2026-10-19・<b>既定OFF</b>)の前方距離。依存ゼロの純ロジック
/// (Tests/UnitTests でテストする)。
///
/// <para>既定ではアバターは常に「走者+3m」に付いていく(AGENTS.md §6)— 遅れても離れず、色とオーラで知らせるだけ。
/// 自走モードでは、アバターは目標ペースで走ったときの位置にいる: 走者が遅れた分だけ前へ離れ、
/// 速ければ近づく。遅れ(m)= START から実際に走った距離 − 目標ペースで進んだ距離(<see cref="SelfPacedTarget"/>)。
/// シンクロ率の距離偏差(速さの積分)は使わない — 平滑化した速さの立ち上がりの遅れが「時定数×速さ」の
/// 偏りとして残り、目標ペースどおりでも数m前に出てしまう(2026-10-19 の E2E で 8.2m)。</para>
///
/// <para>前方距離は 0.8〜9.5m に収める: 0.8m より寄るとグラスの視野から外れ、10m 以上離れると
/// 「離隔待機」(走者を待って止まる)に入ってしまう。9.5m で止まっている間も、色(赤)・オーラ・手招きで
/// 遅れを知らせる。</para>
/// </summary>
public static class SelfPacedLead
{
    /// <summary>これより寄るとグラスの視野から外れる(展示デモのずらしと同じ値)</summary>
    public const float MinDisplayedLeadMeters = 0.8f;

    /// <summary>離隔待機(10m)に入らない上限</summary>
    public const float MaxDisplayedLeadMeters = 9.5f;

    /// <summary>
    /// 目標リードと距離偏差から、アバターを置く前方距離(m)。
    /// </summary>
    /// <param name="targetLeadMeters">目標リード(既定 3.0m)</param>
    /// <param name="distanceDeviationMeters">走者の累積距離偏差(実測 − 目標ペースの距離。遅れは負)</param>
    public static float DisplayedLead(float targetLeadMeters, float distanceDeviationMeters)
    {
        if (float.IsNaN(targetLeadMeters) || float.IsInfinity(targetLeadMeters)) targetLeadMeters = 3.0f;
        float deviation = float.IsNaN(distanceDeviationMeters) || float.IsInfinity(distanceDeviationMeters)
            ? 0f : distanceDeviationMeters;
        float lead = targetLeadMeters - deviation;
        return Math.Max(MinDisplayedLeadMeters, Math.Min(MaxDisplayedLeadMeters, lead));
    }

    /// <summary>目標リードからのずらし(m、正 = 前へ)。AvatarEngine が目標リードに足す</summary>
    public static float LeadOffset(float targetLeadMeters, float distanceDeviationMeters)
        => DisplayedLead(targetLeadMeters, distanceDeviationMeters) - targetLeadMeters;
}

/// <summary>
/// 自走モードで、アバター(目標ペース)が START から進んだ距離。目標ペースは途中で変わってもよい(ゴースト)。
/// 毎フレームの経過時間で積算し、スタンバイ(アバターが非アクティブ)やアプリの停止で数えられなかった時間は
/// 壁時計との差で足す(<see cref="MissedSeconds"/>)— その間もアバターは走り続けていた扱いにする。
/// </summary>
public sealed class SelfPacedTarget
{
    /// <summary>目標ペースで START から進んだ距離(m)</summary>
    public double TargetDistanceMeters { get; private set; }

    /// <summary>壁時計の空きがこれ以上あれば、数えられなかった時間とみなして足す(フレームの揺れは足さない)</summary>
    public const double MissedThresholdSeconds = 1.0;

    public void Reset() => TargetDistanceMeters = 0.0;

    /// <summary>目標の速さ(m/s)で seconds 秒ぶん進める。数でない・負は無視</summary>
    public void Add(double seconds, double targetSpeedMetersPerSecond)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0) return;
        if (double.IsNaN(targetSpeedMetersPerSecond) || double.IsInfinity(targetSpeedMetersPerSecond)
            || targetSpeedMetersPerSecond <= 0.0) return;
        TargetDistanceMeters += seconds * targetSpeedMetersPerSecond;
    }

    /// <summary>
    /// 前のフレームからの壁時計の空き(秒)のうち、このフレームの経過時間で数えられなかった分(ゲーム内の秒)。
    /// スタンバイでアバターが非アクティブだった・アプリが止まっていた時間。普段のフレームでは 0
    /// </summary>
    /// <param name="realGapSeconds">前に数えたときからの壁時計の経過秒</param>
    /// <param name="frameDeltaSeconds">このフレームの Time.deltaTime</param>
    /// <param name="timeScale">Time.timeScale(実機は1。E2E は時間を早回しする)</param>
    public static double MissedSeconds(double realGapSeconds, double frameDeltaSeconds, double timeScale)
    {
        if (double.IsNaN(realGapSeconds) || realGapSeconds <= 0.0) return 0.0;
        double scale = double.IsNaN(timeScale) || timeScale <= 0.0 ? 1.0 : timeScale;
        double delta = double.IsNaN(frameDeltaSeconds) || frameDeltaSeconds < 0.0 ? 0.0 : frameDeltaSeconds;
        double missed = realGapSeconds * scale - delta;
        return missed > MissedThresholdSeconds ? missed : 0.0;
    }

    /// <summary>遅れ(m)= 実際に走った距離 − 目標ペースの距離。遅れは負</summary>
    public float DeviationMeters(double actualDistanceMeters)
    {
        if (double.IsNaN(actualDistanceMeters) || double.IsInfinity(actualDistanceMeters)) return 0f;
        return (float)(actualDistanceMeters - TargetDistanceMeters);
    }
}
