using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// GroundContactMath の検証 (§10 接地誤差 ±5cm)。
/// 本丸は「歩幅の位相で合否が変わらない」こと — 1フレームで測っていたE2E項目は、
/// 計測の瞬間が遊脚期に当たるだけで +6cm を示し落ちうる状態だった。
/// </summary>
[TestFixture]
public class GroundContactMathTests
{
    [Test]
    public void 立脚期の最下点で判定し遊脚期の浮きは無視する()
    {
        // 1歩幅: 接地(-0.03) → 遊脚(+0.06, +0.12) → 接地(-0.02)
        var samples = new List<float> { -0.03f, 0.06f, 0.12f, -0.02f };

        Assert.IsTrue(GroundContactMath.TryContactError(samples, out float err));
        Assert.AreEqual(-0.03f, err, 1e-6f);
        Assert.IsTrue(GroundContactMath.IsWithinTolerance(err));
    }

    [Test]
    public void どの位相から計測を始めても結果は同じ()
    {
        var cycle = new List<float> { -0.03f, 0.06f, 0.12f, 0.04f };
        GroundContactMath.TryContactError(cycle, out float expected);

        for (int shift = 1; shift < cycle.Count; shift++)
        {
            var rotated = new List<float>();
            for (int i = 0; i < cycle.Count; i++) rotated.Add(cycle[(i + shift) % cycle.Count]);

            GroundContactMath.TryContactError(rotated, out float err);
            Assert.AreEqual(expected, err, 1e-6f, $"shift {shift}");
        }
    }

    [Test]
    public void 常に浮いているアバターは検出される()
    {
        var floating = new List<float> { 0.08f, 0.15f, 0.20f, 0.09f };

        GroundContactMath.TryContactError(floating, out float err);
        Assert.AreEqual(0.08f, err, 1e-6f);
        Assert.IsFalse(GroundContactMath.IsWithinTolerance(err));
    }

    [Test]
    public void 床に沈んだアバターは検出される()
    {
        var sunk = new List<float> { -0.09f, -0.01f, 0.03f };

        GroundContactMath.TryContactError(sunk, out float err);
        Assert.IsFalse(GroundContactMath.IsWithinTolerance(err));
    }

    [Test]
    public void サンプルが無ければ判定しない()
    {
        Assert.IsFalse(GroundContactMath.TryContactError(new List<float>(), out _));
        Assert.IsFalse(GroundContactMath.TryContactError(null, out _));
        Assert.IsFalse(GroundContactMath.TryContactError(new List<float> { float.NaN }, out _));
    }

    // ── 足のめり込み補正 ──────────────────────────────────────────────

    [Test]
    public void めり込んだ分だけ持ち上げる()
    {
        Assert.AreEqual(0.06f, GroundContactMath.ComputeLift(-0.06f), 1e-6f);
    }

    [Test]
    public void 足が床より上なら持ち上げない_空中局面を消さない()
    {
        Assert.AreEqual(0f, GroundContactMath.ComputeLift(0f));
        Assert.AreEqual(0f, GroundContactMath.ComputeLift(0.12f));
    }

    [Test]
    public void 異常値で宙へ飛ばさない()
    {
        Assert.AreEqual(GroundContactMath.MaxLiftMeters, GroundContactMath.ComputeLift(-3f), 1e-6f);
        Assert.AreEqual(0f, GroundContactMath.ComputeLift(float.NaN));
        Assert.AreEqual(0f, GroundContactMath.ComputeLift(float.NegativeInfinity));
    }

    [Test]
    public void 足裏の点は最下点と足裏の前後左右の端()
    {
        // 足の甲(高い)と足裏(低い)。足裏は前後 -0.05〜+0.15、左右 -0.04〜+0.04
        var h = new List<float> { 0.08f, 0.000f, 0.005f, 0.010f, 0.004f, 0.006f, 0.09f };
        var f = new List<float> { 0.05f, 0.020f, -0.05f, 0.150f, 0.030f, 0.060f, 0.20f };
        var s = new List<float> { 0.00f, 0.000f, 0.000f, 0.000f, -0.04f, 0.040f, 0.00f };

        var picks = GroundContactMath.SelectSolePoints(h, f, s, GroundContactMath.SoleBandMeters);

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, picks);
        CollectionAssert.DoesNotContain(picks, 0, "足の甲は足裏ではない");
        CollectionAssert.DoesNotContain(picks, 6, "足裏の面より上の点は前端でも選ばない");
    }

    [Test]
    public void 足裏の点は重複せず_空入力では空()
    {
        var one = GroundContactMath.SelectSolePoints(
            new List<float> { 0f }, new List<float> { 0f }, new List<float> { 0f }, 0.015f);
        CollectionAssert.AreEqual(new[] { 0 }, one);

        Assert.IsEmpty(GroundContactMath.SelectSolePoints(
            new List<float>(), new List<float>(), new List<float>(), 0.015f));
        Assert.IsEmpty(GroundContactMath.SelectSolePoints(null, null, null, 0.015f));
    }

    [Test]
    public void 非有限値は読み飛ばす()
    {
        var samples = new List<float> { float.NaN, 0.02f, float.NegativeInfinity, -0.01f };

        Assert.IsTrue(GroundContactMath.TryContactError(samples, out float err));
        Assert.AreEqual(-0.01f, err, 1e-6f);
    }
}
