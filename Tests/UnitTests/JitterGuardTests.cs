using NUnit.Framework;

/// <summary>
/// §3 Jitter Tolerance(±5ms超のフレームは測定を捨てて予測)の検証。
///
/// <para>本丸は「フレーム時間が交互に揺れても予測だけで走り続けないこと」。以前は
/// 直前フレームとの差で判定し、予測の継続に上限が無かったため、16.7/33.3ms の交互で
/// 毎フレームが変動扱いになり、アバターは測定を一度も取り込まず直進し続けた。</para>
/// </summary>
[TestFixture]
public class JitterGuardTests
{
    private const float F60 = 1f / 60f;

    [Test]
    public void 安定した60fpsでは予測しない()
    {
        var g = new JitterGuard();
        for (int i = 0; i < 600; i++)
            Assert.IsFalse(g.ShouldPredict(F60 + (i % 2 == 0 ? 0.001f : -0.001f)), $"frame {i}");
    }

    [Test]
    public void 単発のヒッチは1フレームだけ予測する()
    {
        var g = new JitterGuard();
        for (int i = 0; i < 60; i++) g.ShouldPredict(F60);

        Assert.IsTrue(g.ShouldPredict(0.050f), "跳ねたフレーム");
        // 以前は「戻ったフレーム」も直前との差が大きく、2回目の変動と数えていた
        Assert.IsFalse(g.ShouldPredict(F60), "戻ったフレームは通常");
    }

    [Test]
    public void 交互のフレーム時間でも定期的に測定を取り込む()
    {
        var g = new JitterGuard();
        for (int i = 0; i < 60; i++) g.ShouldPredict(F60);

        int measured = 0, longestPredictRun = 0, run = 0;
        for (int i = 0; i < 600; i++) // 発熱で 16.7 / 33.3ms が交互に続く
        {
            float dt = i % 2 == 0 ? F60 : 2f * F60;
            if (g.ShouldPredict(dt)) { run++; if (run > longestPredictRun) longestPredictRun = run; }
            else { measured++; run = 0; }
        }

        Assert.Greater(measured, 600 / 10, "少なくとも10フレームに1回は測定を取り込む");
        Assert.LessOrEqual(longestPredictRun * 2f * F60, JitterGuard.MaxPredictionSeconds + 2f * F60,
            "予測だけで進む時間は上限まで");
    }

    [Test]
    public void 予測の累計は上限で打ち切られる()
    {
        var g = new JitterGuard();
        g.ShouldPredict(F60);

        float predicted = 0f;
        // 毎フレーム基準から大きく外れる(基準が追いつく前に上限へ届く)
        for (int i = 0; i < 20; i++)
        {
            float dt = i % 2 == 0 ? 0.004f : 0.040f;
            if (g.ShouldPredict(dt)) predicted += dt; else break;
        }
        Assert.LessOrEqual(predicted, JitterGuard.MaxPredictionSeconds + 0.040f);
    }

    [Test]
    public void 経過時間が取れないフレームは判定しない()
    {
        var g = new JitterGuard();
        Assert.IsFalse(g.ShouldPredict(0f));
        Assert.IsFalse(g.ShouldPredict(-1f));
        Assert.AreEqual(0f, g.BaselineSeconds);
    }

    [Test]
    public void Resetで基準を取り直す()
    {
        var g = new JitterGuard();
        for (int i = 0; i < 30; i++) g.ShouldPredict(F60);
        g.Reset();
        Assert.IsFalse(g.ShouldPredict(1f / 30f), "リセット直後の最初のフレームは基準になるだけ");
        Assert.AreEqual(1f / 30f, g.BaselineSeconds, 1e-6f);
    }
}
