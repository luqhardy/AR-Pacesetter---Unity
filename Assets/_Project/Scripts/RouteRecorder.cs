using System;
using System.Collections.Generic;

/// <summary>
/// 走行経路の1点(緯度・経度)。走行記録 JSON と Swift への HistoryData に載る。
/// Unity非依存のPOCO([Serializable]はSystem)。
/// </summary>
[Serializable]
public class RoutePoint
{
    public double lat;
    public double lon;
}

/// <summary>
/// 走行経路を間引いて記録する(ホームの「前回の記録」の地図・ルート選択の過去ルートカード用)。
///
/// <para>地図に線を引くためのもので、距離・ペースの計算には使わない(それは CoreLocation の累積距離)。
/// 100Hz の CSV(F-11)とは別物 — 履歴は Swift へ一度に送る(最大20件)ので、1走行あたりの点数に
/// 上限をかける。上限を超えたら1点おきに捨て、以後の間隔を2倍にする(長い走行でも全体の形が残る)。</para>
///
/// <para>Unity非依存の純ロジック(Tests/UnitTests でテストする)。</para>
/// </summary>
public sealed class RouteRecorder
{
    /// <summary>この距離未満しか動いていない測位は記録しない(m)。GPSのふらつきで点が増えないように</summary>
    public const double DefaultMinSpacingMeters = 10.0;

    /// <summary>1走行あたりの最大点数。20件 × 240点で HistoryData は約100KB</summary>
    public const int DefaultMaxPoints = 240;

    /// <summary>これより精度の悪い測位(水平精度 m)は経路に入れない。地図の線が跳ねるため</summary>
    public const double MaxAccuracyMeters = 20.0;

    private readonly List<RoutePoint> _points = new List<RoutePoint>();
    private readonly double _baseSpacingMeters;
    private readonly int _maxPoints;
    private double _spacingMeters;

    public RouteRecorder(double minSpacingMeters = DefaultMinSpacingMeters, int maxPoints = DefaultMaxPoints)
    {
        _baseSpacingMeters = Math.Max(0.0, minSpacingMeters);
        _maxPoints = Math.Max(2, maxPoints);
        _spacingMeters = _baseSpacingMeters;
    }

    /// <summary>記録した点(古い順)</summary>
    public IReadOnlyList<RoutePoint> Points => _points;

    /// <summary>今の間引き間隔(m)。上限で間引くたびに2倍になる</summary>
    public double SpacingMeters => _spacingMeters;

    public void Reset()
    {
        _points.Clear();
        _spacingMeters = _baseSpacingMeters;
    }

    /// <summary>
    /// 測位を1つ渡す。記録したら true。
    /// 不正な座標・(0,0)・精度の悪い測位・前の点から近すぎる測位は捨てる。
    /// </summary>
    public bool Add(double latitude, double longitude, double accuracyMeters)
    {
        if (!RunnerTrackingMath.IsValidCoordinate(latitude, longitude)) return false;
        if (latitude == 0.0 && longitude == 0.0) return false; // 未測位の既定値
        if (!(accuracyMeters > 0.0) || accuracyMeters > MaxAccuracyMeters) return false;

        if (_points.Count > 0)
        {
            RoutePoint last = _points[_points.Count - 1];
            if (!RunnerTrackingMath.TryLocalOffsetMeters(last.lat, last.lon, latitude, longitude,
                                                        out double east, out double north))
                return false;
            if (RunnerTrackingMath.DistanceMeters(east, north) < _spacingMeters) return false;
        }

        _points.Add(new RoutePoint { lat = latitude, lon = longitude });
        if (_points.Count > _maxPoints) Thin();
        return true;
    }

    /// <summary>1点おきに捨てる。始点と最新の点は必ず残す</summary>
    private void Thin()
    {
        RoutePoint newest = _points[_points.Count - 1];
        var kept = new List<RoutePoint>(_points.Count / 2 + 2);
        for (int i = 0; i < _points.Count; i += 2) kept.Add(_points[i]);
        if (kept[kept.Count - 1] != newest) kept.Add(newest);

        _points.Clear();
        _points.AddRange(kept);
        _spacingMeters = Math.Max(_spacingMeters * 2.0, 1.0);
    }
}
