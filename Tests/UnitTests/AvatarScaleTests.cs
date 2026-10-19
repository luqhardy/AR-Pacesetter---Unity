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
    // ── サイズの異常系(タスク表 ARグラス「サイズの異常系」) ─────────────────

    [Test]
    public void 範囲内の要求身長はそのまま使う()
    {
        Assert.AreEqual(160f, AvatarScale.SanitizeRequestedHeightCm(160f, out bool corrected));
        Assert.IsFalse(corrected);
    }

    [Test]
    public void 範囲外の要求身長は端へ寄せる()
    {
        Assert.AreEqual(AvatarScale.MinRequestedHeightCm, AvatarScale.SanitizeRequestedHeightCm(1f, out bool low));
        Assert.IsTrue(low);
        Assert.AreEqual(AvatarScale.MaxRequestedHeightCm, AvatarScale.SanitizeRequestedHeightCm(9999f, out bool high));
        Assert.IsTrue(high);
    }

    [Test]
    public void 数でない要求身長は既定の175cm()
    {
        Assert.AreEqual(AvatarScale.BaselineHeightCm, AvatarScale.SanitizeRequestedHeightCm(float.NaN, out bool nan));
        Assert.IsTrue(nan);
        Assert.AreEqual(AvatarScale.BaselineHeightCm, AvatarScale.SanitizeRequestedHeightCm(float.PositiveInfinity, out _));
        Assert.AreEqual(AvatarScale.BaselineHeightCm, AvatarScale.SanitizeRequestedHeightCm(-170f, out _));
    }

    [Test]
    public void グラスの身長上限は全身が収まる高さから余白を引く()
    {
        Assert.AreEqual(144f, AvatarScale.ResolveHeightLimitCm(1.47, 0.03), 0.01f);
    }

    [Test]
    public void グラスの身長上限は人形の大きさまで下げない()
    {
        // 前方1mなど、全身を収めると数十cmになる構成
        Assert.AreEqual(AvatarScale.MinHeightLimitCm, AvatarScale.ResolveHeightLimitCm(0.45, 0.03));
    }

    [Test]
    public void 足元が入らない構成は上限なし()
    {
        Assert.AreEqual(0f, AvatarScale.ResolveHeightLimitCm(0.0, 0.03));
        Assert.AreEqual(0f, AvatarScale.ResolveHeightLimitCm(double.NaN, 0.03));
    }

    [Test]
    public void 倍率の上下限に当たったら目標の身長にならない()
    {
        // 素の高さ 1.75m なら 1.0 倍で 175cm
        Assert.IsTrue(AvatarScale.IsAppliedHeightAcceptable(1.75f, 1f, 175f));
        // 素の高さ 10km(単位の取り違え)は MinScale 0.001 まで縮めても 10m 残る
        Assert.IsTrue(AvatarScale.TryComputeScale(10000f, 1f, 175f, out float s));
        Assert.AreEqual(AvatarScale.MinScale, s);
        Assert.IsFalse(AvatarScale.IsAppliedHeightAcceptable(10000f, s, 175f));
    }

    [Test]
    public void 演出の外で倍率がずれていたら異常()
    {
        Assert.IsFalse(AvatarScale.IsRootScaleDeviated(1f, 1f));
        Assert.IsFalse(AvatarScale.IsRootScaleDeviated(1.005f, 1f));
        // スケールインの途中(0.15〜1.0)で止まった大きさ
        Assert.IsTrue(AvatarScale.IsRootScaleDeviated(0.4f, 1f));
        Assert.IsTrue(AvatarScale.IsRootScaleDeviated(float.NaN, 1f));
        // 基準が不明なら判定しない
        Assert.IsFalse(AvatarScale.IsRootScaleDeviated(0.4f, 0f));
    }
}
