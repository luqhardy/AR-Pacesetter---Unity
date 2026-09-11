using NUnit.Framework;

/// <summary>
/// 進行方向の観測ゲートの検証。
/// 本丸は「立ち止まっている時の測位ノイズと手ブレから方位を作らない」こと —
/// これが実機で「アバターがユーザーの周りを3mの半径で振り回される」原因だった。
/// </summary>
[TestFixture]
public class HeadingGateMathTests
{
    private const float MinSegment = 0.75f; // RunnerTrackingState の従来値

    [Test]
    public void 立ち止まり中の測位ノイズは方位に使わない()
    {
        // 精度8mの測位が1秒ごとに2〜3m「動く」— 従来の0.75mゲートは素通しだった
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(2.5, 8f, MinSegment));
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(3.0, 8f, MinSegment));
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(15.9, 8f, MinSegment), "2倍未満");
    }

    [Test]
    public void 精度に対して十分長い区間なら方位に使う()
    {
        Assert.IsTrue(HeadingGateMath.IsGpsSegmentUsableForHeading(16.0, 8f, MinSegment), "ちょうど2倍");
        Assert.IsTrue(HeadingGateMath.IsGpsSegmentUsableForHeading(10.0, 3f, MinSegment));
        // 精度が良ければ短い区間でも可(ただし固定の最小区間は守る)
        Assert.IsTrue(HeadingGateMath.IsGpsSegmentUsableForHeading(1.0, 0.3f, MinSegment));
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(0.5, 0.1f, MinSegment), "固定の最小区間未満");
    }

    [Test]
    public void 不正な精度や距離は使わない()
    {
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(10.0, -1f, MinSegment));
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(double.NaN, 3f, MinSegment));
        Assert.IsFalse(HeadingGateMath.IsGpsSegmentUsableForHeading(10.0, float.NaN, MinSegment));
    }

    [TestCase(0.02f, false)]   // 旧しきい値 = 手ブレ
    [TestCase(0.2f, false)]    // 体の揺れ
    [TestCase(0.49f, false)]
    [TestCase(0.5f, true)]     // 1.5秒で0.5m = 歩き出し
    [TestCase(3.0f, true)]
    public void ARの移動は手ブレを超える量でだけ方位に使う(float meters, bool expected)
    {
        Assert.AreEqual(expected, HeadingGateMath.IsArMotionUsableForHeading(meters));
    }

    [Test]
    public void ARとGPSの向きの対応はARの移動が無いと初期化しない()
    {
        // 移動が無い状態で初期化すると「カメラの向き vs 無作為な方位」で対応が決まり、
        // 以降のGPS方位が全部ずれる
        Assert.IsFalse(HeadingGateMath.CanSeedWorldAlignment(0.0f, 0.15f));
        Assert.IsFalse(HeadingGateMath.CanSeedWorldAlignment(0.14f, 0.15f));
        Assert.IsTrue(HeadingGateMath.CanSeedWorldAlignment(0.15f, 0.15f));
        Assert.IsFalse(HeadingGateMath.CanSeedWorldAlignment(float.NaN, 0.15f));
    }
}
