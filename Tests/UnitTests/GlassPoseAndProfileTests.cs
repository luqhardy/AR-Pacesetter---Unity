using System;
using NUnit.Framework;

/// <summary>
/// 表示プロファイルの決定と、頭部姿勢の供給元ポリシーの検証。
///
/// <para>ここで守っているのは「グラスに何を渡されても見え方を壊さない」こと —
/// iOSは外部ディスプレイの解像度しか知らないので、機種特定に失敗しても
/// 破綻しない既定へ落ちる必要がある。</para>
/// </summary>
[TestFixture]
public class GlassPoseAndProfileTests
{
    // ────────────────────────────────────────────────────────────────
    // GlassDisplayProfile
    // ────────────────────────────────────────────────────────────────

    [Test]
    public void 機種名でプロファイルを引ける()
    {
        Assert.AreSame(GlassDisplayProfile.XrealOne, GlassDisplayProfile.FromModelName("XREAL One"));
        Assert.AreSame(GlassDisplayProfile.XrealAir2, GlassDisplayProfile.FromModelName("xreal air 2"));
    }

    /// <summary>"XREAL One" が "XREAL One Pro" を食わないこと(部分一致の罠)。</summary>
    [Test]
    public void OneProはOneに誤って解決されない()
    {
        Assert.AreSame(GlassDisplayProfile.XrealOnePro, GlassDisplayProfile.FromModelName("XREAL One Pro"));
        Assert.AreSame(GlassDisplayProfile.XrealOnePro, GlassDisplayProfile.FromModelName("xreal-one-pro"));
    }

    [Test]
    public void 未知の機種名はnull()
    {
        Assert.IsNull(GlassDisplayProfile.FromModelName("Some Other Glasses"));
        Assert.IsNull(GlassDisplayProfile.FromModelName(null));
        Assert.IsNull(GlassDisplayProfile.FromModelName(""));
    }

    [Test]
    public void 機種名が無ければ解像度で推定し既定はXREAL_One()
    {
        var resolved = GlassDisplayProfile.Resolve(null, 1920, 1080, 60.0);
        Assert.IsNotNull(resolved);
        Assert.AreEqual("XREAL One", resolved.Model, "1080pは3機種共通のため本プロジェクトの実機を既定にする");
        Assert.AreEqual(60.0, resolved.RefreshHz, 1e-9, "実際に来た表示モードで上書きされる");
        Assert.AreEqual(50.0, resolved.FovDegrees, 1e-9, "画角は公称値のまま(グラスからは取得できない)");
    }

    [Test]
    public void 機種名は解像度より優先される()
    {
        var resolved = GlassDisplayProfile.Resolve("XREAL One Pro", 1920, 1080, 90.0);
        Assert.AreEqual("XREAL One Pro", resolved.Model);
        Assert.AreEqual(90.0, resolved.RefreshHz, 1e-9);
    }

    [Test]
    public void グラス以外のディスプレイはnullを返して上書きを見送らせる()
    {
        Assert.IsNull(GlassDisplayProfile.Resolve(null, 3840, 2160, 60.0));
        Assert.IsNull(GlassDisplayProfile.Resolve(null, 0, 0, 0.0));
    }

    [Test]
    public void iPhone画面プロファイルは投影を上書きしない()
    {
        Assert.IsFalse(GlassDisplayProfile.PhoneScreen.OverridesProjection);
        Assert.IsTrue(GlassDisplayProfile.XrealOne.OverridesProjection);
    }

    [Test]
    public void 表示モードの差し替えは画角とセーフエリアを保つ()
    {
        var swapped = GlassDisplayProfile.XrealOne.WithDisplayMode(1280, 720, 60.0);

        Assert.AreEqual(1280, swapped.PixelWidth);
        Assert.AreEqual(50.0, swapped.FovDegrees, 1e-9);
        Assert.AreEqual(GlassDisplayProfile.XrealOne.SafeAreaFraction, swapped.SafeAreaFraction, 1e-9);
        Assert.AreEqual(GlassDisplayProfile.XrealOne.VerticalFovDegrees, swapped.VerticalFovDegrees, 1e-9,
            "16:9のままなら垂直画角は変わらない");
    }

    [Test]
    public void 垂直画角はUnityのカメラへそのまま入る値になる()
    {
        Assert.AreEqual(25.7, GlassDisplayProfile.XrealOne.VerticalFovDegrees, 0.1);
        Assert.AreEqual(29.8, GlassDisplayProfile.XrealOnePro.VerticalFovDegrees, 0.1);
    }

