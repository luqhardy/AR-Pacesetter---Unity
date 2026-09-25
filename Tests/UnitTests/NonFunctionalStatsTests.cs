using NUnit.Framework;

/// <summary>
/// 位置誤差の集計 (§10: 1.0m以内) の検証。
/// 「平均が小さくても超過フレームが多い」状態を見逃さないことが要点。
/// </summary>
[TestFixture]
public class PositionAccuracyStatsTests
{
    [Test]
    public void 実測が無ければ未計測を返し合格にもしない()
    {
        var stats = new PositionAccuracyStats();

        Assert.AreEqual(0, stats.SampleCount);
        Assert.AreEqual(-1f, stats.AverageErrorMeters);
        Assert.AreEqual(-1f, stats.OverToleranceRatio);
        Assert.IsFalse(stats.MeetsRequirement, "測っていないものを達成扱いにしない");
        StringAssert.Contains("実測なし", stats.Summarize());
    }

    [Test]
    public void 平均と最大を集計する()
    {
        var stats = new PositionAccuracyStats();
        stats.Add(0.2f);
        stats.Add(0.4f);
        stats.Add(0.6f);

        Assert.AreEqual(3, stats.SampleCount);
        Assert.AreEqual(0.4f, stats.AverageErrorMeters, 0.0001f);
        Assert.AreEqual(0.6f, stats.MaxErrorMeters, 0.0001f);
        Assert.IsTrue(stats.MeetsRequirement);
    }

    [Test]
    public void 許容1mを超えたフレームの割合を数える()
    {
        var stats = new PositionAccuracyStats();
        stats.Add(0.5f);
        stats.Add(1.0f);  // ちょうどは超過ではない
        stats.Add(1.5f);
        stats.Add(2.0f);

        Assert.AreEqual(0.5f, stats.OverToleranceRatio, 0.0001f, "1.5と2.0の2件が超過");
        Assert.AreEqual(1.25f, stats.AverageErrorMeters, 0.0001f);
        Assert.IsFalse(stats.MeetsRequirement, "平均1.25mは1.0mを超えている");
    }

    [Test]
    public void 負値とNaNは無視する()
    {
        var stats = new PositionAccuracyStats();
        stats.Add(-1f);
        stats.Add(float.NaN);
        stats.Add(0.3f);

        Assert.AreEqual(1, stats.SampleCount);
        Assert.AreEqual(0.3f, stats.AverageErrorMeters, 0.0001f);
    }
}

/// <summary>
/// 連続稼働の外挿 (§10: 60分でバッテリー30%以上) の検証。
/// 本丸は「短すぎる計測から断定しない」こと。
/// </summary>
[TestFixture]
public class BatteryEnduranceMathTests
{
    [Test]
    public void 消費率を1時間あたりに換算する()
    {
        // 10分(600秒)で 10% 消費 → 60%/時
        bool ok = BatteryEnduranceMath.TryComputeDrainPerHour(1.0f, 0.90f, 600f, out float perHour);

        Assert.IsTrue(ok);
        Assert.AreEqual(0.60f, perHour, 0.0001f);
    }

    [Test]
    public void 充電しながらの走行は消費0として扱う()
    {
        bool ok = BatteryEnduranceMath.TryComputeDrainPerHour(0.50f, 0.70f, 600f, out float perHour);

        Assert.IsTrue(ok);
        Assert.AreEqual(0f, perHour, 0.0001f, "残量が増えた場合にマイナスの消費率を出さない");
    }

    [TestCase(-1f, 0.9f)]   // 残量を取得できない端末 (SystemInfo.batteryLevel = -1)
    [TestCase(0.9f, -1f)]
    [TestCase(1.5f, 0.9f)]  // 0〜1の外
    public void 残量が取得できなければ算出しない(float start, float end)
    {
        Assert.IsFalse(BatteryEnduranceMath.TryComputeDrainPerHour(start, end, 600f, out float perHour));
        Assert.AreEqual(-1f, perHour);
    }

    [Test]
    public void 経過時間がゼロなら算出しない()
    {
        Assert.IsFalse(BatteryEnduranceMath.TryComputeDrainPerHour(1.0f, 0.9f, 0f, out _));
    }

    [TestCase(60f, false)]    // 1分では外挿に足りない
    [TestCase(119f, false)]
    [TestCase(120f, true)]    // 2分から信用する
    [TestCase(3600f, true)]
    public void 短すぎる計測からは断定しない(float elapsed, bool expected)
    {
        Assert.AreEqual(expected, BatteryEnduranceMath.IsReliableSampleDuration(elapsed));
    }

    [Test]
    public void 満充電から60分後の残量を外挿する()
    {
        // 50%/時 なら 60分後は 50%
        Assert.AreEqual(0.50f, BatteryEnduranceMath.ProjectLevelAfterMinutes(1.0f, 0.50f, 60f), 0.0001f);
        // 30分なら 75%
        Assert.AreEqual(0.75f, BatteryEnduranceMath.ProjectLevelAfterMinutes(1.0f, 0.50f, 30f), 0.0001f);
    }

    [Test]
    public void 残量は0未満にならない()
    {
        Assert.AreEqual(0f, BatteryEnduranceMath.ProjectLevelAfterMinutes(0.20f, 0.90f, 60f), 0.0001f);
    }

    [TestCase(0.50f, true)]   // 60分後 50% 残る
    [TestCase(0.70f, true)]   // 60分後ちょうど30% — 「30%以上維持」なので達成
    [TestCase(0.71f, false)]  // 29%しか残らない
    [TestCase(1.00f, false)]  // 1時間で空
    [TestCase(-1f, false)]    // 未計測を達成扱いにしない
    public void 六十分後に三十パーセント残るかで判定する(float drainPerHour, bool expected)
    {
        Assert.AreEqual(expected, BatteryEnduranceMath.MeetsEnduranceRequirement(drainPerHour));
    }
}
