using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 差し替えアバターの受け入れ判定の検証。
///
/// <para>ここが守っているのは「見た目のために計測を壊さない」こと。第1期の成果物は
/// 走行ログCSVであって見栄えではないので、60fps と M2P 20ms(§10)を割るモデルは
/// 黙って読み込まずに<b>理由を付けて断る</b>。</para>
/// </summary>
[TestFixture]
public class VrmAvatarPolicyTests
{
    /// <summary>VRChat向けアバターとしては軽量な部類(受け入れられる想定)。</summary>
    private static VrmAvatarProfile Light() => new VrmAvatarProfile
    {
        Name = "Light",
        HasHumanoidRig = true,
        TriangleCount = 24000,
        MaterialCount = 3,
        SkinnedMeshCount = 2,
        RendererCount = 2,
        EmissiveMaterialCount = 1,
        MeasuredHeightMeters = 1.62f,
    };

    [Test]
    public void 軽量でリグのあるモデルは受け入れる()
    {
        Assert.IsTrue(VrmAvatarPolicy.IsAcceptable(Light()));
        Assert.AreEqual(VrmRejectReason.None, VrmAvatarPolicy.Evaluate(Light()));
    }

    /// <summary>リグが無いと歩行・走行アニメーションを当てられない。差し替えの前提条件。</summary>
    [Test]
    public void ヒューマノイドリグが無ければ他が完璧でも断る()
    {
        var p = Light();
        p.HasHumanoidRig = false;

        Assert.AreEqual(VrmRejectReason.NoHumanoidRig, VrmAvatarPolicy.Evaluate(p));
        Assert.IsFalse(VrmAvatarPolicy.IsAcceptable(p));
    }

    [Test]
    public void 描画するメッシュが無ければ断る()
    {
        var p = Light();
        p.RendererCount = 0;

        Assert.AreEqual(VrmRejectReason.NoRenderer, VrmAvatarPolicy.Evaluate(p));
    }

    /// <summary>
    /// VRChat向けアバターは7万〜20万三角形が珍しくない。そのまま通すと
    /// ARKit・GPS・100Hz IMU・CSV書き出しと同居して60fpsを守れない。
    /// </summary>
    [Test]
    public void 三角形が多すぎるモデルは断る()
    {
        var p = Light();
        p.TriangleCount = VrmAvatarPolicy.MaxTriangles + 1;

        Assert.AreEqual(VrmRejectReason.TooManyTriangles, VrmAvatarPolicy.Evaluate(p));
    }

    [Test]
    public void 上限ちょうどは受け入れる()
    {
        var p = Light();
        p.TriangleCount = VrmAvatarPolicy.MaxTriangles;
        p.MaterialCount = VrmAvatarPolicy.MaxMaterials;
        p.SkinnedMeshCount = VrmAvatarPolicy.MaxSkinnedMeshes;

        Assert.AreEqual(VrmRejectReason.None, VrmAvatarPolicy.Evaluate(p), "境界は許容側");
    }

    [Test]
    public void マテリアルとスキンメッシュの上限も効く()
    {
        var many = Light();
        many.MaterialCount = VrmAvatarPolicy.MaxMaterials + 1;
        Assert.AreEqual(VrmRejectReason.TooManyMaterials, VrmAvatarPolicy.Evaluate(many));

        var skins = Light();
        skins.SkinnedMeshCount = VrmAvatarPolicy.MaxSkinnedMeshes + 1;
        Assert.AreEqual(VrmRejectReason.TooManySkinnedMeshes, VrmAvatarPolicy.Evaluate(skins));
    }

    /// <summary>直せない理由(リグ)は、重さの理由より先に報告する。</summary>
    [Test]
    public void リグ不足は重さより先に報告される()
    {
        var p = Light();
        p.HasHumanoidRig = false;
        p.TriangleCount = 500000;

        Assert.AreEqual(VrmRejectReason.NoHumanoidRig, VrmAvatarPolicy.Evaluate(p));
    }

    // ────────────────────────────────────────────────────────────────
    // §7.1 ペースシンクロ色
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 発光マテリアルが無いモデルは<b>走れるが色が出ない</b>。
    /// 拒否はしない代わりに、警告として必ず表に出す。
    /// </summary>
    [Test]
    public void 発光が無くても拒否はしないが警告に出す()
    {
        var p = Light();
        p.EmissiveMaterialCount = 0;

        Assert.IsTrue(VrmAvatarPolicy.IsAcceptable(p), "走行自体はできる");
        Assert.IsFalse(VrmAvatarPolicy.SupportsPaceColor(p));
        StringAssert.Contains("ペース色", VrmAvatarPolicy.Describe(p));
    }

    [Test]
    public void 発光があればペース色の警告は出ない()
    {
        var p = Light();
        Assert.IsTrue(VrmAvatarPolicy.SupportsPaceColor(p));
        Assert.IsFalse(VrmAvatarPolicy.Describe(p).Contains("ペース色"));
    }

    // ────────────────────────────────────────────────────────────────
    // 説明文と一覧
    // ────────────────────────────────────────────────────────────────

    [Test]
    public void 説明文に判定と実測値が入る()
    {
        string ok = VrmAvatarPolicy.Describe(Light());
        StringAssert.Contains("Light", ok);
        StringAssert.Contains("使用可", ok);
        StringAssert.Contains("24,000", ok);
        StringAssert.Contains("1.62m", ok);

        var heavy = Light();
        heavy.TriangleCount = 200000;
        string ng = VrmAvatarPolicy.Describe(heavy);
        StringAssert.Contains("使用不可", ng);
        StringAssert.Contains("60fps", ng, "なぜ断ったのかが分かること");
    }

    /// <summary>選択UIには「選べない選択肢」を並べない。</summary>
    [Test]
    public void 使用可能なものだけを順序を保って返す()
    {
        var heavy = Light(); heavy.Name = "Heavy"; heavy.TriangleCount = 200000;
        var norig = Light(); norig.Name = "NoRig"; norig.HasHumanoidRig = false;
        var a = Light(); a.Name = "A";
        var b = Light(); b.Name = "B";

        List<VrmAvatarProfile> kept = VrmAvatarPolicy.FilterAcceptable(
            new List<VrmAvatarProfile> { a, heavy, norig, b });

        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual("A", kept[0].Name);
        Assert.AreEqual("B", kept[1].Name);
    }

    [Test]
    public void 入力が無くても落ちない()
    {
        Assert.AreEqual(0, VrmAvatarPolicy.FilterAcceptable(null).Count);
        Assert.AreEqual(VrmRejectReason.NoHumanoidRig, VrmAvatarPolicy.Evaluate(default(VrmAvatarProfile)));
        Assert.IsNotNull(VrmAvatarPolicy.Describe(default(VrmAvatarProfile)));
    }
}
