using NUnit.Framework;

/// <summary>
/// 開始時の向き決めの検証。要点は「手ブレ程度の移動履歴では視線方向を捨てない」こと。
/// 旧実装は 1.5秒で2cm の履歴があるだけで移動方向を優先し、待機中の手ブレで
/// 決まった方向の3m先にアバターを出していた。
/// </summary>
[TestFixture]
public class StartHeadingPolicyTests
{
    [TestCase(0.0f)]
    [TestCase(0.02f)]   // 旧しきい値ちょうど — 手ブレ。これで移動方向を信じてはいけない
    [TestCase(0.3f)]    // その場で足踏み・体の揺れ
    [TestCase(0.99f)]
    public void 立ち止まっていれば視線方向へ置く(float meters)
    {
        Assert.IsTrue(StartHeadingPolicy.ShouldAlignToView(meters));
    }

    [TestCase(1.0f)]    // 1.5秒で1m = 0.67m/s、歩き出している
    [TestCase(3.0f)]
    [TestCase(5.4f)]    // 3.6m/s で走行中に開始を押した
    public void 既に動いていれば移動方向を信じる(float meters)
    {
        Assert.IsFalse(StartHeadingPolicy.ShouldAlignToView(meters));
    }

    [Test]
    public void 測れていなければ視線方向へ置く()
    {
        Assert.IsTrue(StartHeadingPolicy.ShouldAlignToView(float.NaN));
        Assert.IsTrue(StartHeadingPolicy.ShouldAlignToView(float.PositiveInfinity));
    }
}
