using System;
using System.Collections.Generic;

/// <summary>
/// 選んだルート(過去の走行の GPS 経路)からの逸脱を GPS の座標だけで測る。依存ゼロの純ロジック
/// (Tests/UnitTests でテストする)。HANDOVER §5「ルート同期」・企画書 4.3 サイレントルート復帰。
///
/// <para><b>AR の座標で測らない理由</b>: 経路は緯度経度、ARKit の世界は原点もヨーも任意で、
/// 経路を AR の世界へ置くには位置合わせが要り、その誤差がそのまま逸脱に乗る。走者の最新の測位と
/// 経路を同じ緯度経度で比べれば位置合わせは要らない(案内する向きだけを AR へ回す — RunnerTrackingState)。</para>
///
/// <para><b>GPS の誤差を見込む</b>: 記録した経路も今の測位もそれぞれ数mずれる(トラックなら精度3〜8m)。
/// 企画書の「5m 以上で復帰」をそのまま使うと、トラックを周回しているだけで何度も逸脱扱いになる。
/// <see cref="RouteDeviationGate"/> が、しきい値を精度に応じて広げ、一定時間続いたときだけ逸脱とし、
/// 戻ったと判断するしきい値を低くして(ヒステリシス)行き来を防ぐ。</para>
/// </summary>
public static class RouteDeviation
{
    /// <summary>経路として使える最少の点数(線分が1本以上)</summary>
    public const int MinimumPoints = 2;

    /// <summary>
    /// 平らな配列 [緯度0, 経度0, 緯度1, 経度1, ...] を点の列にする。数でない・範囲外の点は飛ばす。
    /// 使える点が2つ未満なら空(=ルート無し)
    /// </summary>
    public static List<(double lat, double lon)> ParseFlatLatLon(double[] flat)
    {
        var points = new List<(double lat, double lon)>();
        if (flat == null) return points;
        for (int i = 0; i + 1 < flat.Length; i += 2)
        {
            double lat = flat[i], lon = flat[i + 1];
            if (RunnerTrackingMath.IsValidCoordinate(lat, lon)) points.Add((lat, lon));
        }
        if (points.Count < MinimumPoints) points.Clear();
        return points;
    }

    /// <summary>
    /// 走者の位置から経路(折れ線)までの最短距離(m)と、経路上の最寄り点への向き(東・北 m)。
    /// 走者を原点にしたローカルの東北座標(等距離近似 — 数百mまでの範囲で十分)で測る。
    /// </summary>
    /// <returns>測れたか(経路が2点未満・座標が不正なら false)</returns>
    public static bool TryCrossTrack(IReadOnlyList<(double lat, double lon)> route, double latitude, double longitude,
                                     out double distanceMeters, out double eastToRoute, out double northToRoute,
                                     out int nearestSegment)
    {
        distanceMeters = double.PositiveInfinity;
        eastToRoute = northToRoute = 0.0;
        nearestSegment = -1;
        if (route == null || route.Count < MinimumPoints) return false;
        if (!RunnerTrackingMath.IsValidCoordinate(latitude, longitude)) return false;

        bool havePrevious = false;
        double pe = 0, pn = 0;
        for (int i = 0; i < route.Count; i++)
        {
            if (!RunnerTrackingMath.TryLocalOffsetMeters(latitude, longitude, route[i].lat, route[i].lon,
                                                         out double e, out double n))
            {
                havePrevious = false;
                continue;
            }
            if (havePrevious)
            {
                ClosestPointOnSegment(pe, pn, e, n, out double ce, out double cn);
                double d = Math.Sqrt(ce * ce + cn * cn);
                if (d < distanceMeters)
                {
                    distanceMeters = d;
                    eastToRoute = ce;
                    northToRoute = cn;
                    nearestSegment = i - 1;
                }
            }
            pe = e; pn = n; havePrevious = true;
        }
        return nearestSegment >= 0;
    }

    /// <summary>原点(走者)から線分 a-b への最寄り点(東・北 m)</summary>
    private static void ClosestPointOnSegment(double ae, double an, double be, double bn,
                                              out double ce, out double cn)
    {
        double abE = be - ae, abN = bn - an;
        double lengthSq = abE * abE + abN * abN;
        double t = lengthSq < 1e-9 ? 0.0 : -(ae * abE + an * abN) / lengthSq;
        t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
        ce = ae + t * abE;
        cn = an + t * abN;
    }
}

/// <summary>
/// 測位ごとの逸脱距離から「ルートを外れた」「戻った」を決める(GPS の誤差を見込んだしきい値・持続時間・ヒステリシス)。
/// </summary>
public sealed class RouteDeviationGate
{
    /// <summary>逸脱とみなす最小の距離(m)。トラックは8レーンで幅約10m — 外へ出たら確実に超える値</summary>
    public const double MinimumEnterMeters = 15.0;

