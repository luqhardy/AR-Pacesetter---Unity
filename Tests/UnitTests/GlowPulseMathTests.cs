using NUnit.Framework;

/// <summary>
/// バイオルミネッセンス(§4.4)の発光強度。
/// 以前は既定値(base 1.0 / amplitude 1.5)で周期の約27%が負になり、
/// 心拍が一度も届いていなくても仮の60bpmで脈動していた。
/// </summary>
[TestFixture]
public class GlowPulseMathTests
{
    private const float Base = 1.0f, Amp = 1.5f; // AvatarVisualsAndActions の既定値

    [Test]
    public void 発光強度は負にならない()
    {
        for (float t = 0f; t < 10f; t += 0.01f)
            Assert.GreaterOrEqual(GlowPulseMath.Intensity(Base, Amp, true, 60, t), 0f, $"t={t}");
    }

    [Test]
    public void 心拍が届いていなければ脈動せず一定()
    {
        for (float t = 0f; t < 5f; t += 0.1f)
            Assert.AreEqual(Base, GlowPulseMath.Intensity(Base, Amp, false, 60, t), 1e-6f);
        Assert.AreEqual(Base, GlowPulseMath.Intensity(Base, Amp, true, 0, 1.3f), 1e-6f, "0bpm = 不明");
    }

    [Test]
    public void 心拍があれば式どおりに脈動する()
    {
        // HR=120 → sin(t·2·π)。t=0.25 で最大
        Assert.AreEqual(Base + Amp, GlowPulseMath.Intensity(Base, Amp, true, 120, 0.25f), 1e-4f);
        Assert.AreEqual(Base, GlowPulseMath.Intensity(Base, Amp, true, 120, 0.5f), 1e-4f);
    }
}
