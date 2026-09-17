/// <summary>
/// 3軸独立のスカラーカルマンフィルタ (AGENTS.md §3 / 要件定義 6.1)。Unity非依存の純ロジック。
///
/// <para>以前は <c>Assets/Plugins/iOS/KalmanFilterNative.mm</c> の C++ 実装を
/// iOS実機でだけ使い、エディタは素通し(<c>return raw</c>)だった。つまり
/// **実機だけが通る経路が、ユニットテストにもE2Eにも一度も掛かっていなかった**。
/// 同じアルゴリズムをC#へ移して全プラットフォームで使うことで、
/// エディタの検証がそのまま実機の挙動を縛る。</para>
///
/// <para>アルゴリズム(各軸): 予測 <c>x' = x + v·dt</c>(dtは0.5秒で頭打ち)、共分散 <c>p += Q·dt</c>、
/// ゲイン <c>g = p/(p+R)</c>、更新 <c>x = x' + g(z − x')</c>、<c>p *= (1−g)</c>、
/// 速度 <c>v += (w·dt)((x − x_prev)/dt − v)</c>。</para>
///
/// <para><b>2026-09-17: フレーム単位から時間単位へ</b>。以前は <c>dt</c> がどこにも無く、
/// 予測もプロセスノイズも速度追従も<b>フレーム数</b>の関数だった。つまり30fpsと60fpsで
/// 平滑化の強さも予測量も変わり、長いフレームの直後は「直前の16msの速度で1フレーム分だけ」
/// 予測する = 実時間に対して大幅に予測不足になっていた。AGENTS.md §3 は
/// 「ジッタが±5msを超えたら予測補間を優先する」とこのフィルタに求めているが、
/// フレーム数基準のフィルタはその役には立たない。</para>
///
/// <para><b>既定値は 60fps で従来と完全に同一の挙動になるよう換算してある</b>
/// (Q・W を60倍し、速度を「毎秒」単位へ)。<c>dt = 1/60</c> を渡せば旧実装と数値まで一致する。</para>
/// </summary>
public class SpatialKalmanFilter
{
    /// <summary>換算の基準フレームレート。この値で旧実装(フレーム単位)と一致する。</summary>
    public const float ReferenceFrameRate = 60f;

    /// <summary>
    /// 予測(デッドレコニング)に使う経過時間の上限(秒)。
    ///
    /// <para>予測とプロセスノイズは<b>実際の経過時間</b>でスケールしなければならない —
    /// 330msのフレームではユーザーは本当に330msぶん進んでいる。ここを平滑化用の上限
    /// (100ms)で丸めると、逆に「予測不足」で0.5mの遅れを生む(実測)。
    /// 一方で長時間の中断(バックグラウンド復帰など)まで外挿しても意味が無いので、
    /// 0.5秒で頭打ちにする。それを超える間隔では共分散が育ってゲインが上がり、
    /// 観測側へ素直に寄る — これは正しい振る舞い。</para>
    /// </summary>
    public const float MaxPredictionSeconds = 0.5f;

    /// <summary>既定のプロセスノイズ Q(**毎秒**)。旧フレーム単位 0.05 × 60。</summary>
    public const float DefaultProcessNoise = 3.0f;
    /// <summary>既定の観測ノイズ R(時間に依存しない)。</summary>
    public const float DefaultMeasurementNoise = 0.80f;
    /// <summary>既定の速度追従レート(**毎秒**)。旧フレーム単位 0.12 × 60。</summary>
    public const float DefaultTrendWeight = 7.2f;

    private struct Axis
    {
        public float X;      // 状態推定
        public float P;      // 誤差共分散
        public float V;      // 速度(線形トレンド)推定
        public bool Seeded;
    }

    private readonly float _q, _r, _w;
    private Axis _ax, _ay, _az;

    public SpatialKalmanFilter()
        : this(DefaultProcessNoise, DefaultMeasurementNoise, DefaultTrendWeight) { }

    public SpatialKalmanFilter(float processNoise, float measurementNoise, float trendWeight)
    {
        _q = processNoise > 0f ? processNoise : DefaultProcessNoise;
        _r = measurementNoise > 0f ? measurementNoise : DefaultMeasurementNoise;
        _w = trendWeight >= 0f ? trendWeight : DefaultTrendWeight;
        Reset();
    }

    /// <summary>初回観測で初期化されたか。</summary>
    public bool IsSeeded => _ax.Seeded;

    /// <summary>
    /// 状態を破棄する。位置を外部が飛ばした直後(再同期・セッションリセット)に呼ぶ —
    /// 呼ばないと古い推定から新しい位置へ「引きずる」動きが出る。
    /// </summary>
    public void Reset()
    {
        _ax = default; _ay = default; _az = default;
        _ax.P = _ay.P = _az.P = 1f;
    }

    /// <summary>
    /// 観測(x,y,z)を取り込み、平滑化された推定を返す。
    /// <paramref name="deltaSeconds"/> は前回更新からの経過時間(秒)。
    /// 長いフレームで予測が暴れないよう <see cref="FrameSmoothing"/> の上限を掛ける。
    /// </summary>
    public void Update(float rawX, float rawY, float rawZ, float deltaSeconds,
                       out float smoothX, out float smoothY, out float smoothZ)
    {
        float dt = deltaSeconds > 0f ? deltaSeconds : 1f / ReferenceFrameRate;

        smoothX = Step(ref _ax, rawX, dt);
        smoothY = Step(ref _ay, rawY, dt);
        smoothZ = Step(ref _az, rawZ, dt);
    }

    private float Step(ref Axis s, float z, float dt)
    {
        if (float.IsNaN(z) || float.IsInfinity(z))
            return s.Seeded ? s.X : 0f; // 壊れた観測は無視して前の推定を返す

        if (!s.Seeded)
        {
            // 初回は観測値で初期化(起動直後の大きな引き込みを防ぐ)
            s.X = z; s.P = 1f; s.V = 0f; s.Seeded = true;
            return s.X;
        }

        float previous = s.X;

        // 予測とプロセスノイズは**実際の経過時間**に比例する(フレーム数ではない)。
        // 外挿しすぎないよう上限だけ掛ける
        float predictDt = dt < MaxPredictionSeconds ? dt : MaxPredictionSeconds;
        float predicted = s.X + s.V * predictDt;
        s.P += _q * predictDt;

        float gain = s.P / (s.P + _r);
        s.X = predicted + gain * (z - predicted);
        s.P *= (1f - gain);

        // 速度は「毎秒」で保持する。瞬間速度は実経過時間で割り、追従率だけ
        // FrameSmoothing の上限(100ms)を通して1を超えさせない
        float instantVelocity = (s.X - previous) / dt;
        s.V += FrameSmoothing.Factor(dt, _w) * (instantVelocity - s.V);
        return s.X;
    }
}
