using NUnit.Framework;

/// <summary>
/// 自走モード(タスク表「指定した速さで走る」)の前方距離 — 遅れた分だけ前へ、速ければ近づく
/// </summary>
[TestFixture]
public class SelfPacedLeadTests
{
    [Test]
    public void 目標ペースどおりなら目標リードのまま()
    {
        Assert.AreEqual(3.0f, SelfPacedLead.DisplayedLead(3.0f, 0f), 1e-5f);
        Assert.AreEqual(0f, SelfPacedLead.LeadOffset(3.0f, 0f), 1e-5f);
    }

    [Test]
    public void 遅れた分だけ前へ離れる()
    {
        // 4m 遅れ(偏差 −4m)なら 7m 先
        Assert.AreEqual(7.0f, SelfPacedLead.DisplayedLead(3.0f, -4f), 1e-5f);
        Assert.AreEqual(4.0f, SelfPacedLead.LeadOffset(3.0f, -4f), 1e-5f);
    }

    [Test]
    public void 速ければ近づく()
    {
        Assert.AreEqual(1.5f, SelfPacedLead.DisplayedLead(3.0f, 1.5f), 1e-5f);
    }

    [Test]
    public void 離隔待機に入らず視野から外れない範囲に収める()
    {
        Assert.AreEqual(SelfPacedLead.MaxDisplayedLeadMeters, SelfPacedLead.DisplayedLead(3.0f, -50f), 1e-5f);
        Assert.Less(SelfPacedLead.MaxDisplayedLeadMeters, 10f, "10m で離隔待機に入る");
        Assert.AreEqual(SelfPacedLead.MinDisplayedLeadMeters, SelfPacedLead.DisplayedLead(3.0f, 20f), 1e-5f);
    }

    [Test]
    public void 数でない値は目標リードのまま()
    {
        Assert.AreEqual(3.0f, SelfPacedLead.DisplayedLead(3.0f, float.NaN), 1e-5f);
        Assert.AreEqual(3.0f, SelfPacedLead.DisplayedLead(float.NaN, 0f), 1e-5f);
    }
    // ── 目標ペースの距離(SelfPacedTarget) ──────────────────────────────

    [Test]
    public void 目標の速さで経過時間ぶん進む()
    {
        var target = new SelfPacedTarget();
        for (int i = 0; i < 60; i++) target.Add(1.0 / 60.0, 3.6);   // 1秒
        Assert.AreEqual(3.6, target.TargetDistanceMeters, 1e-6);
        Assert.AreEqual(-0.6f, target.DeviationMeters(3.0), 1e-5f, "3m しか走っていなければ 0.6m の遅れ");
    }

    [Test]
    public void ペースが途中で変わっても積算する()
    {
        var target = new SelfPacedTarget();
        target.Add(10, 3.0);
        target.Add(10, 4.0);
        Assert.AreEqual(70.0, target.TargetDistanceMeters, 1e-9);
    }

    [Test]
    public void 数えられなかった時間だけ壁時計で補う()
    {
        // 普段のフレーム(壁時計とフレーム時間がほぼ同じ)は補わない
        Assert.AreEqual(0.0, SelfPacedTarget.MissedSeconds(0.0167, 0.0167, 1.0), 1e-9);
        Assert.AreEqual(0.0, SelfPacedTarget.MissedSeconds(0.5, 0.1, 1.0), 1e-9, "1秒未満の揺れは補わない");
        // スタンバイで30秒止まっていた
        Assert.AreEqual(29.98, SelfPacedTarget.MissedSeconds(30.0, 0.02, 1.0), 1e-9);
        // E2E の早回し(timeScale 3)では壁時計1秒 = ゲーム内3秒
        Assert.AreEqual(0.0, SelfPacedTarget.MissedSeconds(0.1, 0.3, 3.0), 1e-9);
    }

    [Test]
    public void 数でない値やリセット()
    {
        var target = new SelfPacedTarget();
        target.Add(double.NaN, 3.6);
        target.Add(1, double.NaN);
        target.Add(-1, 3.6);
        Assert.AreEqual(0.0, target.TargetDistanceMeters, 1e-9);
        target.Add(5, 3.0);
        target.Reset();
        Assert.AreEqual(0.0, target.TargetDistanceMeters, 1e-9);
        Assert.AreEqual(0f, target.DeviationMeters(double.NaN), 1e-9f);
    }
}
