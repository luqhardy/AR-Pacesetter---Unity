using NUnit.Framework;

/// <summary>
/// GroundFloorTracker の検証 (F-05 接地の土台)。
/// 要点は「一度確定した床はカメラに追従して動かない」こと —
/// 実測フロアが無い間に毎フレーム カメラ高−1.5m を再計算していたのが
/// 「アバターが接地せず浮き上がる」不具合の原因だった。
/// </summary>
[TestFixture]
public class GroundFloorTrackerTests
{
    private const float AssumedHeight = 1.5f;

    private static float Provisional(float cameraY) => cameraY - AssumedHeight;

    [Test]
    public void 初期状態では床が未確定()
    {
        var t = new GroundFloorTracker();
        Assert.AreEqual(GroundFloorTracker.FloorSource.None, t.Source);
        Assert.IsFalse(t.HasFloor);
        Assert.IsFalse(t.HasMeasuredFloor);
    }

    [Test]
    public void 実測が無ければ暫定値を1回だけ採用する()
    {
        var t = new GroundFloorTracker();
        bool changed = t.Resolve(false, 0f, Provisional(1.6f), out float y);

        Assert.IsTrue(changed, "初回は由来が変化するのでログ対象");
        Assert.AreEqual(0.1f, y, 0.0001f);
        Assert.AreEqual(GroundFloorTracker.FloorSource.Provisional, t.Source);
        Assert.IsFalse(t.HasMeasuredFloor, "暫定値は実測ではない");
    }

    /// <summary>これが回帰防止の本丸: カメラが上下しても床は動かない。</summary>
    [TestCase(1.6f)]
    [TestCase(3.0f)]
    [TestCase(10.0f)]
    [TestCase(-2.0f)]
    public void 暫定確定後はカメラが動いても床が追従しない(float movedCameraY)
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, Provisional(1.6f), out float first);

        bool changed = t.Resolve(false, 0f, Provisional(movedCameraY), out float after);

        Assert.IsFalse(changed, "由来は変わらない");
        Assert.AreEqual(first, after, 0.0001f,
            $"カメラY={movedCameraY} へ動いても床は {first} のままであること");
    }

    [Test]
    public void 実測が来たら暫定値を上書きする()
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, Provisional(1.6f), out _);

        bool changed = t.Resolve(true, -0.4f, Provisional(1.6f), out float y);

        Assert.IsTrue(changed, "Provisional → Measured は由来の変化");
        Assert.AreEqual(-0.4f, y, 0.0001f);
        Assert.IsTrue(t.HasMeasuredFloor);
    }

    [Test]
    public void 実測は毎回追従する_坂や段差に対応()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.0f, 0f, out _);

        bool changed = t.Resolve(true, 0.35f, 0f, out float y);

        Assert.IsFalse(changed, "Measured のままなので由来は変化しない");
        Assert.AreEqual(0.35f, y, 0.0001f, "実測が来ている間は素直に追従する");
    }

    [Test]
    public void 実測が途切れたら直前の実測値を保持する()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.8f, 0f, out _);

        // ARプレーンのトラッキングが一瞬切れた状況。カメラは 5m の高さにある
        t.Resolve(false, 0f, Provisional(5.0f), out float y);

        Assert.AreEqual(0.8f, y, 0.0001f, "カメラ基準へ戻さず直前の実測床を維持すること");
        Assert.IsTrue(t.HasMeasuredFloor, "一時的な欠測で実測状態を失わない");
    }

    [Test]
    public void NaNの実測は無視される()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.5f, 0f, out _);

        t.Resolve(true, float.NaN, Provisional(1.6f), out float y);

        Assert.AreEqual(0.5f, y, 0.0001f);
        Assert.IsTrue(t.HasMeasuredFloor);
    }

    [Test]
    public void NaNの暫定値は0へ丸められる()
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, float.NaN, out float y);

        Assert.AreEqual(0f, y, 0.0001f);
        Assert.AreEqual(GroundFloorTracker.FloorSource.Provisional, t.Source);
    }

    [Test]
    public void Resetで確定をやり直せる_再走行対応()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 2.0f, 0f, out _);
        t.Reset();

        Assert.AreEqual(GroundFloorTracker.FloorSource.None, t.Source);
        Assert.IsFalse(t.HasFloor);

        t.Resolve(false, 0f, Provisional(1.6f), out float y);
        Assert.AreEqual(0.1f, y, 0.0001f, "リセット後は新しい暫定値を採用できる");
    }
}
