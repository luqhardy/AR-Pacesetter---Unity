using NUnit.Framework;

/// <summary>
/// AvatarScale の検証 (企画書 §4.1 身長スケール)。
/// 要点は「実測値から逆算する」こと — 旧実装は
/// 「モデルはスケール1で175cm」という未検証の前提で固定倍率を掛けており、
/// モデルが実際には大きいと、指定175cmでも実物より大きく表示され続けた。
/// </summary>
[TestFixture]
public class AvatarScaleTests
{
    private const float Tol = 0.0005f;

    [Test]
    public void 素の高さがちょうど目標なら等倍()
    {
        // scale 1.0 で 1.75m のモデルに 175cm を指定 → 1.0
        Assert.IsTrue(AvatarScale.TryComputeScale(1.75f, 1.0f, 175f, out float s));
        Assert.AreEqual(1.0f, s, Tol);
    }

    [Test]
    public void モデルが大きすぎる場合は縮小する()
    {
        // 実測3.5m(=想定の2倍)なら 175cm 指定で 0.5 倍
        Assert.IsTrue(AvatarScale.TryComputeScale(3.5f, 1.0f, 175f, out float s));
        Assert.AreEqual(0.5f, s, Tol);
    }

    [Test]
    public void センチ単位で取り込まれたモデルも実寸へ戻せる()
    {
        // 1ユニット=1cm で入ってしまい 175m に見えているケース
        Assert.IsTrue(AvatarScale.TryComputeScale(175f, 1.0f, 175f, out float s));
        Assert.AreEqual(0.01f, s, Tol);
    }

    [Test]
    public void モデルが小さすぎる場合は拡大する()
    {
        Assert.IsTrue(AvatarScale.TryComputeScale(0.875f, 1.0f, 175f, out float s));
        Assert.AreEqual(2.0f, s, Tol);
    }

    [Test]
    public void 既にスケールが掛かっていても正しく再計算する()
    {
        // scale 2.0 で 3.5m ということは素の高さは 1.75m。175cm指定なら 1.0 へ戻る
        Assert.IsTrue(AvatarScale.TryComputeScale(3.5f, 2.0f, 175f, out float s));
        Assert.AreEqual(1.0f, s, Tol);
    }

    [Test]
    public void 目標身長を変えれば比例する()
    {
        Assert.IsTrue(AvatarScale.TryComputeScale(1.75f, 1.0f, 160f, out float s160));
        Assert.AreEqual(160f / 175f, s160, Tol);

        Assert.IsTrue(AvatarScale.TryComputeScale(1.75f, 1.0f, 190f, out float s190));
        Assert.AreEqual(190f / 175f, s190, Tol);
    }

    [Test]
    public void 計測できていなければ適用しない_現状維持()
    {
        Assert.IsFalse(AvatarScale.TryComputeScale(0f, 1.0f, 175f, out float s));
        Assert.AreEqual(1.0f, s, Tol, "失敗時は現在のスケールを返す");
    }

    [Test]
    public void 極小の実測値は計測失敗として扱う()
    {
        Assert.IsFalse(AvatarScale.TryComputeScale(0.005f, 1.0f, 175f, out _));
    }

    [Test]
    public void NaNや無限大は弾く()
    {
        Assert.IsFalse(AvatarScale.TryComputeScale(float.NaN, 1.0f, 175f, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, float.NaN, 175f, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, 1.0f, float.NaN, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(float.PositiveInfinity, 1.0f, 175f, out _));
    }

    [Test]
    public void 不正な現在スケールや目標身長は弾く()
    {
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, 0f, 175f, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, -1f, 175f, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, 1f, 0f, out _));
        Assert.IsFalse(AvatarScale.TryComputeScale(1.75f, 1f, -175f, out _));
    }
}
