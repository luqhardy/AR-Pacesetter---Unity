using NUnit.Framework;

/// <summary>
/// F-09「直前1秒の平均速度・方向で慣性移動」の速度推定。
/// 以前の慣性移動は目標ペースで進み、ロスト直前に止まっていてもアバターだけ走り去っていた。
/// </summary>
[TestFixture]
public class RecentVelocityWindowTests
{
    private const float Dt = 1f / 60f;

    [Test]
    public void 等速移動の平均速度と方向()
    {
        var w = new RecentVelocityWindow(1f);
        for (int i = 0; i <= 120; i++) w.Add(i * Dt, 3f * i * Dt, 4f * i * Dt); // (3,4) m/s

        Assert.IsTrue(w.TryGetAverageVelocity(out float vx, out float vz));
        Assert.AreEqual(3f, vx, 0.01f);
        Assert.AreEqual(4f, vz, 0.01f);
    }

    [Test]
    public void 窓より古い移動は含めない()
    {
        var w = new RecentVelocityWindow(1f);
        float t = 0f, x = 0f;
        for (int i = 0; i < 120; i++, t += Dt) { x += 4f * Dt; w.Add(t, x, 0f); } // 2秒 4m/s
        for (int i = 0; i < 90; i++, t += Dt) w.Add(t, x, 0f);                     // 直前1.5秒は停止

        Assert.IsTrue(w.TryGetAverageVelocity(out float vx, out _));
        Assert.AreEqual(0f, vx, 1e-4f, "ロスト直前に止まっていれば慣性も0");
    }

    [Test]
    public void 観測区間が短すぎれば分からない()
    {
        var w = new RecentVelocityWindow(1f);
        Assert.IsFalse(w.TryGetAverageVelocity(out _, out _));
        w.Add(0f, 0f, 0f);
        w.Add(0.1f, 1f, 0f);
        Assert.IsFalse(w.TryGetAverageVelocity(out _, out _), $"< {RecentVelocityWindow.MinSpanSeconds}s");
        w.Add(0.3f, 1f, 0f);
        Assert.IsTrue(w.TryGetAverageVelocity(out _, out _));
    }

    [Test]
    public void 窓の中に1点しか無ければ分からない()
    {
        // 5秒サンプルが途絶えた後: 直前1秒の移動は観測していない(推測で埋めない)。
        // AvatarEngine はこのとき目標ペースで代用する
        var w = new RecentVelocityWindow(1f);
        w.Add(0f, 0f, 0f);
        w.Add(5f, 10f, 0f);
        Assert.IsFalse(w.TryGetAverageVelocity(out _, out _));

        w.Add(5.5f, 12f, 0f);
        Assert.IsTrue(w.TryGetAverageVelocity(out float vx, out _));
        Assert.AreEqual(4f, vx, 1e-4f);
    }

    [Test]
    public void Clearで空になる()
    {
        var w = new RecentVelocityWindow(1f);
        for (int i = 0; i < 60; i++) w.Add(i * Dt, i, 0f);
        w.Clear();
        Assert.IsFalse(w.TryGetAverageVelocity(out _, out _));
    }
}
