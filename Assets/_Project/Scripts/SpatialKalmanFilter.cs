/// <summary>
/// 3軸独立のスカラーカルマンフィルタ (AGENTS.md §3 / 要件定義 6.1)。Unity非依存の純ロジック。
///
/// <para>以前は <c>Assets/Plugins/iOS/KalmanFilterNative.mm</c> の C++ 実装を
/// iOS実機でだけ使い、エディタは素通し(<c>return raw</c>)だった。つまり
/// **実機だけが通る経路が、ユニットテストにもE2Eにも一度も掛かっていなかった**。
/// 同じアルゴリズムをC#へ移して全プラットフォームで使うことで、
/// エディタの検証がそのまま実機の挙動を縛る。</para>
///
/// <para>アルゴリズム(各軸): 予測 <c>x' = x + v</c>、共分散 <c>p += Q</c>、
/// ゲイン <c>g = p/(p+R)</c>、更新 <c>x = x' + g(z − x')</c>、<c>p *= (1−g)</c>、
/// 速度 <c>v += w((x − x_prev) − v)</c>。速度項はフレーム単位。</para>
/// </summary>
public class SpatialKalmanFilter
{
    /// <summary>既定のプロセスノイズ Q。</summary>
    public const float DefaultProcessNoise = 0.05f;
    /// <summary>既定の観測ノイズ R。</summary>
    public const float DefaultMeasurementNoise = 0.80f;
    /// <summary>既定の速度追従係数(0〜1)。</summary>
    public const float DefaultTrendWeight = 0.12f;

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
        _w = (trendWeight >= 0f && trendWeight <= 1f) ? trendWeight : DefaultTrendWeight;
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

    /// <summary>観測(x,y,z)を取り込み、平滑化された推定を返す。</summary>
    public void Update(float rawX, float rawY, float rawZ,
                       out float smoothX, out float smoothY, out float smoothZ)
    {
        smoothX = Step(ref _ax, rawX);
        smoothY = Step(ref _ay, rawY);
        smoothZ = Step(ref _az, rawZ);
    }

    private float Step(ref Axis s, float z)
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

        float predicted = s.X + s.V;
        s.P += _q;

        float gain = s.P / (s.P + _r);
        s.X = predicted + gain * (z - predicted);
        s.P *= (1f - gain);

        s.V += _w * ((s.X - previous) - s.V);
        return s.X;
    }
}
