using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// CliffMath の検証 (§4.2 断崖判定)。
/// 本丸は「頭上の天井コライダーを『ユーザーの地面』にしない」こと —
/// 真下へのレイの最初のヒット(=天井)を地面にしていたため、3m先の天井が未検出の
/// 室内では「天井 − 床」が断崖として成立し、アバターが足踏み停止 → ユーザーが
/// 追い越して視界から消えていた。
/// </summary>
[TestFixture]
public class CliffMathTests
{
    private const float CameraY = 1.2f;     // 胸マウント想定
    private const float FloorY  = 0.0f;
    private const float CeilY   = 2.4f;     // 一般的な天井高
    private const float MinDrop = 0.5f;     // GroundFloorTracker.DefaultMinCameraToFloorMeters

    private static CliffMath.GroundCandidate Up(float y)   => new CliffMath.GroundCandidate(y, 1.0f);
    private static CliffMath.GroundCandidate Wall(float y) => new CliffMath.GroundCandidate(y, 0.0f);

    [Test]
    public void 頭上の天井ではなく床が地面に選ばれる()
    {
        // レイは頭上から撃つので天井が先にヒットする。順序に依存せず床を選ぶこと
        var hits = new List<CliffMath.GroundCandidate> { Up(CeilY), Up(FloorY) };

        bool ok = CliffMath.TrySelectGround(hits, CameraY, MinDrop, out float y);

        Assert.IsTrue(ok);
        Assert.AreEqual(FloorY, y, 0.0001f, "天井(2.4m)は候補から外れ、床(0m)が選ばれる");
    }

    /// <summary>これが不具合の再現: 天井は頭上だけ検出済み、3m先は床しか無い。</summary>
    [Test]
    public void 天井の下にいても3m先の床との落差は断崖にならない()
    {
        var under = new List<CliffMath.GroundCandidate> { Up(CeilY), Up(FloorY) };
        var ahead = new List<CliffMath.GroundCandidate> { Up(FloorY) };

        Assert.IsTrue(CliffMath.TrySelectGround(under, CameraY, MinDrop, out float userGround));
        Assert.IsTrue(CliffMath.TrySelectGround(ahead, CameraY, MinDrop, out float aheadGround));

        Assert.IsFalse(CliffMath.IsCliffDrop(userGround, aheadGround, 1.5f),
            $"床同士({userGround} vs {aheadGround})に落差は無い。旧実装は天井2.4−床0=2.4mを断崖にしていた");
    }

    [Test]
    public void 天井しか無ければ地面は見つからない()
    {
        var hits = new List<CliffMath.GroundCandidate> { Up(CeilY) };
        Assert.IsFalse(CliffMath.TrySelectGround(hits, CameraY, MinDrop, out _),
            "カメラより上の面は地面ではない — 呼び出し側は『地面なし』として断崖判定を行わない");
    }

    [Test]
    public void 壁の縁は地面にならない()
    {
        // 垂直面はカメラより下でも地面候補にならない(高さ帯だけに頼っていない)
        var hits = new List<CliffMath.GroundCandidate> { Wall(0.6f), Up(FloorY) };

        Assert.IsTrue(CliffMath.TrySelectGround(hits, CameraY, MinDrop, out float y));
        Assert.AreEqual(FloorY, y, 0.0001f);
    }

    [Test]
    public void 机の天板はカメラに近すぎるので地面にならない()
    {
        // 机 0.9m: カメラ1.2mから0.3m下 < 0.5m → 弾く
        var hits = new List<CliffMath.GroundCandidate> { Up(0.9f), Up(FloorY) };

        Assert.IsTrue(CliffMath.TrySelectGround(hits, CameraY, MinDrop, out float y));
        Assert.AreEqual(FloorY, y, 0.0001f);
    }

    [Test]
    public void 段差はカメラから十分下なら高い方を地面にする()
    {
        // 30cmの段差の上にいる: 段(0.3m)と床(0m)が両方ヒット → 自分が乗っている段を採る
        var hits = new List<CliffMath.GroundCandidate> { Up(FloorY), Up(0.3f) };

        Assert.IsTrue(CliffMath.TrySelectGround(hits, CameraY, MinDrop, out float y));
        Assert.AreEqual(0.3f, y, 0.0001f);
    }

    [TestCase(1.5f, true)]    // ちょうど1.5mは断崖
    [TestCase(2.5f, true)]    // 深い断崖 — 上限を設けていないので深くても検出する
    [TestCase(1.49f, false)]  // 未満は段差扱い
    [TestCase(0.0f, false)]
    [TestCase(-0.4f, false)]  // 前方が高い(上り)は断崖ではない
    public void 実測された落差でのみ断崖と判定する(float drop, bool expected)
    {
        var ahead = new List<CliffMath.GroundCandidate> { Up(FloorY - drop) };
        Assert.IsTrue(CliffMath.TrySelectGround(ahead, CameraY, MinDrop, out float aheadGround),
            "3m先の地面はカメラより下でさえあればどれだけ深くても候補になる");
        Assert.AreEqual(expected, CliffMath.IsCliffDrop(FloorY, aheadGround, 1.5f));
    }

    [Test]
    public void 候補が無ければ地面なし()
    {
        Assert.IsFalse(CliffMath.TrySelectGround(new List<CliffMath.GroundCandidate>(), CameraY, MinDrop, out _));
        Assert.IsFalse(CliffMath.TrySelectGround(null, CameraY, MinDrop, out _));
    }

    [Test]
    public void NaNは地面にも断崖にもならない()
    {
        var hits = new List<CliffMath.GroundCandidate> { Up(float.NaN), new CliffMath.GroundCandidate(0f, float.NaN) };
        Assert.IsFalse(CliffMath.TrySelectGround(hits, CameraY, MinDrop, out _));
        Assert.IsFalse(CliffMath.TrySelectGround(new List<CliffMath.GroundCandidate> { Up(0f) }, float.NaN, MinDrop, out _));
        Assert.IsFalse(CliffMath.IsCliffDrop(float.NaN, 0f, 1.5f));
        Assert.IsFalse(CliffMath.IsCliffDrop(0f, float.NegativeInfinity, 1.5f));
    }
}
