using NUnit.Framework;

/// <summary>
/// M2P遅延算出の検証 (§10: 20ms以内・最大許容30ms)。
///
/// ここが守るべき不変条件は「**測れていないときは測れていないと言う**」こと。
/// 以前は合成値(実質フレーム時間)をCSVへ書いていたため、60fpsなら約16msが全行に並び、
/// §11.2の「CSV解析で20msを評価」が測定ではなく構造によって合格していた。
/// </summary>
[TestFixture]
public class MotionToPhotonMathTests
{
    // CACurrentMediaTime 相当(端末起動からの秒)。実機の値域に近い数字を使う
    private const double Sensor = 12345.010;

    [Test]
    public void センサー時刻と提示予定時刻の差がms単位で返る()
    {
        bool ok = MotionToPhotonMath.TryComputeLatencyMs(Sensor, Sensor + 0.018, out double ms);

        Assert.IsTrue(ok);
        Assert.AreEqual(18.0, ms, 0.0001);
    }

    [Test]
    public void 提示予定がセンサー時刻より前なら未計測扱い()
    {
        // 時間軸を取り違えている(別クロックを混ぜた)ときに起きる。値を捏造せず棄却する
        Assert.IsFalse(MotionToPhotonMath.TryComputeLatencyMs(Sensor, Sensor - 0.001, out double ms));
        Assert.AreEqual(MotionToPhotonMath.Unmeasured, ms);
    }

    [TestCase(0.0, 100.0)]   // センサー未供給
    [TestCase(100.0, 0.0)]   // プラグイン未リンク(0が返る)
    [TestCase(0.0, 0.0)]
    public void ゼロは未供給として棄却する(double sensor, double present)
    {
        Assert.IsFalse(MotionToPhotonMath.TryComputeLatencyMs(sensor, present, out double ms));
        Assert.AreEqual(MotionToPhotonMath.Unmeasured, ms);
    }

    [Test]
    public void スリープ明けなどの極端な値は棄却する()
    {
        // 2秒 = 明らかに停止していたフレーム。平均を壊すので実測に混ぜない
        Assert.IsFalse(MotionToPhotonMath.TryComputeLatencyMs(Sensor, Sensor + 2.0, out _));
        // 上限ちょうど(1000ms)は通す
        Assert.IsTrue(MotionToPhotonMath.TryComputeLatencyMs(Sensor, Sensor + 1.0, out double ms));
        Assert.AreEqual(1000.0, ms, 0.0001);
    }

    [Test]
    public void NaNや無限大は棄却する()
    {
        Assert.IsFalse(MotionToPhotonMath.TryComputeLatencyMs(double.NaN, Sensor, out _));
        Assert.IsFalse(MotionToPhotonMath.TryComputeLatencyMs(Sensor, double.PositiveInfinity, out _));
    }

    [TestCase(0.0, true)]
    [TestCase(19.9, true)]
    [TestCase(20.0, true)]   // 「20ms以内」なので境界は合格
    [TestCase(20.1, false)]
    [TestCase(-1.0, false)]  // 未計測は合格にしない
    public void 要求20ms以内の判定(double ms, bool expected)
    {
        Assert.AreEqual(expected, MotionToPhotonMath.MeetsBudget(ms));
    }

    [TestCase(25.0, true)]
    [TestCase(30.0, true)]   // 最大許容ちょうど
    [TestCase(30.1, false)]
    [TestCase(-1.0, false)]
    public void 最大許容30ms以内の判定(double ms, bool expected)
    {
        Assert.AreEqual(expected, MotionToPhotonMath.WithinMaxAllowed(ms));
    }
}

/// <summary>
/// 走行1本ぶんのM2P集計の検証。平均だけでは合否を言えないので、
/// 「20msを超えたフレームの割合」と最悪値を持つことを確かめる。
/// </summary>
[TestFixture]
public class MotionToPhotonStatsTests
{
    [Test]
    public void 実測が無ければ未計測を返す()
    {
        var stats = new MotionToPhotonStats();

        Assert.AreEqual(0, stats.SampleCount);
        Assert.AreEqual(MotionToPhotonMath.Unmeasured, stats.AverageMs);
        Assert.AreEqual(-1.0, stats.OverBudgetRatio);
        StringAssert.Contains("実測なし", stats.Summarize());
    }

    [Test]
    public void 平均_最大_最小を集計する()
    {
        var stats = new MotionToPhotonStats();
        stats.Add(10.0);
        stats.Add(20.0);
        stats.Add(30.0);

        Assert.AreEqual(3, stats.SampleCount);
        Assert.AreEqual(20.0, stats.AverageMs, 0.0001);
        Assert.AreEqual(30.0, stats.MaxMs, 0.0001);
        Assert.AreEqual(10.0, stats.MinMs, 0.0001);
    }

    [Test]
    public void 超過率は要求20msと最大許容30msで別々に数える()
    {
        var stats = new MotionToPhotonStats();
        stats.Add(15.0); // 合格
        stats.Add(25.0); // 20ms超過・30ms以内
        stats.Add(35.0); // 両方超過
        stats.Add(18.0); // 合格

        Assert.AreEqual(0.5, stats.OverBudgetRatio, 0.0001, "25と35の2件が20msを超えている");
        Assert.AreEqual(0.25, stats.OverMaxAllowedRatio, 0.0001, "35の1件だけが30msを超えている");
    }

    [Test]
    public void 未計測は集計に混ざらない()
    {
        var stats = new MotionToPhotonStats();
        stats.Add(MotionToPhotonMath.Unmeasured);
        stats.Add(double.NaN);
        stats.Add(16.0);

        Assert.AreEqual(1, stats.SampleCount, "-1 と NaN は無視される");
        Assert.AreEqual(16.0, stats.AverageMs, 0.0001);
    }

    [Test]
    public void リセットで走行ごとに集計しなおせる()
    {
        var stats = new MotionToPhotonStats();
        stats.Add(50.0);
        stats.Reset();
        stats.Add(10.0);

        Assert.AreEqual(1, stats.SampleCount);
        Assert.AreEqual(10.0, stats.MaxMs, 0.0001, "前の走行の最悪値が残らない");
    }
}
