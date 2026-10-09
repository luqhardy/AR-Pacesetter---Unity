using NUnit.Framework;

/// <summary>
/// 追従の定常遅れ補正の検証 (§10 位置誤差1.0m / F-03 3.0m前方維持)。
///
/// ここで守るのは「指数平滑は動く目標を v/k だけ取り残す」という事実そのもの。
/// 実測でも E2E で 3.6m/s・k=2.5 のとき平均1.51mのずれが出ており、
/// 理論値 1.44m と一致していた。
/// </summary>
[TestFixture]
public class TrackingLagMathTests
{
    [Test]
    public void 定常遅れは速度を平滑係数で割った値()
    {
        // k=2.5 で 3.6m/s(≒4分37秒/km) → 1.44m。E2Eで観測された1.51mとほぼ一致
        Assert.AreEqual(1.44f, TrackingLagMath.SteadyStateLagMeters(3.6f, 2.5f), 0.0001f);
        // 追い抜き時は k=4.0 に上がるので遅れは小さくなる
        Assert.AreEqual(0.9f, TrackingLagMath.SteadyStateLagMeters(3.6f, 4.0f), 0.0001f);
    }

    [Test]
    public void 止まっていれば遅れは無い()
    {
        Assert.AreEqual(0f, TrackingLagMath.SteadyStateLagMeters(0f, 2.5f), 0.0001f);
    }

    [TestCase(0f)]     // 平滑が無効
    [TestCase(-1f)]    // 不正値
    public void 平滑係数が正でなければ補正しない(float lerpSpeed)
    {
        Assert.AreEqual(0f, TrackingLagMath.SteadyStateLagMeters(3.6f, lerpSpeed), 0.0001f);
        Assert.AreEqual(0f, TrackingLagMath.FeedForwardMeters(3.6f, lerpSpeed), 0.0001f);
    }

    [Test]
    public void 後退や不正な速度では補正しない()
    {
        Assert.AreEqual(0f, TrackingLagMath.SteadyStateLagMeters(-2f, 2.5f), 0.0001f);
        Assert.AreEqual(0f, TrackingLagMath.SteadyStateLagMeters(float.NaN, 2.5f), 0.0001f);
        Assert.AreEqual(0f, TrackingLagMath.SteadyStateLagMeters(3.6f, float.NaN), 0.0001f);
    }

    [Test]
    public void 先回り量は上限で頭打ちになる()
    {
        // 速度スパイク(テレポート等)で飛びすぎないための安全弁
        Assert.AreEqual(3.0f, TrackingLagMath.FeedForwardMeters(100f, 2.5f, 3.0f), 0.0001f);
        Assert.AreEqual(1.44f, TrackingLagMath.FeedForwardMeters(3.6f, 2.5f, 3.0f), 0.0001f);
    }

    [Test]
    public void 上限が不正なら補正しない()
    {
        Assert.AreEqual(0f, TrackingLagMath.FeedForwardMeters(3.6f, 2.5f, 0f), 0.0001f);
        Assert.AreEqual(0f, TrackingLagMath.FeedForwardMeters(3.6f, 2.5f, -1f), 0.0001f);
        Assert.AreEqual(0f, TrackingLagMath.FeedForwardMeters(3.6f, 2.5f, float.NaN), 0.0001f);
    }

    /// <summary>
    /// 補正の本質を数値シミュレーションで確かめる。
    /// 同じ平滑器に「補正なし」「補正あり」で動く目標を追わせ、定常状態のずれを比べる。
    ///
    /// <para>離散時間では定常遅れは厳密には <c>(v/k)·(1 − dt·k)</c> になる
    /// (目標を進めてから平滑するため、1ステップぶんだけ有利になる)。
    /// 60fps・k=2.5 なら v/k の約96%。連続時間の近似 v/k で補正すると、
    /// 残差は <c>v·dt</c> = <b>1フレームぶんの移動量</b>(3.6m/s なら6cm)まで縮む。
    /// これは予測を入れない限り消せない下限で、§10の1.0mに対して十分小さい。</para>
    /// </summary>
    [Test]
    public void 補正を入れると定常状態のずれがフレーム1枚ぶんまで縮む()
    {
        const float k = 2.5f;
        const float v = 3.6f;
        const float dt = 1f / 60f;

        float targetPos = 0f;
        float plain = 0f;       // 補正なし
        float compensated = 0f; // 補正あり

        for (int i = 0; i < 600; i++) // 10秒ぶん (時定数1/k=0.4秒なので十分収束する)
        {
            targetPos += v * dt;

            plain += (targetPos - plain) * dt * k;

            float aim = targetPos + TrackingLagMath.FeedForwardMeters(v, k);
            compensated += (aim - compensated) * dt * k;
        }

        float plainError = targetPos - plain;
        float compensatedError = System.Math.Abs(targetPos - compensated);

        float discreteLag = (v / k) * (1f - dt * k);
        Assert.AreEqual(discreteLag, plainError, 0.01f,
            "補正なしでは v/k(離散補正込み)だけ取り残される");
        Assert.Greater(plainError, 1.3f,
            "§10の許容1.0mを構造的に超えていることの確認");

        Assert.AreEqual(v * dt, compensatedError, 0.02f,
            "補正後の残差は1フレームぶんの移動量まで縮む");
        Assert.Less(compensatedError, plainError / 10f,
            "補正で誤差が1桁以上小さくなる");
        Assert.Less(compensatedError, PositionAccuracyStats.ToleranceMeters,
            "§10の位置誤差1.0m以内に収まる");
    }
}
