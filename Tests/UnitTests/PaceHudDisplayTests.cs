using NUnit.Framework;

/// <summary>
/// PaceHudDisplay の検証 (F-07 右上「現在ペース」)。
/// 遅れ=赤 / 維持=緑 の境界と、表示整形の桁上がりを重点的に見る。
/// </summary>
[TestFixture]
public class PaceHudDisplayTests
{
    private const float Tol = PaceHudDisplay.DefaultBehindTolerance; // 0.05

    // ── 速度 → ペース ────────────────────────────────────────────────
    [Test]
    public void 秒速3_33mは5分キロになる()
    {
        // 1000m / 3.3333m/s = 300s = 5.00 min/km
        Assert.AreEqual(5.0f, PaceHudDisplay.SpeedToPaceMinutesPerKm(1000f / 300f), 0.001f);
    }

    [Test]
    public void 時速12kmは5分キロになる()
    {
        Assert.AreEqual(5.0f, PaceHudDisplay.KmhToPaceMinutesPerKm(12f), 0.001f);
    }

    [TestCase(0f)]
    [TestCase(0.1f)]
    [TestCase(0.29f)]
    public void 停止相当の速度はペース算出不能(float mps)
    {
        Assert.IsTrue(float.IsPositiveInfinity(PaceHudDisplay.SpeedToPaceMinutesPerKm(mps)));
    }

    [Test]
    public void 負の速度やNaNも算出不能として扱う()
    {
        Assert.IsTrue(float.IsPositiveInfinity(PaceHudDisplay.SpeedToPaceMinutesPerKm(-2f)));
        Assert.IsTrue(float.IsPositiveInfinity(PaceHudDisplay.SpeedToPaceMinutesPerKm(float.NaN)));
    }

    // ── 遅延判定 ─────────────────────────────────────────────────────
    [Test]
    public void 目標どおりなら維持()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Maintaining,
            PaceHudDisplay.Evaluate(5.0f, 5.0f, Tol));
    }

    [Test]
    public void 目標より速ければ維持_緑のまま()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Maintaining,
            PaceHudDisplay.Evaluate(4.2f, 5.0f, Tol));
    }

    [Test]
    public void 許容範囲内の遅れはまだ維持()
    {
        // 5.00 * 1.05 = 5.25 が境界
        Assert.AreEqual(PaceHudDisplay.PaceState.Maintaining,
            PaceHudDisplay.Evaluate(5.24f, 5.0f, Tol));
    }

    [Test]
    public void 境界ちょうどは維持側()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Maintaining,
            PaceHudDisplay.Evaluate(5.25f, 5.0f, Tol));
    }

    [Test]
    public void 許容を超えたら遅れ()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Behind,
            PaceHudDisplay.Evaluate(5.26f, 5.0f, Tol));
    }

    [Test]
    public void 大幅な遅れは当然遅れ()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Behind,
            PaceHudDisplay.Evaluate(7.5f, 5.0f, Tol));
    }

    [Test]
    public void 停止中は不明_赤にはしない()
    {
        float stopped = PaceHudDisplay.SpeedToPaceMinutesPerKm(0f);
        Assert.AreEqual(PaceHudDisplay.PaceState.Unknown,
            PaceHudDisplay.Evaluate(stopped, 5.0f, Tol));
    }

    [Test]
    public void 目標が未設定なら不明()
    {
        Assert.AreEqual(PaceHudDisplay.PaceState.Unknown,
            PaceHudDisplay.Evaluate(5.0f, 0f, Tol));
    }

    // ── 表示整形 ─────────────────────────────────────────────────────
    [Test]
    public void ちょうど5分キロの整形()
    {
        Assert.AreEqual("5'00\"/km", PaceHudDisplay.Format(5.0f));
    }

    [Test]
    public void 秒の桁は常に2桁()
    {
        Assert.AreEqual("4'30\"/km", PaceHudDisplay.Format(4.5f));
        Assert.AreEqual("6'06\"/km", PaceHudDisplay.Format(6.1f));
    }

    [Test]
    public void 秒が60へ丸まる場合は分へ桁上がりする()
    {
        // 4.99999分 → 299.9994秒 → 300秒 = 5'00" (4'60" になってはいけない)
        Assert.AreEqual("5'00\"/km", PaceHudDisplay.Format(4.99999f));
    }

    [Test]
    public void 算出不能なペースはダッシュ表示()
    {
        Assert.AreEqual("--'--\"/km", PaceHudDisplay.Format(float.PositiveInfinity));
        Assert.AreEqual("--'--\"/km", PaceHudDisplay.Format(0f));
        Assert.AreEqual("--'--\"/km", PaceHudDisplay.Format(float.NaN));
        Assert.AreEqual("--'--\"/km", PaceHudDisplay.Format(150f));
    }
}
