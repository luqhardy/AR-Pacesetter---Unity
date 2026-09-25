using NUnit.Framework;

/// <summary>
/// ARグラスの光学系まわりの検証。
///
/// <para>ここで一番効いているのは「公称FoVは対角である」という前提の検算と、
/// 「§仕様の3.0m前方に等身大アバターを置くと XREAL One の視野に全身が入らない」という
/// 事実の固定。後者は実機で初めて気付くと痛いので、数値として残しておく。</para>
/// </summary>
[TestFixture]
public class GlassOpticsMathTests
{
    private const double Aspect16x9 = 16.0 / 9.0;
    private const double OneDiagonalFov = 50.0;      // XREAL One 公称
    private const double OneProDiagonalFov = 57.0;   // XREAL One Pro 公称

    private static double OneVerticalFov =>
        GlassOpticsMath.VerticalFovDegrees(OneDiagonalFov, GlassFovAxis.Diagonal, Aspect16x9);

    // ────────────────────────────────────────────────────────────────
    // 公称値が対角であることの検算
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// XREALは One Pro を「4mで171インチ相当」と表現する。対角57°から求めた対角長が
    /// それと一致するなら、公称FoVは対角と解釈してよい(One も 50°→147インチで一致)。
    /// </summary>
    [Test]
    public void 公称FoVを対角とみなすと仮想スクリーンのインチ表記と一致する()
    {
        Assert.AreEqual(171.0, GlassOpticsMath.VirtualScreenDiagonalInches(OneProDiagonalFov, 4.0), 1.0,
            "One Pro: 対角57° → 4mで171インチ");
        Assert.AreEqual(147.0, GlassOpticsMath.VirtualScreenDiagonalInches(OneDiagonalFov, 4.0), 1.0,
            "One: 対角50° → 4mで147インチ");
    }

    [Test]
    public void 対角50度16対9は水平44度垂直26度になる()
    {
        Assert.AreEqual(44.2, GlassOpticsMath.HorizontalFovDegrees(OneDiagonalFov, GlassFovAxis.Diagonal, Aspect16x9), 0.1);
        Assert.AreEqual(25.7, OneVerticalFov, 0.1);
    }

    [Test]
    public void 軸指定がそのままの軸なら値を変えない()
    {
        Assert.AreEqual(30.0, GlassOpticsMath.VerticalFovDegrees(30.0, GlassFovAxis.Vertical, Aspect16x9), 1e-9);
        Assert.AreEqual(30.0, GlassOpticsMath.HorizontalFovDegrees(30.0, GlassFovAxis.Horizontal, Aspect16x9), 1e-9);
    }

    [Test]
    public void 水平から垂直への換算はアスペクト比で決まる()
    {
        double h = GlassOpticsMath.HorizontalFovDegrees(OneDiagonalFov, GlassFovAxis.Diagonal, Aspect16x9);
        double v = GlassOpticsMath.VerticalFovForViewport(h, Aspect16x9);
        Assert.AreEqual(OneVerticalFov, v, 1e-6, "同じアスペクトなら対角経由と一致する");

        // ビューポートが縦長(iPhone実機の画面など)なら垂直画角は広がる
        Assert.Greater(GlassOpticsMath.VerticalFovForViewport(h, 0.5), v);
    }

    [Test]
    public void 不正な入力ではゼロを返す()
    {
        Assert.AreEqual(0.0, GlassOpticsMath.VerticalFovDegrees(0.0, GlassFovAxis.Diagonal, Aspect16x9));
        Assert.AreEqual(0.0, GlassOpticsMath.VerticalFovDegrees(180.0, GlassFovAxis.Diagonal, Aspect16x9));
        Assert.AreEqual(0.0, GlassOpticsMath.HorizontalFovDegrees(50.0, GlassFovAxis.Diagonal, 0.0));
    }

    // ────────────────────────────────────────────────────────────────
    // F-03(3.0m前方)と視野の突き合わせ — 第1期の要判断事項
    // ────────────────────────────────────────────────────────────────

    private const double EyeHeight = 1.55;     // HeadPoseMath.DefaultEyeHeightMeters
    private const double AvatarHeight = 1.75;  // AvatarScale.BaselineHeightCm
    private const double LeadDistance = 3.0;   // F-03

    [Test]
    public void 身長175cmのアバターは3m前方で垂直31度を占める()
    {
        double span = GlassOpticsMath.BodySpanDegrees(EyeHeight, AvatarHeight, LeadDistance);
        Assert.AreEqual(31.1, span, 0.1);
    }

