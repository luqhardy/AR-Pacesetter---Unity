using NUnit.Framework;

/// <summary>
/// カルマンフィルタ(C#移植)の検証。
/// 旧C++実装は実機でしか動かず、テストに一度も掛かっていなかった。
/// ここで「収束する・ノイズを減らす・発散しない・リセットで引きずらない」を縛る。
/// </summary>
[TestFixture]
public class SpatialKalmanFilterTests
{
    [Test]
    public void 初回の観測で初期化され引き込みが無い()
    {
        var f = new SpatialKalmanFilter();
        Assert.IsFalse(f.IsSeeded);

        f.Update(10f, 20f, 30f, out float x, out float y, out float z);

        Assert.IsTrue(f.IsSeeded);
        Assert.AreEqual(10f, x, 0.0001f);
        Assert.AreEqual(20f, y, 0.0001f);
        Assert.AreEqual(30f, z, 0.0001f);
    }

    [Test]
    public void 一定の観測には収束する()
    {
        var f = new SpatialKalmanFilter();
        float x = 0, y = 0, z = 0;
        for (int i = 0; i < 300; i++)
            f.Update(5f, 0f, -2f, out x, out y, out z);

        Assert.AreEqual(5f, x, 0.001f);
        Assert.AreEqual(0f, y, 0.001f);
        Assert.AreEqual(-2f, z, 0.001f);
    }

    [Test]
    public void 白色ノイズを減衰させる()
    {
        var f = new SpatialKalmanFilter();
        var rng = new System.Random(7);
        double rawVar = 0, outVar = 0;
        int n = 0;

        for (int i = 0; i < 2000; i++)
        {
            float noise = (float)(rng.NextDouble() * 2 - 1) * 0.1f; // ±10cm
            f.Update(noise, 0f, 0f, out float x, out _, out _);
            if (i > 200) { rawVar += noise * noise; outVar += x * x; n++; }
        }

        Assert.Less(outVar / n, rawVar / n * 0.5, "出力の分散が入力の半分未満に落ちる");
    }

    [Test]
    public void 等速で動く観測に発散せず追従する()
    {
        var f = new SpatialKalmanFilter();
        float x = 0;
        for (int i = 0; i < 600; i++)
            f.Update(i * 0.06f, 0f, 0f, out x, out _, out _); // 3.6m/s @60fps

        float target = 599 * 0.06f;
        Assert.Less(System.Math.Abs(target - x), 0.5f, "速度項があるので定常遅れは小さい");
    }

    [Test]
    public void 段差入力の行き過ぎは2割強まで()
    {
        // 0 で収束させてから 1.0 へ飛ばす。速度(トレンド)項があるため段差では
        // 約18%行き過ぎる — 旧C++実装と同じ挙動。位置を飛ばす操作(再同期・リセット)では
        // Reset を呼んで段差を作らないので実害は無いが、性質として固定しておく
        var f = new SpatialKalmanFilter();
        for (int i = 0; i < 300; i++) f.Update(0f, 0f, 0f, out _, out _, out _);

        float max = float.MinValue;
        for (int i = 0; i < 300; i++)
        {
            f.Update(1f, 0f, 0f, out float x, out _, out _);
            if (x > max) max = x;
        }
        Assert.Less(max, 1.25f, "オーバーシュートは25%未満(実測≒18%)");
        Assert.Greater(max, 1.0f, "速度項があるので必ず少し行き過ぎる(性質の記録)");
    }

    [Test]
    public void リセットすると古い推定を引きずらない()
    {
        var f = new SpatialKalmanFilter();
        for (int i = 0; i < 300; i++) f.Update(100f, 0f, 0f, out _, out _, out _);

        f.Reset();
        f.Update(0f, 0f, 0f, out float x, out _, out _);

        Assert.AreEqual(0f, x, 0.0001f, "リセット後の最初の観測でそのまま初期化される");
    }

    [Test]
    public void 壊れた観測は無視して前の推定を返す()
    {
        var f = new SpatialKalmanFilter();
        f.Update(3f, 3f, 3f, out _, out _, out _);
        f.Update(float.NaN, float.PositiveInfinity, 3f, out float x, out float y, out _);

        Assert.AreEqual(3f, x, 0.0001f);
        Assert.AreEqual(3f, y, 0.0001f);
    }

    [Test]
    public void 不正なパラメータは既定値へ()
    {
        // 例外にせず動くこと(実機で初期化に失敗しても追従が止まらない)
        var f = new SpatialKalmanFilter(-1f, 0f, 5f);
        f.Update(1f, 1f, 1f, out float x, out _, out _);
        Assert.AreEqual(1f, x, 0.0001f);
    }
}
