using NUnit.Framework;
using static AvatarPaceColor;

/// <summary>
/// AvatarPaceColor.Evaluate の境界検証 (基本設計書 §7.1)。
/// 目標リード3.0m・許容±1.5m・超過幅1.5m・遅延幅3.0m を前提とする。
/// </summary>
[TestFixture]
public class AvatarPaceColorTests
{
    private const float Target = 3.0f;
    private const float JustTol = 1.5f;
    private const float OverSpan = 1.5f;
    private const float RedSpan = 3.0f;

    private static PaceState Eval(float signedLead, out float t)
        => AvatarPaceColor.Evaluate(signedLead, Target, JustTol, OverSpan, RedSpan, out t);

    [TestCase(3.0f)]  // 目標ちょうど
    [TestCase(1.5f)]  // ジャスト下限境界
    [TestCase(4.5f)]  // ジャスト上限境界
    [TestCase(2.2f)]
    public void JustBand_IsJust(float lead)
    {
        Assert.AreEqual(PaceState.Just, Eval(lead, out _));
    }

    [Test]
    public void JustAboveBand_IsBehind_OrangeAtEdge()
    {
        // 4.5超で遅延、境界直上は t≈0(橙)
        var st = Eval(4.6f, out float t);
        Assert.AreEqual(PaceState.Behind, st);
        Assert.Less(t, 0.1f);
    }

    [Test]
    public void FarBehind_IsRed_TClampedToOne()
    {
        // justHigh(4.5)+redSpan(3.0)=7.5 で完全赤、それ以上もt=1
        Assert.AreEqual(1.0f, EvalT(7.5f), 0.001f);
        Assert.AreEqual(1.0f, EvalT(20f), 0.001f);
    }

    [Test]
    public void HalfBehind_IsMidGradient()
    {
        // 4.5 + 1.5 = 6.0 で t=0.5
        var st = Eval(6.0f, out float t);
        Assert.AreEqual(PaceState.Behind, st);
        Assert.AreEqual(0.5f, t, 0.001f);
    }

    [Test]
    public void BelowJust_IsOverPace()
    {
        // 1.5未満は超過(ユーザーが詰めている)
        var st = Eval(1.0f, out float t);
        Assert.AreEqual(PaceState.OverPace, st);
        Assert.Greater(t, 0f);
    }

    [Test]
    public void Overtaken_IsFullyBlue()
    {
        // justLow(1.5)-overSpan(1.5)=0.0 以下で t=1(完全に青)
        Assert.AreEqual(PaceState.OverPace, Eval(0.0f, out float t0));
        Assert.AreEqual(1.0f, t0, 0.001f);
        // 追い抜き(負)でもt=1にクランプ
        Assert.AreEqual(PaceState.OverPace, Eval(-5.0f, out float tn));
        Assert.AreEqual(1.0f, tn, 0.001f);
    }

    private static float EvalT(float lead)
    {
        AvatarPaceColor.Evaluate(lead, Target, JustTol, OverSpan, RedSpan, out float t);
        return t;
    }
}
