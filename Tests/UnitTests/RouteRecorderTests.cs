using NUnit.Framework;

/// <summary>
/// 走行経路の間引き記録(RouteRecorder)。履歴の地図・ルート選択の過去ルートカードが使う
/// </summary>
public class RouteRecorderTests
{
    private const double BaseLat = 34.6937, BaseLon = 135.5023;
    private const double GoodAccuracy = 5.0;

    /// <summary>基準点から北へ meters 進んだ緯度</summary>
    private static double North(double meters) => BaseLat + meters / 111195.0;

    [Test]
    public void FirstValidFixIsRecorded()
    {
        var r = new RouteRecorder();
        Assert.IsTrue(r.Add(BaseLat, BaseLon, GoodAccuracy));
        Assert.AreEqual(1, r.Points.Count);
        Assert.AreEqual(BaseLat, r.Points[0].lat, 1e-12);
        Assert.AreEqual(BaseLon, r.Points[0].lon, 1e-12);
    }

    [Test]
    public void FixesCloserThanTheSpacingAreSkipped()
    {
        // GPSのふらつき(数m)で点が増えない
        var r = new RouteRecorder(minSpacingMeters: 10.0);
        r.Add(BaseLat, BaseLon, GoodAccuracy);
        Assert.IsFalse(r.Add(North(4.0), BaseLon, GoodAccuracy));
        Assert.IsFalse(r.Add(North(9.0), BaseLon, GoodAccuracy));
        Assert.IsTrue(r.Add(North(11.0), BaseLon, GoodAccuracy));
        Assert.AreEqual(2, r.Points.Count);
    }

    [Test]
    public void InaccurateOrUnknownAccuracyFixesAreRejected()
    {
        var r = new RouteRecorder();
        Assert.IsFalse(r.Add(BaseLat, BaseLon, RouteRecorder.MaxAccuracyMeters + 0.1));
        Assert.IsFalse(r.Add(BaseLat, BaseLon, 0.0));
        Assert.IsFalse(r.Add(BaseLat, BaseLon, -1.0));
        Assert.IsFalse(r.Add(BaseLat, BaseLon, double.NaN));
        Assert.AreEqual(0, r.Points.Count);
        Assert.IsTrue(r.Add(BaseLat, BaseLon, RouteRecorder.MaxAccuracyMeters));
    }

    [Test]
    public void InvalidCoordinatesAndTheZeroDefaultAreRejected()
    {
        var r = new RouteRecorder();
        Assert.IsFalse(r.Add(0.0, 0.0, GoodAccuracy));       // 未測位の既定値
        Assert.IsFalse(r.Add(91.0, BaseLon, GoodAccuracy));
        Assert.IsFalse(r.Add(BaseLat, 181.0, GoodAccuracy));
        Assert.IsFalse(r.Add(double.NaN, BaseLon, GoodAccuracy));
        Assert.AreEqual(0, r.Points.Count);
    }

    [Test]
    public void LongRunIsThinnedButKeepsStartAndNewestPoint()
    {
        var r = new RouteRecorder(minSpacingMeters: 10.0, maxPoints: 20);
        double meters = 0.0;
        for (int i = 0; i < 200; i++)
        {
            meters += 12.0;
            r.Add(North(meters), BaseLon, GoodAccuracy);
        }

        Assert.LessOrEqual(r.Points.Count, 20);
        Assert.GreaterOrEqual(r.Points.Count, 10);
        Assert.AreEqual(North(12.0), r.Points[0].lat, 1e-12, "start of the run is kept");
        Assert.Greater(r.SpacingMeters, 10.0, "spacing grows after thinning");

        // 最新の点は「最後に記録した点」— 間引き後の間隔より近い測位は捨てられるため
        // 最終測位そのものとは限らないが、そこから今の間隔以内にある
        double lastLat = r.Points[r.Points.Count - 1].lat;
        double gapMeters = (North(meters) - lastLat) * 111195.0;
        Assert.That(gapMeters, Is.GreaterThanOrEqualTo(-1e-6).And.LessThan(r.SpacingMeters));
    }

    [Test]
    public void PointsStayInRunningOrderAfterThinning()
    {
        var r = new RouteRecorder(minSpacingMeters: 10.0, maxPoints: 8);
        for (int i = 1; i <= 100; i++) r.Add(North(i * 15.0), BaseLon, GoodAccuracy);
        for (int i = 1; i < r.Points.Count; i++)
            Assert.Greater(r.Points[i].lat, r.Points[i - 1].lat);
    }

    [Test]
    public void ResetClearsPointsAndRestoresTheSpacing()
    {
        var r = new RouteRecorder(minSpacingMeters: 10.0, maxPoints: 4);
        for (int i = 1; i <= 20; i++) r.Add(North(i * 12.0), BaseLon, GoodAccuracy);
        Assert.Greater(r.SpacingMeters, 10.0);

        r.Reset();
        Assert.AreEqual(0, r.Points.Count);
        Assert.AreEqual(10.0, r.SpacingMeters, 1e-12);
    }
}