    /// <summary>これが本件の核心。XREAL One の垂直25.7°に31.1°は入らない。</summary>
    [Test]
    public void XREAL_Oneの視野では3m前方のアバターの全身は収まらない()
    {
        Assert.IsFalse(GlassOpticsMath.FullBodyFits(EyeHeight, AvatarHeight, LeadDistance, OneVerticalFov));

        double needed = GlassOpticsMath.MinimumFullBodyDistanceMeters(EyeHeight, AvatarHeight, OneVerticalFov);
        Assert.Greater(needed, LeadDistance, "全身を収めるには3.0mより離す必要がある");
        Assert.AreEqual(3.71, needed, 0.15, "必要距離はおよそ3.7m");
    }

    [Test]
    public void One_Proでも3m前方の全身は僅かに収まらない()
    {
        double proVertical = GlassOpticsMath.VerticalFovDegrees(OneProDiagonalFov, GlassFovAxis.Diagonal, Aspect16x9);
        Assert.AreEqual(29.8, proVertical, 0.1);
        Assert.IsFalse(GlassOpticsMath.FullBodyFits(EyeHeight, AvatarHeight, LeadDistance, proVertical));

        double needed = GlassOpticsMath.MinimumFullBodyDistanceMeters(EyeHeight, AvatarHeight, proVertical);
        Assert.AreEqual(3.16, needed, 0.15, "One Pro なら3.2m弱まで詰められる");
    }

    [Test]
    public void 距離を伸ばせば必要距離の地点でちょうど収まる()
    {
        double needed = GlassOpticsMath.MinimumFullBodyDistanceMeters(EyeHeight, AvatarHeight, OneVerticalFov);
        Assert.IsTrue(GlassOpticsMath.FullBodyFits(EyeHeight, AvatarHeight, needed, OneVerticalFov));
        Assert.AreEqual(OneVerticalFov, GlassOpticsMath.BodySpanDegrees(EyeHeight, AvatarHeight, needed), 0.05);
    }

    [Test]
    public void アバターを視野中心に置く俯角はおよそ12度()
    {
        Assert.AreEqual(11.75, GlassOpticsMath.CenteringDownPitchDegrees(EyeHeight, AvatarHeight, LeadDistance), 0.1);
    }

    /// <summary>
    /// §7.2 のオーラはアバターの足元からランナー側へ伸びる。その足元が視野に入るのかを固定する。
    /// 水平に構えると6.8m先まで地面が見えない = 3.0m前方の接地点は視野外。
    /// </summary>
    [Test]
    public void 光軸が水平だと足元の地面は6m先まで見えない()
    {
        double ground = GlassOpticsMath.NearestVisibleGroundDistanceMeters(EyeHeight, OneVerticalFov, 0.0);
        Assert.AreEqual(6.78, ground, 0.05);
        Assert.Greater(ground, LeadDistance, "3.0m前方の接地点は視野に入らない");
    }

    [Test]
    public void 俯角をつけても3m前方の接地点はまだ視野に入らない()
    {
        double pitch = GlassOpticsMath.CenteringDownPitchDegrees(EyeHeight, AvatarHeight, LeadDistance);
        double ground = GlassOpticsMath.NearestVisibleGroundDistanceMeters(EyeHeight, OneVerticalFov, pitch);

        Assert.AreEqual(3.38, ground, 0.05);
        Assert.Greater(ground, LeadDistance,
            "全身が収まらない以上、中心に寄せても足元は欠ける(オーラ・接地の見えに影響)");
    }

    [Test]
    public void 視野の下端が水平より上を向いていれば地面は一切見えない()
    {
        double ground = GlassOpticsMath.NearestVisibleGroundDistanceMeters(EyeHeight, OneVerticalFov, -20.0);
        Assert.IsTrue(double.IsPositiveInfinity(ground));
    }

    [Test]
    public void 指定距離で見える最も低い高さが求まる()
    {
        // 水平・3.0m先で見えるのは 1.55 - 3.0*tan(12.87°) = 0.864m より上
        double lowest = GlassOpticsMath.LowestVisibleHeightMeters(EyeHeight, OneVerticalFov, 0.0, LeadDistance);
        Assert.AreEqual(0.864, lowest, 0.01);
        Assert.Greater(lowest, 0.0, "地面(0m)は見えていない");
    }

    [Test]
    public void 仰角は上が正で下が負()
    {
        Assert.Less(GlassOpticsMath.ElevationDegrees(EyeHeight, 0.0, LeadDistance), 0.0);
        Assert.Greater(GlassOpticsMath.ElevationDegrees(EyeHeight, 3.0, LeadDistance), 0.0);
        Assert.AreEqual(0.0, GlassOpticsMath.ElevationDegrees(EyeHeight, EyeHeight, LeadDistance), 1e-9);
    }
}
