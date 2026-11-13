using System;

/// <summary>足音と呼吸の音量の倍率(0〜1)</summary>
public readonly struct PacesetterSoundMix
{
    public readonly float Footstep;
    public readonly float Breath;

    public PacesetterSoundMix(float footstep, float breath)
    {
        Footstep = footstep;
        Breath = breath;
    }
}

/// <summary>
/// ペースセッターの音(Docs/PACESETTER_BEHAVIOR_SPEC.md §6)。依存ゼロの純ロジック。
/// 音の方向は AudioSource がアバターの位置にあることで決まる(R-32)ので、ここでは距離ごとの大きさだけを決める。
/// 曲線と消える距離は U-04 で未決 — 下の値はすべて<b>仮</b>で、実機で調整する。
/// </summary>
public static class PacesetterSound
{
    /// <summary>U-04(仮): 追い抜いたあと、これだけ離れると聞こえなくなる(AudioSource の到達距離に揃える)</summary>
    public const float SilentBehindMeters = 15f;
    /// <summary>仮: すぐ前のときの呼吸(仕様「控えめ」)</summary>
    public const float NearBreath = 0.35f;
    /// <summary>仮: 10m 手前の足音</summary>
    public const float FarFootstep = 0.25f;
    /// <summary>仮: 10m 手前の呼吸</summary>
    public const float FarBreath = 0.1f;
    /// <summary>U-05(仮): 待っている様子の音。種類が決まるまで静かな呼吸を置く</summary>
    public const float WaitingBreath = 0.25f;

    /// <summary>R-33: 足音のテンポ(歩/秒)。歩き約1.8・走り約3.0</summary>
    public static float StepsPerSecond(float speedMetersPerSecond)
    {
        float speed = float.IsNaN(speedMetersPerSecond) ? 0f : speedMetersPerSecond;
        return Math.Max(1.6f, Math.Min(3.2f, 1.4f + speed * 0.35f));
    }

    public static PacesetterSoundMix Evaluate(PacesetterState state, float gap)
    {
        switch (state)
        {
            case PacesetterState.Start:
                return new PacesetterSoundMix(0f, 0f);

            case PacesetterState.Wait:
                return new PacesetterSoundMix(0f, WaitingBreath);

            case PacesetterState.Overtaken:
                // 後方からはっきり聞こえ、離れると小さくなり、やがて消える
                float behind = Clamp01(1f - Math.Abs(gap) / SilentBehindMeters);
                return new PacesetterSoundMix(behind, behind);

            default:
                // 0〜3m ははっきり、3〜10m は連続して小さく遠く
                float t = Clamp01((gap - (float)PacesetterModel.WaitExitMeters)
                                  / (float)(PacesetterModel.WaitEnterMeters - PacesetterModel.WaitExitMeters));
                return new PacesetterSoundMix(Lerp(1f, FarFootstep, t), Lerp(NearBreath, FarBreath, t));
        }
    }

    private static float Clamp01(float value) => float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
