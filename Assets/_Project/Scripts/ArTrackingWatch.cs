using System;

/// <summary>
/// ARKit の位置が止まったことを、GPS の距離との食い違いから見つける。依存ゼロ(ユニットテスト対象)。
///
/// <para>アバターの位置は「ARKitカメラ + 3m × 進行方向」なので、ARKit が止まると GPS と HUD が
/// 進んでいてもアバターは同じ場所に留まる。走っているのにカメラが動いていない = 止まっている、と判定する。
/// 立っているだけなら GPS の距離も増えないので反応しない。</para>
/// </summary>
public struct ArTrackingWatch
{
    /// <summary>食い違いを見る時間(秒)。これより遅い増え方は GPS のふらつきとして捨てる</summary>
    public const float WindowSeconds = 8f;

    /// <summary>カメラがほぼ動かない間に GPS がこれだけ進んだら止まったとみなす(m)</summary>
    public const double GpsMetersWhileStill = 6.0;

    /// <summary>ARKit 自身が追跡を失ったと言っているときの同じしきい値(m)</summary>
    public const double GpsMetersWhenReportedLost = 2.0;

    /// <summary>
    /// GPS の距離が増えた回数の下限。走っていれば約1Hzで増え続けるが、測位の飛びは1回で大きく増える。
    /// 1回の飛びで止まった扱いにすると、立っている走者の前でアバターが飛び出す
    /// </summary>
    public const int MinGpsUpdates = 3;

    /// <summary>カメラが「動いた」とみなす距離(m)</summary>
    public const double CameraMovedMeters = 0.5;

    private bool _hasBaseline;
    private float _baselineTime;
    private double _baselineX, _baselineZ, _baselineGps;
    private double _lastGps;
    private int _gpsUpdates;

    /// <summary>ARKit の位置が止まっているか。</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>判定の基準にした時点の GPS 距離(m)。止まったと分かった時点で取りこぼした距離の計算に使う</summary>
    public double BaselineGpsMeters => _baselineGps;

    /// <summary>1フレーム進めて <see cref="IsFrozen"/> を返す。</summary>
    /// <param name="cameraX">ARKit が報告したカメラの位置(フォールバックで足した分を含まない)</param>
    /// <param name="cameraZ">同上</param>
    /// <param name="gpsMeters">GPS の累積距離(m)</param>
    /// <param name="arkitReportsLost">ARKit 自身が追跡を失っていると報告しているか</param>
    public bool Update(float now, double cameraX, double cameraZ, double gpsMeters, bool arkitReportsLost)
    {
        if (!_hasBaseline)
        {
            Rebase(now, cameraX, cameraZ, gpsMeters);
            return IsFrozen;
        }

        if (gpsMeters > _lastGps) _gpsUpdates++;
        _lastGps = gpsMeters;

        double dx = cameraX - _baselineX;
        double dz = cameraZ - _baselineZ;
        bool cameraMoved = Math.Sqrt(dx * dx + dz * dz) >= CameraMovedMeters;

        if (IsFrozen)
        {
            if (cameraMoved && !arkitReportsLost)
            {
                IsFrozen = false;
                Rebase(now, cameraX, cameraZ, gpsMeters);
            }
            return IsFrozen;
        }

        if (cameraMoved)
        {
            Rebase(now, cameraX, cameraZ, gpsMeters);
            return false;
        }

        double gpsAdvance = gpsMeters - _baselineGps;
        if (_gpsUpdates >= MinGpsUpdates
            && gpsAdvance >= (arkitReportsLost ? GpsMetersWhenReportedLost : GpsMetersWhileStill))
        {
            IsFrozen = true;
            return true;
        }

        if (now - _baselineTime >= WindowSeconds)
            Rebase(now, cameraX, cameraZ, gpsMeters);
        return false;
    }

    /// <summary>走行の開始・終了で基準を捨てる。</summary>
    public void Reset()
    {
        _hasBaseline = false;
        IsFrozen = false;
    }

    private void Rebase(float now, double cameraX, double cameraZ, double gpsMeters)
    {
        _hasBaseline = true;
        _baselineTime = now;
        _baselineX = cameraX;
        _baselineZ = cameraZ;
        _baselineGps = gpsMeters;
        _lastGps = gpsMeters;
        _gpsUpdates = 0;
    }
}
