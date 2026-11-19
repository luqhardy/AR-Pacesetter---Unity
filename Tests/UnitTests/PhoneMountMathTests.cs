using System;
using NUnit.Framework;

/// <summary>
/// 頭に付けた iPhone の縦横(<see cref="PhoneMountMath"/>)と、90° 回したときのカメラ映像の切り出し
/// (<see cref="GlassPassthroughMath.QuarterTurnUvScales"/>)。
/// </summary>
[TestFixture]
public class PhoneMountMathTests
{
    [TestCase("portrait", PhoneMountOrientation.Portrait)]
    [TestCase("landscape", PhoneMountOrientation.Landscape)]
    [TestCase("", PhoneMountOrientation.Portrait)]
    [TestCase(null, PhoneMountOrientation.Portrait)]
    [TestCase("sideways", PhoneMountOrientation.Portrait)]
    public void Parse_UnknownIsPortrait(string value, PhoneMountOrientation expected)
    {
        Assert.That(PhoneMountMath.Parse(value), Is.EqualTo(expected));
        Assert.That(PhoneMountMath.Parse(PhoneMountMath.ToWireValue(expected)), Is.EqualTo(expected));
    }

    [TestCase(0f, -1f, 0f)]    // 縦・上端が上
    [TestCase(-1f, 0f, 90f)]   // 反時計回りに倒した(上端が左)
    [TestCase(1f, 0f, -90f)]   // 時計回りに倒した(上端が右)
    [TestCase(0f, 1f, 180f)]   // 逆さ
    public void DeviceAngleFromGravity(float gx, float gy, float expected)
    {
        float angle = PhoneMountMath.DeviceAngleFromGravity(gx, gy);
        Assert.That(HeadPoseMath.WrapDegrees(angle - expected), Is.EqualTo(0f).Within(1e-3f));
    }

    [Test]
    public void DeviceAngleFromGravity_LookingStraightDownIsUndecided()
    {
        // 重力がほぼ画面の奥向き(真下を見ている)
        Assert.That(float.IsNaN(PhoneMountMath.DeviceAngleFromGravity(0.1f, -0.1f)), Is.True);
        Assert.That(float.IsNaN(PhoneMountMath.DeviceAngleFromGravity(float.NaN, -1f)), Is.True);
    }

    [TestCase(90f, 0)]
    [TestCase(-90f, 0)]
    [TestCase(float.NaN, 0)]
    public void Portrait_IsAlwaysUpright(float deviceAngle, int previous)
    {
        Assert.That(PhoneMountMath.ResolveMountAngleDegrees(PhoneMountOrientation.Portrait, deviceAngle, previous),
                    Is.EqualTo(0));
    }

    [TestCase(90f, 0, 90)]
    [TestCase(60f, -90, 90)]
    [TestCase(-90f, 90, -90)]
    [TestCase(-120f, 90, -90)]
    [TestCase(10f, -90, -90)]        // 縦に近い間は前の左右のまま(首かしげ)
    [TestCase(175f, 90, 90)]         // 逆さに近い間も
    [TestCase(float.NaN, -90, -90)]  // 決められない(真下を見ている・エディタ)
    [TestCase(float.NaN, 0, 90)]     // 初回は +90
    public void Landscape_SideFromGravity(float deviceAngle, int previous, int expected)
    {
        Assert.That(PhoneMountMath.ResolveMountAngleDegrees(PhoneMountOrientation.Landscape, deviceAngle, previous),
                    Is.EqualTo(expected));
    }

    [TestCase(0, 0, 0)]       // 縦に付けて画面も縦 — 今までどおり
    [TestCase(90, 90, 0)]     // 横に付けて画面も追従して横 — 回さない
    [TestCase(90, 0, 90)]     // 横に付けたが画面は縦のまま — 映像が時計回りに倒れるので反時計回りに戻す
    [TestCase(-90, 0, -90)]
    [TestCase(0, 90, -90)]
    [TestCase(90, -90, 180)]
    [TestCase(0, 180, 180)]
    public void PassthroughRotation(int mount, int screen, int expected)
    {
        Assert.That(PhoneMountMath.PassthroughRotationDegrees(mount, screen), Is.EqualTo(expected));
    }

    [TestCase(90, true)]
    [TestCase(-90, true)]
    [TestCase(0, false)]
    [TestCase(180, false)]
    public void SwapsAxes(int rotation, bool expected)
    {
        Assert.That(PhoneMountMath.SwapsAxes(rotation), Is.EqualTo(expected));
    }

