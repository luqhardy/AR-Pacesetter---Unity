using System;

/// <summary>
/// 周回のチェックポイント(Docs/PACESETTER_BEHAVIOR_SPEC.md R-20〜R-22)。依存ゼロの純ロジック。
///
/// <para>スタートラインの検知方法は U-07 で<b>未決</b>。決まるまでの仮として、START の地点をスタートラインとみなし、
/// 距離の推定が周の境目に来た前後で GPS がそこへ最も近づいた瞬間を「通過」とする。
/// そのときの距離と「周回数 × 1周」の差が補正量になる。検知方法が決まったら <see cref="Report"/> の中だけを差し替える。</para>
/// </summary>
public sealed class StartLineCheckpoint
{
    /// <summary>仮: 周の境目の前後これだけの範囲で最接近を探す(1周で±30m はズレない — §7 の目安で約4m)</summary>
    public const double WindowMeters = 30.0;
    /// <summary>仮: スタート地点へこれ以上近づいた周だけ補正する(GPS の誤差は数m)</summary>
    public const double AcceptMeters = 15.0;
    /// <summary>これより精度の悪い測位は使わない(GPS ロスト判定の 10m と同じ)</summary>
    public const float MaxAccuracyMeters = 10f;

    private const double MetersPerDegree = 111320.0;

    private readonly double _lapLengthMeters;
    private double _originLatitude;
    private double _originLongitude;
    private int _nextLap = 1;
    private double _bestDistance = double.PositiveInfinity;
    private double _bestUserPos;

    public bool HasOrigin { get; private set; }

    public StartLineCheckpoint(double lapLengthMeters)
    {
        _lapLengthMeters = lapLengthMeters;
    }

    /// <summary>測位を1回渡す。周の境目の範囲を抜けたときに補正量を返す。</summary>
    /// <param name="userPos">今の user_pos(START からの距離)</param>
    /// <param name="correctionMeters">真の user_pos − 数えていた user_pos</param>
    /// <returns>補正量が出たか</returns>
    public bool Report(double latitude, double longitude, float accuracyMeters, double userPos, out double correctionMeters)
    {
        correctionMeters = 0.0;
        if (_lapLengthMeters <= WindowMeters * 2.0) return false;
        if (accuracyMeters <= 0f || accuracyMeters > MaxAccuracyMeters) return false;

        if (!HasOrigin)
        {
            _originLatitude = latitude;
            _originLongitude = longitude;
            HasOrigin = true;
            return false;
        }

        double lapPosition = _nextLap * _lapLengthMeters;
        if (userPos < lapPosition - WindowMeters) return false;

        if (userPos <= lapPosition + WindowMeters)
        {
            double distance = DistanceFromOrigin(latitude, longitude);
            if (distance < _bestDistance)
            {
                _bestDistance = distance;
                _bestUserPos = userPos;
            }
            return false;
        }

        // 範囲を抜けた: この周の結果を確定して次の周へ
        bool accepted = _bestDistance <= AcceptMeters;
        if (accepted) correctionMeters = lapPosition - _bestUserPos;
        _nextLap++;
        _bestDistance = double.PositiveInfinity;
        return accepted;
    }

    private double DistanceFromOrigin(double latitude, double longitude)
    {
        double north = (latitude - _originLatitude) * MetersPerDegree;
        double east = (longitude - _originLongitude) * MetersPerDegree
                      * Math.Cos(_originLatitude * Math.PI / 180.0);
        return Math.Sqrt(north * north + east * east);
    }
}
