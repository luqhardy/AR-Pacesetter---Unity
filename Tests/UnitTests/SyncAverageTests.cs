using NUnit.Framework;

/// <summary>
/// 同期率の平均(§4.3 の S〜D 評価の入力)。時間重み付き・double積算であること。
/// </summary>
[TestFixture]
public class SyncAverageTests
{
    [Test]
    public void フレームが落ちた区間も時間どおりに効く()
    {
        var a = new SyncAverage();
        // 60秒は60fpsで同期100%、続く60秒は発熱で20fpsに落ちて同期50%
        for (int i = 0; i < 3600; i++) a.Add(100f, 1f / 60f);
        for (int i = 0; i < 1200; i++) a.Add(50f, 1f / 20f);

        Assert.AreEqual(75f, a.Average, 0.01f, "時間で見れば半々 → 75%");
        // 旧実装(フレーム数で割る)なら (3600×100 + 1200×50) / 4800 = 87.5% と甘く出ていた
    }

    [Test]
    public void 六十分走でも精度が落ちない()
    {
        var a = new SyncAverage();
        const int frames = 60 * 60 * 60; // 60分 × 60fps
        for (int i = 0; i < frames; i++) a.Add(i % 2 == 0 ? 91f : 89f, 1f / 60f);

        Assert.AreEqual(90f, a.Average, 0.001f);
        Assert.AreEqual(3600.0, a.Seconds, 0.01);
    }

    [Test]
    public void 不正なサンプルは積まない()
    {
        var a = new SyncAverage();
        a.Add(80f, 0f);
        a.Add(80f, -1f);
        a.Add(float.NaN, 0.1f);
        Assert.IsFalse(a.HasSamples);
        Assert.AreEqual(0f, a.Average);

        a.Add(80f, 0.5f);
        Assert.IsTrue(a.HasSamples);
        Assert.AreEqual(80f, a.Average, 1e-5f);
    }

    [Test]
    public void Resetで空に戻る()
    {
        var a = new SyncAverage();
        a.Add(70f, 1f);
        a.Reset();
        Assert.IsFalse(a.HasSamples);
    }
}
