using NUnit.Framework;

/// <summary>
/// 屋外の画像分類(ARCore Scene Semantics)まわりの検証。
///
/// <para>守りたいのは2点: <b>3D面分類を画像分類で上書きしないこと</b>(メッシュの面分類は
/// その三角形そのものの分類で、画像は2D投影からの推定にすぎない)と、
/// <b>安全側に倒れること</b>(未知のラベル・低信頼度・画面外はすべて「判らない」に落ちて
/// 従来の幾何判定へ戻る)。</para>
/// </summary>
[TestFixture]
public class OutdoorSemanticMathTests
{
    // ────────────────────────────────────────────────────────────────
    // ARCore ラベル値 (ArSemanticLabel: UNLABELED=0 〜 WATER=11) の変換
    // ────────────────────────────────────────────────────────────────

    [TestCase(0, SurfaceSemantic.Unknown)]   // UNLABELED
    [TestCase(1, SurfaceSemantic.Sky)]
    [TestCase(2, SurfaceSemantic.Building)]
    [TestCase(3, SurfaceSemantic.Tree)]
    [TestCase(4, SurfaceSemantic.Road)]
    [TestCase(5, SurfaceSemantic.Sidewalk)]
    [TestCase(6, SurfaceSemantic.Terrain)]
    [TestCase(7, SurfaceSemantic.Structure)]
    [TestCase(8, SurfaceSemantic.Other)]     // OBJECT
    [TestCase(9, SurfaceSemantic.Vehicle)]
    [TestCase(10, SurfaceSemantic.Person)]
    [TestCase(11, SurfaceSemantic.Water)]
    public void ARCoreのラベル値が対応する分類へ変換される(int label, SurfaceSemantic expected)
    {
        Assert.AreEqual(expected, SurfaceSemanticMath.FromArcoreLabel(label));
    }

    /// <summary>ラベルが増えたときに、知らないものを勝手に地面や障害物と決めつけない。</summary>
    [TestCase(12)]
    [TestCase(255)]
    [TestCase(-1)]
    public void 未知のラベル値はUnknownへ落ちる(int label)
    {
        Assert.AreEqual(SurfaceSemantic.Unknown, SurfaceSemanticMath.FromArcoreLabel(label));
    }

    // ────────────────────────────────────────────────────────────────
    // 地面候補の優先度 — ARKitに road が無い穴を埋めるのが本題
    // ────────────────────────────────────────────────────────────────