    /// <summary>精度(m)の何倍まで誤差とみなすか。精度10mの測位なら20mまで外れていても誤差扱い</summary>
    public const double AccuracyFactor = 2.0;

    /// <summary>戻ったとみなすのは、入るしきい値からこれだけ内側に入ったとき(行き来しない)</summary>
    public const double HysteresisMeters = 5.0;

    /// <summary>しきい値を超えた状態がこれだけ続いたら逸脱(1回の飛びでは反応しない)</summary>
    public const double EnterSeconds = 5.0;

    /// <summary>戻ったとみなすには、内側にこれだけいること</summary>
    public const double ExitSeconds = 2.0;

    /// <summary>これより精度の悪い測位は判定に使わない(状態も時間も変えない)</summary>
    public const double MaximumUsableAccuracyMeters = 20.0;

    /// <summary>測位の間隔をこれ以上は数えない(GPS が途切れた後の1回で、いきなり逸脱にしない)</summary>
    public const double MaximumStepSeconds = 2.0;

    /// <summary>
    /// 一度もこの距離まで近づいていなければ、まだルートに着いていない(別の場所の記録・スタート地点へ移動中)
    /// とみなして判定しない。一度近づいたら、以後は離れても逸脱として扱う
    /// </summary>
    public const double NearbyLimitMeters = 100.0;

    public bool IsDeviated { get; private set; }

    /// <summary>測位はあるが、まだルートに近づいていないので判定していない</summary>
    public bool IsRouteFarAway => _seenFix && !_reachedRoute;

    /// <summary>直近の逸脱距離(m)。まだ測っていなければ負</summary>
    public double LastDistanceMeters { get; private set; } = -1.0;

    /// <summary>直近の測位に使った「入る」しきい値(m)</summary>
    public double LastEnterThresholdMeters { get; private set; } = MinimumEnterMeters;

    private bool _seenFix;
    private bool _reachedRoute;
    private double _outsideSeconds;
    private double _insideSeconds;

    public static double EnterThreshold(double accuracyMeters)
        => Math.Max(MinimumEnterMeters, AccuracyFactor * Math.Max(0.0, accuracyMeters));

    public static double ExitThreshold(double accuracyMeters)
        => EnterThreshold(accuracyMeters) - HysteresisMeters;

    /// <summary>
    /// 新しい測位を1つ反映する。
    /// </summary>
    /// <param name="distanceMeters">経路からの距離(m)</param>
    /// <param name="accuracyMeters">その測位の水平精度(m)</param>
    /// <param name="secondsSincePreviousFix">前の測位からの経過秒(最初は0)</param>
    /// <returns>逸脱の状態が変わったか</returns>
    public bool Update(double distanceMeters, double accuracyMeters, double secondsSincePreviousFix)
    {
        if (double.IsNaN(distanceMeters) || double.IsInfinity(distanceMeters) || distanceMeters < 0.0) return false;
        if (double.IsNaN(accuracyMeters) || accuracyMeters < 0.0 || accuracyMeters > MaximumUsableAccuracyMeters)
            return false;

        _seenFix = true;
        if (!_reachedRoute)
        {
            if (distanceMeters > NearbyLimitMeters) return false;
            _reachedRoute = true;
        }

        LastDistanceMeters = distanceMeters;
        LastEnterThresholdMeters = EnterThreshold(accuracyMeters);
        double step = secondsSincePreviousFix;
        if (double.IsNaN(step) || step < 0.0) step = 0.0;
        if (step > MaximumStepSeconds) step = MaximumStepSeconds;

        bool before = IsDeviated;
        if (!IsDeviated)
        {
            _outsideSeconds = distanceMeters >= EnterThreshold(accuracyMeters) ? _outsideSeconds + step : 0.0;
            if (_outsideSeconds >= EnterSeconds)
            {
                IsDeviated = true;
                _insideSeconds = 0.0;
            }
        }
        else
        {
            _insideSeconds = distanceMeters < ExitThreshold(accuracyMeters) ? _insideSeconds + step : 0.0;
            if (_insideSeconds >= ExitSeconds)
            {
                IsDeviated = false;
                _outsideSeconds = 0.0;
            }
        }
        return before != IsDeviated;
    }

    public void Reset()
    {
        IsDeviated = false;
        LastDistanceMeters = -1.0;
        LastEnterThresholdMeters = MinimumEnterMeters;
        _seenFix = false;
        _reachedRoute = false;
        _outsideSeconds = 0.0;
        _insideSeconds = 0.0;
    }
}
