using NUnit.Framework;

/// <summary>
/// AuraFeedback.TryEvaluate の検証 (基本設計書 §7.2)。
/// 発動閾値5.0m・最大強度12.0mを前提とする。
/// </summary>
[TestFixture]
public class AuraFeedbackTests
{
    private const float Activation = 5.0f;
    private const float Full = 12.0f;

    private static bool Eval(float deviation, out float intensity)
        => AuraFeedback.TryEvaluate(deviation, Activation, Full, out intensity);

    [TestCase(0.0f)]
    [TestCase(3.0f)]
    [TestCase(4.99f)]
    public void BelowActivation_DoesNotEmit(float deviation)
    {
        Assert.IsFalse(Eval(deviation, out float t), $"deviation {deviation}m では非発動のはず");
        Assert.AreEqual(0f, t, 0.0001f);
    }

    [Test]
    public void OnPaceOrAhead_DoesNotEmit()
    {
        // 目標より前(負の遅延)でも当然非発動
        Assert.IsFalse(Eval(-4.0f, out float t));
        Assert.AreEqual(0f, t, 0.0001f);
    }

    [Test]
    public void AtActivation_EmitsAtZeroIntensity()
    {
        Assert.IsTrue(Eval(5.0f, out float t));
        Assert.AreEqual(0f, t, 0.0001f);
    }

    [Test]
    public void Midway_IsHalfIntensity()
    {
        // 5.0 + (12.0-5.0)/2 = 8.5 で 0.5
        Assert.IsTrue(Eval(8.5f, out float t));
        Assert.AreEqual(0.5f, t, 0.001f);
    }

    [Test]
    public void AtOrBeyondFull_IsClampedToOne()
    {
        Assert.IsTrue(Eval(12.0f, out float t1));
        Assert.AreEqual(1f, t1, 0.0001f);

        Assert.IsTrue(Eval(50.0f, out float t2));
        Assert.AreEqual(1f, t2, 0.0001f);
    }

    [Test]
    public void DegenerateSpan_IsFullIntensity()
    {
        // activation == full の設定ミスでも0除算せず最大強度で発動
        Assert.IsTrue(AuraFeedback.TryEvaluate(6.0f, 5.0f, 5.0f, out float t));
        Assert.AreEqual(1f, t, 0.0001f);
    }
}
