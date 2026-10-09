using NUnit.Framework;

/// <summary>
/// フレーム時間依存の平滑化の検証。
///
/// <para>本丸は「長いフレームで追従が瞬間移動に化けないこと」。
/// <c>Lerp(現在, 目標, dt * k)</c> は dt が 1/k 秒を超えると係数が1に飽和し、
/// 補間ではなく目標へのテレポートになる。k=2.5(通常)で400ms、
/// k=4.0(追い抜き中)では**250ms**で成立してしまう。</para>
/// </summary>
[TestFixture]
public class FrameSmoothingTests
{
    private const float NormalRate = 2.5f;   // AvatarEngine の通常追従
    private const float SprintRate = 4.0f;   // 追い抜き中

    [Test]
    public void 飽和し始めるフレーム時間は追従速度の逆数()
    {
        Assert.AreEqual(0.4f, FrameSmoothing.SaturationThresholdSeconds(NormalRate), 1e-6f);
        Assert.AreEqual(0.25f, FrameSmoothing.SaturationThresholdSeconds(SprintRate), 1e-6f);
    }

    [Test]
    public void 実測された長いフレームは飽和する()
    {
        // E2E計測: 走行中の最大 deltaTime 330ms、実際に飽和したフレーム 702ms
        Assert.IsFalse(FrameSmoothing.WouldSaturate(0.330f, NormalRate), "330msはまだ飽和しない(k=2.5)");
        Assert.IsTrue(FrameSmoothing.WouldSaturate(0.702f, NormalRate), "702msは飽和する");

        // 追い抜き中は閾値が250msまで下がるので、330msでも飽和する
        Assert.IsTrue(FrameSmoothing.WouldSaturate(0.330f, SprintRate),
            "追い抜き中(k=4.0)は330msのフレームで既に瞬間移動になる");
    }

    [Test]
    public void 通常のフレームでは従来と同じ補間係数になる()
    {
        // 60fps(16.7ms)・30fps(33ms)・10fps(100ms)まではクランプが効かない
        Assert.AreEqual(0.0167f * NormalRate, FrameSmoothing.Factor(0.0167f, NormalRate), 1e-6f);
        Assert.AreEqual(0.0333f * NormalRate, FrameSmoothing.Factor(0.0333f, NormalRate), 1e-6f);
        Assert.AreEqual(0.1f * NormalRate, FrameSmoothing.Factor(0.1f, NormalRate), 1e-6f);
    }

    [Test]
    public void 長いフレームでも補間係数は頭打ちになり瞬間移動しない()
    {
        float factor = FrameSmoothing.Factor(0.702f, NormalRate);

        Assert.AreEqual(FrameSmoothing.MaxSmoothingDeltaSeconds * NormalRate, factor, 1e-6f);
        Assert.Less(factor, 1f, "係数が1に達しない = 目標へ飛ばない");
    }

    [Test]
    public void どれだけ長いフレームでも係数は1未満に留まる()
    {
        foreach (float dt in new[] { 0.5f, 1f, 5f, 60f })
            Assert.Less(FrameSmoothing.Factor(dt, NormalRate), 1f, $"dt={dt}s");
    }

    /// <summary>追従速度が極端に速い設定では上限内でも1に達しうる。そこは丸めて守る。</summary>
    [Test]
    public void 追従速度が極端でも係数は1を超えない()
    {
        Assert.AreEqual(1f, FrameSmoothing.Factor(0.5f, 50f), 1e-6f);
        Assert.LessOrEqual(FrameSmoothing.Factor(0.016f, 1000f), 1f);
    }

    [Test]
    public void 経過時間の上限は指定できる()
    {
        Assert.AreEqual(0.02f, FrameSmoothing.ClampDelta(0.5f, 0.02f), 1e-6f);
        Assert.AreEqual(0.01f, FrameSmoothing.ClampDelta(0.01f, 0.02f), 1e-6f);
    }

    [Test]
    public void 不正な入力では動かさない()
    {
        Assert.AreEqual(0f, FrameSmoothing.Factor(0.016f, 0f), 1e-6f, "追従速度0なら動かさない");
        Assert.AreEqual(0f, FrameSmoothing.Factor(0.016f, -1f), 1e-6f);
        Assert.AreEqual(0f, FrameSmoothing.Factor(0f, NormalRate), 1e-6f);
        Assert.AreEqual(0f, FrameSmoothing.ClampDelta(-1f), 1e-6f);
        Assert.IsFalse(FrameSmoothing.WouldSaturate(10f, 0f));
    }

    [Test]
    public void 上限が無効なら経過時間をそのまま返す()
    {
        Assert.AreEqual(0.5f, FrameSmoothing.ClampDelta(0.5f, 0f), 1e-6f);
    }
}
