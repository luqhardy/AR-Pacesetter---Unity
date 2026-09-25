using NUnit.Framework;

/// <summary>
/// カルマンフィルタ(C#移植)の検証。
/// 旧C++実装は実機でしか動かず、テストに一度も掛かっていなかった。
/// ここで「収束する・ノイズを減らす・発散しない・リセットで引きずらない」を縛る。
/// </summary>
[TestFixture]
public class SpatialKalmanFilterTests
{
    /// <summary>基準フレームレート(60fps)の経過時間。旧フレーム単位実装と数値が一致する。</summary>
    private const float Dt = 1f / SpatialKalmanFilter.ReferenceFrameRate;

    [Test]
    public void 初回の観測で初期化され引き込みが無い()
    {
        var f = new SpatialKalmanFilter();
        Assert.IsFalse(f.IsSeeded);

        f.Update(10f, 20f, 30f, Dt, out float x, out float y, out float z);

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
            f.Update(5f, 0f, -2f, Dt, out x, out y, out z);

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
            f.Update(noise, 0f, 0f, Dt, out float x, out _, out _);
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
            f.Update(i * 0.06f, 0f, 0f, Dt, out x, out _, out _); // 3.6m/s @60fps

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
        for (int i = 0; i < 300; i++) f.Update(0f, 0f, 0f, Dt, out _, out _, out _);

        float max = float.MinValue;
        for (int i = 0; i < 300; i++)
        {
            f.Update(1f, 0f, 0f, Dt, out float x, out _, out _);
            if (x > max) max = x;
        }
        Assert.Less(max, 1.25f, "オーバーシュートは25%未満(実測≒18%)");
        Assert.Greater(max, 1.0f, "速度項があるので必ず少し行き過ぎる(性質の記録)");
    }

    [Test]
    public void リセットすると古い推定を引きずらない()
    {
        var f = new SpatialKalmanFilter();
        for (int i = 0; i < 300; i++) f.Update(100f, 0f, 0f, Dt, out _, out _, out _);

        f.Reset();
        f.Update(0f, 0f, 0f, Dt, out float x, out _, out _);

        Assert.AreEqual(0f, x, 0.0001f, "リセット後の最初の観測でそのまま初期化される");
    }

    [Test]
    public void 壊れた観測は無視して前の推定を返す()
    {
        var f = new SpatialKalmanFilter();
        f.Update(3f, 3f, 3f, Dt, out _, out _, out _);
        f.Update(float.NaN, float.PositiveInfinity, 3f, Dt, out float x, out float y, out _);

        Assert.AreEqual(3f, x, 0.0001f);
        Assert.AreEqual(3f, y, 0.0001f);
    }

    [Test]
    public void 不正なパラメータは既定値へ()
    {
        // 例外にせず動くこと(実機で初期化に失敗しても追従が止まらない)
        var f = new SpatialKalmanFilter(-1f, 0f, 5f);
        f.Update(1f, 1f, 1f, Dt, out float x, out _, out _);
        Assert.AreEqual(1f, x, 0.0001f);
    }

    // ════════════════════════════════════════════════════════════════════
    // フレームレート非依存 (2026-09-17 の時間単位化)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>一定速度で1秒進んだときの推定位置を、指定fpsで刻んで求める。</summary>
    private static float TrackConstantVelocity(float fps, float metersPerSecond, float seconds)
    {
        var f = new SpatialKalmanFilter();
        float dt = 1f / fps;
        int steps = (int)(seconds * fps);
        float x = 0f;
        for (int i = 0; i < steps; i++)
            f.Update((i + 1) * metersPerSecond * dt, 0f, 0f, dt, out x, out _, out _);
        return x;
    }

    /// <summary>
    /// 同じ物理運動なら刻み方(fps)が変わっても到達点は一致すること。
    /// <b>注意</b>: 定速の定常状態では旧フレーム単位実装でもほぼ一致する(実測差0.004m)。
    /// この項目は退行検知であって、時間単位化の効果を示すものではない —
    /// 効果は <see cref="長いフレームの直後に位置誤差が10の予算を食い潰さない"/> が示す。
    /// </summary>
    [Test]
    public void 同じ運動なら刻むfpsが変わっても推定はほぼ一致する()
    {
        float at60 = TrackConstantVelocity(60f, 3.6f, 1f);
        float at30 = TrackConstantVelocity(30f, 3.6f, 1f);
        float at120 = TrackConstantVelocity(120f, 3.6f, 1f);

        Assert.AreEqual(at60, at30, 0.10f, $"60fps={at60:F3} 30fps={at30:F3}");
        Assert.AreEqual(at60, at120, 0.10f, $"60fps={at60:F3} 120fps={at120:F3}");
        Assert.AreEqual(3.6f, at60, 0.30f, "1秒後は真値3.6m付近へ来ていること");
    }