    // ────────────────────────────────────────────────────────────────
    // HeadPoseMath — 描画カメラの向きをどこから取るか
    // ────────────────────────────────────────────────────────────────

    [Test]
    public void グラス非接続ならiPhoneカメラの姿勢で描く()
    {
        Assert.AreEqual(HeadOrientationSource.PhoneCamera,
            HeadPoseMath.ResolveOrientationSource(false, GlassScreenMode.FollowLocked, true));
    }

    [Test]
    public void グラス接続で外部姿勢が無ければ進行方向ヨーを使う()
    {
        Assert.AreEqual(HeadOrientationSource.TravelHeading,
            HeadPoseMath.ResolveOrientationSource(true, GlassScreenMode.FollowLocked, false));
        Assert.AreEqual(HeadOrientationSource.TravelHeading,
            HeadPoseMath.ResolveOrientationSource(true, GlassScreenMode.Unknown, false));
    }

    [Test]
    public void Follow固定で外部姿勢が来ていればそれを使う()
    {
        Assert.AreEqual(HeadOrientationSource.ExternalGlassPose,
            HeadPoseMath.ResolveOrientationSource(true, GlassScreenMode.FollowLocked, true));
    }

    /// <summary>
    /// Anchorモードはグラスが自前の3DoFで頭回転を打ち消す。そこへUnity側の頭部姿勢を足すと
    /// 補正が二重にかかるため、外部姿勢が来ていても進行方向ヨーに留める。
    /// </summary>
    [Test]
    public void Anchorモードでは外部姿勢が来ていても採用しない()
    {
        Assert.AreEqual(HeadOrientationSource.TravelHeading,
            HeadPoseMath.ResolveOrientationSource(true, GlassScreenMode.Anchor, true));
    }

    [Test]
    public void 二重補正のリスクはAnchorと外部姿勢の組み合わせのみ()
    {
        Assert.IsTrue(HeadPoseMath.IsDoubleCompensationRisk(
            GlassScreenMode.Anchor, HeadOrientationSource.ExternalGlassPose));
        Assert.IsFalse(HeadPoseMath.IsDoubleCompensationRisk(
            GlassScreenMode.Anchor, HeadOrientationSource.TravelHeading));
        Assert.IsFalse(HeadPoseMath.IsDoubleCompensationRisk(
            GlassScreenMode.FollowLocked, HeadOrientationSource.ExternalGlassPose));
    }

    [Test]
    public void 外部姿勢の鮮度判定は上限秒で切れる()
    {
        Assert.IsTrue(HeadPoseMath.IsExternalPoseFresh(10.0f, 9.9f, 0.25f));
        Assert.IsFalse(HeadPoseMath.IsExternalPoseFresh(10.0f, 9.0f, 0.25f), "1秒前は古い");
        Assert.IsFalse(HeadPoseMath.IsExternalPoseFresh(10.0f, 0f, 0.25f), "未受信は常に古い");
        Assert.IsFalse(HeadPoseMath.IsExternalPoseFresh(10.0f, 11.0f, 0.25f), "未来の時刻は信用しない");
    }

    [Test]
    public void 胸マウントから眼までの持ち上げ量()
    {
        Assert.AreEqual(0.35f,
            HeadPoseMath.ChestToEyeOffsetMeters(HeadPoseMath.DefaultChestMountHeightMeters,
                                                HeadPoseMath.DefaultEyeHeightMeters), 1e-5f);
    }

    [Test]
    public void 俯角は視野中心へ寄せる値になり上限でクランプされる()
    {
        double vFov = GlassDisplayProfile.XrealOne.VerticalFovDegrees;

        Assert.AreEqual(11.75, HeadPoseMath.ResolveDownPitchDegrees(1.55, 1.75, 3.0, vFov, 15.0), 0.1);
        Assert.AreEqual(5.0, HeadPoseMath.ResolveDownPitchDegrees(1.55, 1.75, 3.0, vFov, 5.0), 1e-9,
            "上限を超えたらクランプ");
    }

    [Test]
    public void 見上げる配置では俯角をつけない()
    {
        double vFov = GlassDisplayProfile.XrealOne.VerticalFovDegrees;
        // アバターが極端に高い(=中心が水平より上)ケースでは負の俯角にせず0へ丸める
        Assert.AreEqual(0.0, HeadPoseMath.ResolveDownPitchDegrees(1.55, 6.0, 3.0, vFov, 15.0), 1e-9);
    }
    // ── 首振り(タスク表 ARグラス「首を振り向けた時に想定する場所を走る」) ──────

