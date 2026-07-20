/// <summary>
/// ペーシング・オーラエフェクトの発動判定 (基本設計書 §7.2)。Unity非依存の純ロジック。
/// 目標リードからの超過遅延(deviation)が activation 以上でオーラを放射し、
/// full に達するまで強度(=光ラインの密度・流速)を 0→1 で高める。
/// </summary>
public static class AuraFeedback
{
    /// <param name="deviationMeters">目標リードからの超過遅延(m)。正=遅れている</param>
    /// <param name="activationMeters">発動閾値(m。設計書=5.0)</param>
    /// <param name="fullIntensityMeters">強度が最大に達する遅延(m)</param>
    /// <param name="intensity">発動時の強度[0,1]。非発動時は0</param>
    /// <returns>オーラを放射すべきか</returns>
    public static bool TryEvaluate(float deviationMeters, float activationMeters,
        float fullIntensityMeters, out float intensity)
    {
        if (deviationMeters < activationMeters)
        {
            intensity = 0f;
            return false;
        }

        float span = fullIntensityMeters - activationMeters;
        intensity = span <= 0.0001f
            ? 1f
            : Clamp01((deviationMeters - activationMeters) / span);
        return true;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
