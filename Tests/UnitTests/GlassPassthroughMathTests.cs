using System;
using NUnit.Framework;

/// <summary>
/// グラスへ出すカメラ映像の切り出し(<see cref="GlassPassthroughMath"/>)。
/// 映像を全面に出すと現実より小さく映る — 中央をグラスの画角ぶんだけ使うことを固定する。
/// </summary>
[TestFixture]
public class GlassPassthroughMathTests
{
    private static double M11ForVerticalFov(double degrees) => 1.0 / Math.Tan(degrees * Math.PI / 360.0);

    [Test]
    public void XrealOneOnIphoneWideCamera_UsesCentreOnly()
    {
        // XREAL One 垂直約25.7°、カメラ映像の垂直画角は例として45°
        double scale = GlassPassthroughMath.UvCropScale(25.7, M11ForVerticalFov(45.0));
        double expected = Math.Tan(25.7 * Math.PI / 360.0) / Math.Tan(45.0 * Math.PI / 360.0);
        Assert.That(scale, Is.EqualTo(expected).Within(1e-9));
        Assert.That(scale, Is.InRange(0.5, 0.6));
        Assert.That(GlassPassthroughMath.CenteredUvOffset(scale), Is.EqualTo(0.5 - expected / 2).Within(1e-9));
    }

    [Test]
    public void SameFov_UsesWholeImage()
    {
        Assert.That(GlassPassthroughMath.UvCropScale(40.0, M11ForVerticalFov(40.0)), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(GlassPassthroughMath.CenteredUvOffset(1.0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void GlassWiderThanCamera_ClampsToWholeImage()
    {
        // 映像の外側は無いので 1 で止める(UV が 0〜1 をはみ出すと端が引き伸ばされる)
        Assert.That(GlassPassthroughMath.UvCropScale(60.0, M11ForVerticalFov(40.0)), Is.EqualTo(1.0));
    }

    [TestCase(0.0, 2.0)]
    [TestCase(-10.0, 2.0)]
    [TestCase(180.0, 2.0)]
    [TestCase(double.NaN, 2.0)]
    [TestCase(25.7, 0.0)]
    [TestCase(25.7, -1.0)]
    [TestCase(25.7, double.NaN)]
    [TestCase(25.7, double.PositiveInfinity)]
    public void InvalidInput_UsesWholeImage(double glassFov, double m11)
    {
        Assert.That(GlassPassthroughMath.UvCropScale(glassFov, m11), Is.EqualTo(1.0));
    }
}