    /// <summary>長いフレームでも予測が暴走しないこと(経過時間に上限を掛けている)。</summary>

    /// <summary>
    /// <b>時間単位化の核心</b>。1秒の定速走行のあとに330msの長いフレームが1回来たとき、
    /// 推定位置が真値からどれだけ外れるか。
    ///
    /// <para>旧実装(フレーム単位)は「直前の16msぶんの速度で1フレーム分だけ」予測するため、
    /// 実時間330msぶん進んだ観測に追いつけず <b>0.879m</b> ずれていた。
    /// §10 の位置誤差要求は <b>1.0m以内</b> なので、**長いフレーム1回で予算の88%を使い切る**。
    /// 時間単位化後は経過時間ぶんを予測し、プロセスノイズも経過時間でスケールするため
    /// ほぼ誤差ゼロで追随する。</para>
    ///
    /// <para>走行中の最大 Time.deltaTime は実測で328〜333ms(E2E計測)。机上の話ではない。</para>
    /// </summary>
    [Test]
    public void 長いフレームの直後に位置誤差が10の予算を食い潰さない()
    {
        const float speed = 3.6f;      // 目標ペース相当
        const float longFrame = 0.330f; // 実測された最大フレーム時間
        var f = new SpatialKalmanFilter();

        float t = 0f, x = 0f;
        for (int i = 0; i < 60; i++)   // 60fpsで1秒
        {
            t += Dt;
            f.Update(speed * t, 0f, 0f, Dt, out x, out _, out _);
        }

        t += longFrame;
        f.Update(speed * t, 0f, 0f, longFrame, out x, out _, out _);

        float truth = speed * t;
        float error = System.Math.Abs(x - truth);

        Assert.Less(error, 0.15f,
            $"長いフレーム後の位置誤差 {error:F3}m (旧フレーム単位実装では0.879m ≒ §10予算1.0mの88%)");
    }

    [Test]
    public void 長いフレームでも予測は上限内に収まる()
    {
        var f = new SpatialKalmanFilter();
        float dt = 1f / 60f;

        // まず一定速度で速度推定を育てる
        for (int i = 0; i < 120; i++)
            f.Update((i + 1) * 3.6f * dt, 0f, 0f, dt, out _, out _, out _);

        // 観測が止まったまま 2秒のフレームが来ても、1回の予測は上限(100ms)ぶんまで
        float before = 120 * 3.6f * dt;
        f.Update(before, 0f, 0f, 2.0f, out float after, out _, out _);

        // 外挿は0.5秒で頭打ち。そのぶん進んだ後、育った共分散ぶん観測側へ引き戻される
        Assert.Less(System.Math.Abs(after - before), 3.6f * SpatialKalmanFilter.MaxPredictionSeconds,
            $"before={before:F3} after={after:F3}");
    }

    [Test]
    public void 経過時間が取れない呼び出しは基準レート扱いになる()
    {
        var a = new SpatialKalmanFilter();
        var b = new SpatialKalmanFilter();
        float dt = 1f / SpatialKalmanFilter.ReferenceFrameRate;

        for (int i = 0; i < 30; i++)
        {
            a.Update(i * 0.06f, 0f, 0f, 0f, out _, out _, out _);   // dt=0
            b.Update(i * 0.06f, 0f, 0f, dt, out _, out _, out _);
        }
        a.Update(2f, 0f, 0f, 0f, out float ax, out _, out _);
        b.Update(2f, 0f, 0f, dt, out float bx, out _, out _);

        Assert.AreEqual(bx, ax, 1e-5f);
    }

    /// <summary>既定値が「旧フレーム単位の定数 × 60」であること(60fpsで挙動が変わらない担保)。</summary>
    [Test]
    public void 既定値は基準フレームレートで旧実装と同じ量になる()
    {
        Assert.AreEqual(0.05f, SpatialKalmanFilter.DefaultProcessNoise / SpatialKalmanFilter.ReferenceFrameRate, 1e-6f);
        Assert.AreEqual(0.12f, SpatialKalmanFilter.DefaultTrendWeight / SpatialKalmanFilter.ReferenceFrameRate, 1e-6f);
        Assert.AreEqual(0.80f, SpatialKalmanFilter.DefaultMeasurementNoise, 1e-6f);
    }
}
