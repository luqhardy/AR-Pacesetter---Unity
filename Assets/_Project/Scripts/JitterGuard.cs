/// <summary>
/// §3 Jitter Tolerance: フレーム間隔の変動が ±5ms を超えたフレームは生の測定値を捨て、
/// カルマンフィルタの予測を優先する。Unity非依存の純ロジック。<see cref="AvatarEngine"/> が委譲する。
///
/// <para><b>以前の不具合</b>: 変動を「直前フレームとの差」で測り、予測の継続に上限が無かった。
/// 発熱や負荷でフレームが 16.7ms / 33.3ms と交互になると<b>毎フレームが変動扱い</b>になり、
/// アバターは測定を一度も取り込まず最後の速度で直進し続けた(コーナーで接線から外れる — F-04)。
/// 単発のヒッチでも「跳ねたフレーム」と「戻ったフレーム」の2回を変動と数えていた。</para>
///
/// <para>ここでは基準を<b>フレーム時間の移動平均</b>に置き(単発ヒッチは1回だけ)、予測だけで進める
/// 時間に上限を設ける(上限に達したら次のフレームは測定を取り込む)。</para>
/// </summary>
public sealed class JitterGuard
{
    /// <summary>§3 の許容変動(秒)。</summary>
    public const float ToleranceSeconds = 0.005f;

    /// <summary>測定を取り込まずに予測だけで進めてよい累計時間(秒)。60fpsで約6フレーム。</summary>
    public const float MaxPredictionSeconds = 0.1f;

    /// <summary>基準フレーム時間(移動平均)の1フレームあたりの追従率。</summary>
    public const float BaselineSmoothing = 0.1f;

    private float _baseline;
    private bool _hasBaseline;
    private float _predictedFor;

    /// <summary>基準のフレーム時間(秒)。未確立なら0。</summary>
    public float BaselineSeconds => _hasBaseline ? _baseline : 0f;

    /// <summary>
    /// このフレームで測定を捨てて予測を使うべきか。毎フレーム1回、実経過時間を渡して呼ぶ。
    /// </summary>
    public bool ShouldPredict(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return false;

        if (!_hasBaseline)
        {
            _baseline = deltaSeconds;
            _hasBaseline = true;
            return false;
        }

        float drift = deltaSeconds - _baseline;
        bool spike = drift > ToleranceSeconds || drift < -ToleranceSeconds;
        _baseline += drift * BaselineSmoothing;

        if (spike && _predictedFor < MaxPredictionSeconds)
        {
            _predictedFor += deltaSeconds;
            return true;
        }

        _predictedFor = 0f; // 測定を取り込んだので予測の累計を戻す
        return false;
    }

    public void Reset()
    {
        _baseline = 0f;
        _hasBaseline = false;
        _predictedFor = 0f;
    }
}
