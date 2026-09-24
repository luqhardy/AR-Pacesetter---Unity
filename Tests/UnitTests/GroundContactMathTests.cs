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

    [Test]
    public void 非有限値は読み飛ばす()
    {
        var samples = new List<float> { float.NaN, 0.02f, float.NegativeInfinity, -0.01f };

        Assert.IsTrue(GroundContactMath.TryContactError(samples, out float err));
        Assert.AreEqual(-0.01f, err, 1e-6f);
    }
}