    // ── 90° 回したときの切り出し ─────────────────────────────────────

    private static double M(double fullFovDegrees) => 1.0 / Math.Tan(fullFovDegrees * Math.PI / 360.0);

    [Test]
    public void QuarterTurn_GlassVerticalComesFromScreenWidth()
    {
        // 表示変換が恒等(テクスチャ = 画面の UV)で、映像は横 60°・縦 70° を写している
        const double glassVFov = 25.7, aspect = 16.0 / 9.0;
        bool ok = GlassPassthroughMath.QuarterTurnUvScales(glassVFov, aspect, M(60.0), M(70.0),
            1, 0, 0, 1, 0, 0, out double sx, out double sy);
        Assert.That(ok, Is.True);

        double halfV = Math.Tan(glassVFov * Math.PI / 360.0);
        // 画面の横 = グラスの縦、画面の縦 = グラスの横
        Assert.That(sx, Is.EqualTo(halfV * M(60.0)).Within(1e-9));
        Assert.That(sy, Is.EqualTo(halfV * aspect * M(70.0)).Within(1e-9));
        // 回した後のグラスの縦横比のまま(歪まない): 角度の広がりの比 = 縦/横
        double spanX = sx / M(60.0), spanY = sy / M(70.0);
        Assert.That(spanY / spanX, Is.EqualTo(aspect).Within(1e-9));
    }

    [Test]
    public void QuarterTurn_ReadsBeyondTheCroppedScreenWhenTheCameraHasIt()
    {
        // 表示変換が画面の縦を映像の中央 50% に詰めている(縦の外側に映像が残っている)
        bool ok = GlassPassthroughMath.QuarterTurnUvScales(25.7, 16.0 / 9.0, M(60.0), M(25.0),
            1, 0, 0, 0.5, 0, 0.25, out double sx, out double sy);
        Assert.That(ok, Is.True);
        Assert.That(sy, Is.GreaterThan(1.0), "the glass width needs more than the cropped screen height");
        // 読む先はテクスチャの中に収まる
        Assert.That(0.5 * sy * 0.5, Is.LessThanOrEqualTo(0.5 + 1e-9));
    }

    [Test]
    public void QuarterTurn_ShrinksUniformlyWhenTheCameraRunsOut()
    {
        // 表示変換が恒等(外側に映像が無い)のに、グラスの横が画面の縦より広い
        bool ok = GlassPassthroughMath.QuarterTurnUvScales(25.7, 16.0 / 9.0, M(60.0), M(25.0),
            1, 0, 0, 1, 0, 0, out double sx, out double sy);
        Assert.That(ok, Is.True);
        Assert.That(sy, Is.EqualTo(1.0).Within(1e-9), "stops at the edge of the camera image");
        double halfV = Math.Tan(25.7 * Math.PI / 360.0);
        double unclampedY = halfV * 16.0 / 9.0 * M(25.0);
        Assert.That(sx / sy, Is.EqualTo(halfV * M(60.0) / unclampedY).Within(1e-9), "same ratio — no stretching");
    }

    [Test]
    public void QuarterTurn_HandlesRotatedDisplayTransform()
    {
        // ARKit の映像は横長で、縦向きの画面では u と v が入れ替わる(回転を含む表示変換)
        bool ok = GlassPassthroughMath.QuarterTurnUvScales(25.7, 16.0 / 9.0, M(45.0), M(60.0),
            0, -1, 1, 0, 0, 1, out double sx, out double sy);
        Assert.That(ok, Is.True);
        double halfV = Math.Tan(25.7 * Math.PI / 360.0);
        Assert.That(sx, Is.EqualTo(halfV * M(45.0)).Within(1e-9));
        Assert.That(sy, Is.EqualTo(halfV * 16.0 / 9.0 * M(60.0)).Within(1e-9));
    }

    [TestCase(0.0, 1.7, 2.0, 2.0)]
    [TestCase(25.7, 0.0, 2.0, 2.0)]
    [TestCase(25.7, 1.7, 0.0, 2.0)]
    [TestCase(25.7, 1.7, 2.0, double.NaN)]
    public void QuarterTurn_InvalidInputFallsBack(double fov, double aspect, double m00, double m11)
    {
        bool ok = GlassPassthroughMath.QuarterTurnUvScales(fov, aspect, m00, m11,
            1, 0, 0, 1, 0, 0, out double sx, out double sy);
        Assert.That(ok, Is.False);
        Assert.That(sx, Is.EqualTo(1.0));
        Assert.That(sy, Is.EqualTo(1.0));
    }
}
