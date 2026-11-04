using NUnit.Framework;

/// <summary>
/// ARKit の位置が止まったことの判定(<see cref="ArTrackingWatch"/>)。
/// 走っているのに同じ場所に留まるアバターの原因で、立っているだけの走者とは見分ける。
/// </summary>
[TestFixture]
public class ArTrackingWatchTests
{
    [Test]
    public void GpsAdvancesWhileCameraStays_IsFrozen()
    {
        var w = new ArTrackingWatch();
        Freeze(ref w);
        Assert.IsTrue(w.IsFrozen);
    }

    // 3回の更新で 7.5m(走っている走者の GPS)
    private static void Freeze(ref ArTrackingWatch w, double startMeters = 0)
    {
        w.Update(0f, 0, 0, startMeters, false);
        w.Update(1f, 0, 0, startMeters + 2.5, false);
        w.Update(2f, 0, 0, startMeters + 5, false);
        w.Update(3f, 0, 0, startMeters + 7.5, false);
    }

    [Test]
    public void SingleGpsJump_IsNotFrozen()
    {
        // 測位の飛びは1回で大きく増える。以前は 20m の飛びで立っている走者の前へアバターが出た
        var w = new ArTrackingWatch();
        w.Update(0f, 0, 0, 0, false);
        Assert.IsFalse(w.Update(1f, 0, 0, 20, false));
        for (int t = 2; t < 30; t++)
            Assert.IsFalse(w.Update(t, 0, 0, 20, false));
    }

    [Test]
    public void StandingStill_IsNotFrozen()
    {
        var w = new ArTrackingWatch();
        for (int t = 0; t < 60; t++)
            Assert.IsFalse(w.Update(t, 0, 0, 0, false));
    }

    [Test]
    public void SlowGpsDrift_IsNotFrozen()
    {
        // 窓の8秒で4mしか増えない — 屋内のふらつきの累積で走行扱いにしない
        var w = new ArTrackingWatch();
        for (int t = 0; t < 120; t++)
            Assert.IsFalse(w.Update(t, 0, 0, t * 0.5, false));
    }

    [Test]
    public void CameraMovingWithGps_IsNotFrozen()
    {
        var w = new ArTrackingWatch();
        for (int t = 0; t < 60; t++)
            Assert.IsFalse(w.Update(t, t * 3.0, 0, t * 3.0, false));
    }

    [Test]
    public void ArkitReportingLost_TriggersSooner()
    {
        var w = new ArTrackingWatch();
        w.Update(0f, 0, 0, 0, true);
        w.Update(0.2f, 0, 0, 1, true);
        w.Update(0.4f, 0, 0, 2, true);
        Assert.IsTrue(w.Update(0.6f, 0, 0, 2.5, true));
    }

    [Test]
    public void ArkitReportingLostWhileStandingStill_IsNotFrozen()
    {
        var w = new ArTrackingWatch();
        for (int t = 0; t < 30; t++)
            Assert.IsFalse(w.Update(t, 0, 0, 0, true));
    }

    [Test]
    public void CameraMovingAgain_Recovers()
    {
        var w = new ArTrackingWatch();
        Freeze(ref w);
        Assert.IsTrue(w.IsFrozen);
        Assert.IsTrue(w.Update(4f, 0.1, 0, 10, false), "0.5m 未満の揺れでは復帰しない");
        Assert.IsFalse(w.Update(5f, 1.0, 0, 13, false));
    }

    [Test]
    public void DoesNotRecoverWhileArkitStillReportsLost()
    {
        var w = new ArTrackingWatch();
        w.Update(0f, 0, 0, 0, true);
        w.Update(1f, 0, 0, 1, true);
        w.Update(2f, 0, 0, 2, true);
        w.Update(3f, 0, 0, 3, true);
        Assert.IsTrue(w.Update(4f, 5, 0, 6, true));
    }

    [Test]
    public void BaselineGivesTheDistanceAlreadyMissed()
    {
        var w = new ArTrackingWatch();
        Freeze(ref w, startMeters: 100);
        Assert.AreEqual(100.0, w.BaselineGpsMeters);
    }

    [Test]
    public void Reset_ForgetsTheFreeze()
    {
        var w = new ArTrackingWatch();
        Freeze(ref w);
        w.Reset();
        Assert.IsFalse(w.IsFrozen);
        Assert.IsFalse(w.Update(4f, 0, 0, 0, false), "新しい走行は GPS 距離が0へ戻る");
    }
}