    [TestCase(SurfaceSemantic.Road)]
    [TestCase(SurfaceSemantic.Sidewalk)]
    [TestCase(SurfaceSemantic.Terrain)]
    public void 屋外の路面は明示的な地面として屋内のFloorと同格になる(SurfaceSemantic semantic)
    {
        Assert.AreEqual(SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Floor),
                        SurfaceSemanticMath.GroundPriority(semantic));
        Assert.IsTrue(SurfaceSemanticMath.CanBeGround(semantic));
    }

    [Test]
    public void 明示的な路面は未分類の候補より優先される()
    {
        Assert.Greater(SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Road),
                       SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Unknown));
    }

    /// <summary>水面は幾何的には水平な地面に見えるが、そこへアバターを置くとランナーを水へ誘導する。</summary>
    [Test]
    public void 水面は地面候補にしない()
    {
        Assert.IsFalse(SurfaceSemanticMath.CanBeGround(SurfaceSemantic.Water));
    }

    [Test]
    public void 空は地面でも障害物でもない()
    {
        Assert.IsFalse(SurfaceSemanticMath.CanBeGround(SurfaceSemantic.Sky));
        Assert.IsFalse(SurfaceSemanticMath.IsExplicitObstacle(SurfaceSemantic.Sky),
            "無限遠の空で足踏み停止させない");
    }

    [TestCase(SurfaceSemantic.Building)]
    [TestCase(SurfaceSemantic.Structure)]
    [TestCase(SurfaceSemantic.Vehicle)]
    [TestCase(SurfaceSemantic.Person)]
    [TestCase(SurfaceSemantic.Tree)]
    public void 屋外の障害物は進路を塞ぐ分類になる(SurfaceSemantic semantic)
    {
        Assert.IsTrue(SurfaceSemanticMath.IsExplicitObstacle(semantic));
        Assert.IsFalse(SurfaceSemanticMath.CanBeGround(semantic));
    }

    /// <summary>水たまり程度で走行が止まらないこと(落差判定 §4.2 へ委ねる)。</summary>
    [Test]
    public void 水面では足踏み停止させない()
    {
        Assert.IsFalse(SurfaceSemanticMath.IsExplicitObstacle(SurfaceSemantic.Water));
    }

    [Test]
    public void 既存の屋内分類の扱いは変わらない()
    {
        Assert.AreEqual(2, SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Floor));
        Assert.AreEqual(1, SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Unknown));
        Assert.AreEqual(1, SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Other));
        Assert.AreEqual(0, SurfaceSemanticMath.GroundPriority(SurfaceSemantic.Ceiling));
        Assert.IsTrue(SurfaceSemanticMath.IsExplicitObstacle(SurfaceSemantic.Wall));
    }

    // ────────────────────────────────────────────────────────────────
    // 合成 — 3Dが勝ち、画像は穴埋めに徹する
    // ────────────────────────────────────────────────────────────────

    [Test]
    public void 三次元の面分類は画像分類に上書きされない()
    {
        Assert.AreEqual(SurfaceSemantic.Floor,
            SurfaceSemanticMath.Combine(SurfaceSemantic.Floor, SurfaceSemantic.Road));
    }

    /// <summary>これが効かないと、天井に映った路面ラベルで天井が地面になる。</summary>
    [Test]
    public void 天井は画像がRoadと言っても天井のまま()
    {
        var combined = SurfaceSemanticMath.Combine(SurfaceSemantic.Ceiling, SurfaceSemantic.Road);

        Assert.AreEqual(SurfaceSemantic.Ceiling, combined);
        Assert.IsFalse(SurfaceSemanticMath.CanBeGround(combined));
    }

    [Test]
    public void 未分類の面は画像分類で埋まる()
    {
        Assert.AreEqual(SurfaceSemantic.Road,
            SurfaceSemanticMath.Combine(SurfaceSemantic.Unknown, SurfaceSemantic.Road));
        Assert.AreEqual(SurfaceSemantic.Sidewalk,
            SurfaceSemanticMath.Combine(SurfaceSemantic.Other, SurfaceSemantic.Sidewalk));
    }

    [Test]
    public void 画像が判らなければ三次元側の結果をそのまま返す()
    {
        Assert.AreEqual(SurfaceSemantic.Unknown,
            SurfaceSemanticMath.Combine(SurfaceSemantic.Unknown, SurfaceSemantic.Unknown));
        Assert.AreEqual(SurfaceSemantic.Other,
            SurfaceSemanticMath.Combine(SurfaceSemantic.Other, SurfaceSemantic.Unknown));
    }

    /// <summary>
    /// 陸上トラックの合成路面が unlabeled に落ちても、従来どおり幾何判定で接地できること
    /// (第1期の検証場所そのものなので、ここが安全側に倒れるかは重要)。
    /// </summary>
    [Test]
    public void トラックの路面が未分類でも従来の接地経路が残る()
    {
        var image = SurfaceSemanticMath.FromArcoreLabel(0); // UNLABELED
        var combined = SurfaceSemanticMath.Combine(SurfaceSemantic.Unknown, image);

        Assert.AreEqual(SurfaceSemantic.Unknown, combined);
        Assert.IsTrue(SurfaceSemanticMath.CanBeGround(combined), "未分類は幾何判定へ流れる");
    }

    // ────────────────────────────────────────────────────────────────
    // サンプルの鮮度と画素位置
    // ────────────────────────────────────────────────────────────────

    [Test]
    public void 古い画像サンプルは使わない()
    {
        Assert.IsTrue(SurfaceSemanticMath.IsImageSampleFresh(10.0f, 9.8f, 0.5f));
        Assert.IsFalse(SurfaceSemanticMath.IsImageSampleFresh(10.0f, 9.0f, 0.5f));
        Assert.IsFalse(SurfaceSemanticMath.IsImageSampleFresh(10.0f, 0f, 0.5f), "未受信は常に古い");
        Assert.IsFalse(SurfaceSemanticMath.IsImageSampleFresh(10.0f, 11.0f, 0.5f), "未来の時刻は信用しない");
    }

    [Test]
    public void ビューポート中心は画像の中心画素になる()
    {
        Assert.IsTrue(SurfaceSemanticMath.TryViewportToPixel(0.5f, 0.5f, 3f, 101, 201, out int x, out int y));
        Assert.AreEqual(50, x);
        Assert.AreEqual(100, y);
    }

    [Test]
    public void ビューポートの端は画像の端画素になる()
    {
        Assert.IsTrue(SurfaceSemanticMath.TryViewportToPixel(0f, 0f, 1f, 64, 48, out int x0, out int y0));
        Assert.AreEqual(0, x0);
        Assert.AreEqual(0, y0);

        Assert.IsTrue(SurfaceSemanticMath.TryViewportToPixel(1f, 1f, 1f, 64, 48, out int x1, out int y1));
        Assert.AreEqual(63, x1);
        Assert.AreEqual(47, y1);
    }

    [Test]
    public void 画面外とカメラ後方はサンプルしない()
    {
        Assert.IsFalse(SurfaceSemanticMath.TryViewportToPixel(1.2f, 0.5f, 3f, 64, 48, out _, out _));
        Assert.IsFalse(SurfaceSemanticMath.TryViewportToPixel(0.5f, -0.1f, 3f, 64, 48, out _, out _));
        Assert.IsFalse(SurfaceSemanticMath.TryViewportToPixel(0.5f, 0.5f, -1f, 64, 48, out _, out _),
            "カメラ後方の点は投影しても意味が無い");
        Assert.IsFalse(SurfaceSemanticMath.TryViewportToPixel(0.5f, 0.5f, 0f, 64, 48, out _, out _));
    }

    [Test]
    public void 画像が無いサイズならサンプルしない()
    {
        Assert.IsFalse(SurfaceSemanticMath.TryViewportToPixel(0.5f, 0.5f, 3f, 0, 0, out _, out _));
    }
}