    [Test]
    public void 首振りは進行方向ヨーに足す()
    {
        // 進行方向 90°(東)で右へ30°向いたら、描画は120°を向く = アバターは画面の左へ流れる
        Assert.AreEqual(120f, HeadPoseMath.ResolveViewYawDegrees(90f, 30f, applyOffset: true), 1e-4f);
        Assert.AreEqual(60f, HeadPoseMath.ResolveViewYawDegrees(90f, -30f, applyOffset: true), 1e-4f);
    }

    [Test]
    public void 首振りは90度で打ち切る()
    {
        Assert.AreEqual(90f + HeadPoseMath.MaxHeadYawOffsetDegrees,
            HeadPoseMath.ResolveViewYawDegrees(90f, 170f, applyOffset: true), 1e-4f);
    }

    [Test]
    public void 古い首振りや数でない値は足さない()
    {
        Assert.AreEqual(90f, HeadPoseMath.ResolveViewYawDegrees(90f, 30f, applyOffset: false), 1e-4f);
        Assert.AreEqual(90f, HeadPoseMath.ResolveViewYawDegrees(90f, float.NaN, applyOffset: true), 1e-4f);
    }

    [Test]
    public void Anchorでは首振りを足さない_二重補正になる()
    {
        Assert.IsTrue(HeadPoseMath.ShouldApplyHeadYaw(GlassScreenMode.FollowLocked, headYawFresh: true));
        Assert.IsFalse(HeadPoseMath.ShouldApplyHeadYaw(GlassScreenMode.Anchor, headYawFresh: true));
        Assert.IsFalse(HeadPoseMath.ShouldApplyHeadYaw(GlassScreenMode.FollowLocked, headYawFresh: false));
    }
    [Test]
    public void 角度差は前後180度へ折り返す()
    {
        Assert.AreEqual(-170f, HeadPoseMath.WrapDegrees(190f), 1e-4f);
        Assert.AreEqual(170f, HeadPoseMath.WrapDegrees(-190f), 1e-4f);
        Assert.AreEqual(0f, HeadPoseMath.WrapDegrees(720f), 1e-4f);
        Assert.AreEqual(0f, HeadPoseMath.WrapDegrees(float.NaN), 1e-4f);
    }

    [Test]
    public void 最初の頭の向きをまっすぐ前とみなす()
    {
        // AirPods の向きの基準は任意(ここでは進行方向から 137° ずれている)
        var b = new HeadYawBaseline();
        Assert.AreEqual(0f, b.Update(headYawDegrees: 227f, travelYawDegrees: 90f, deltaSeconds: 0.016f), 1e-4f);
        Assert.IsTrue(b.IsInitialized);
    }

    [Test]
    public void 数秒の首振りはほぼそのまま残る()
    {
        var b = new HeadYawBaseline();
        b.Update(227f, 90f, 0.016f);
        // 2秒かけて右へ40°向いた
        float offset = 0f;
        for (int i = 1; i <= 120; i++)
            offset = b.Update(227f + 40f * i / 120f, 90f, 1f / 60f);
        Assert.AreEqual(40f, offset, 2f);
    }

    [Test]
    public void カーブでは頭と進行方向が一緒に回るので首振りにならない()
    {
        var b = new HeadYawBaseline();
        b.Update(10f, 0f, 0.016f);
        float offset = 0f;
        // 10秒で180°のコーナー(頭も体と一緒に回る)
        for (int i = 1; i <= 600; i++)
        {
            float turn = 180f * i / 600f;
            offset = b.Update(10f + turn, turn, 1f / 60f);
        }
        Assert.AreEqual(0f, offset, 0.01f);
    }

    [Test]
    public void ずっと横を向いたままの差やドリフトはゆっくり0へ戻る()
    {
        var b = new HeadYawBaseline();
        b.Update(0f, 0f, 0.016f);
        float offset = 0f;
        for (int i = 0; i < 60 * 120; i++) // 2分
            offset = b.Update(30f, 0f, 1f / 60f);
        Assert.Less(Math.Abs(offset), 1f);
    }

    [Test]
    public void リセットすると次のサンプルを基準に取り直す()
    {
        var b = new HeadYawBaseline();
        b.Update(0f, 0f, 0.016f);
        Assert.AreEqual(30f, b.Update(30f, 0f, 0.016f), 0.1f);
        b.Reset();
        Assert.AreEqual(0f, b.Update(30f, 0f, 0.016f), 1e-4f);
    }
}
