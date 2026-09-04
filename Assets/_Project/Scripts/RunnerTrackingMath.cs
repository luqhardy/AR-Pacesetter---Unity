using System;

/// <summary>
/// Unity非依存のランナー測位計算。緯度経度の短距離差分をローカルの
/// East/Northメートルへ変換し、Unity側のAR座標系との方位合わせに使う。
/// </summary>
public static class RunnerTrackingMath
{
    private const double EarthRadiusMeters = 6371000.0;

    public static bool IsValidCoordinate(double latitude, double longitude)
        => !double.IsNaN(latitude) && !double.IsInfinity(latitude)
        && !double.IsNaN(longitude) && !double.IsInfinity(longitude)
        && latitude >= -90.0 && latitude <= 90.0
        && longitude >= -180.0 && longitude <= 180.0;

    /// <summary>
    /// 数十〜数百mの走行区間向け等距離近似。日付変更線も最短差分へ正規化する。
    /// </summary>
    public static bool TryLocalOffsetMeters(
        double fromLatitude, double fromLongitude,
        double toLatitude, double toLongitude,
        out double eastMeters, out double northMeters)
    {
        eastMeters = 0.0;
        northMeters = 0.0;
        if (!IsValidCoordinate(fromLatitude, fromLongitude)
            || !IsValidCoordinate(toLatitude, toLongitude))
            return false;

        double latitudeRadians = DegreesToRadians((fromLatitude + toLatitude) * 0.5);
        double latitudeDelta = DegreesToRadians(toLatitude - fromLatitude);
        double longitudeDeltaDegrees = NormalizeLongitudeDelta(toLongitude - fromLongitude);
        double longitudeDelta = DegreesToRadians(longitudeDeltaDegrees);

        northMeters = latitudeDelta * EarthRadiusMeters;
        eastMeters = longitudeDelta * EarthRadiusMeters * Math.Cos(latitudeRadians);
        return true;
    }

    public static double DistanceMeters(double eastMeters, double northMeters)
        => Math.Sqrt(eastMeters * eastMeters + northMeters * northMeters);

    /// <summary>0°=North、90°=EastのGPS方位角。</summary>
    public static double BearingDegrees(double eastMeters, double northMeters)
    {
        if (Math.Abs(eastMeters) < 1e-9 && Math.Abs(northMeters) < 1e-9)
            return 0.0;

        double degrees = Math.Atan2(eastMeters, northMeters) * 180.0 / Math.PI;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double NormalizeLongitudeDelta(double degrees)
    {
        while (degrees > 180.0) degrees -= 360.0;
        while (degrees < -180.0) degrees += 360.0;
        return degrees;
    }
}
