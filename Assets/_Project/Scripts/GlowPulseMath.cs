using System;

/// <summary>
/// バイオルミネッセンス(AGENTS.md §4.4・第1期スコープ外): 心拍に同期した発光の強さ。
/// <c>intensity = base + sin(t × (HR/60)·π) × amplitude</c>。Unity非依存の純ロジック。
///
/// <para><b>以前の不具合</b>(2点):</para>
/// <para>① 既定値 base=1.0 / amplitude=1.5 では周期の約27%で強さが<b>負</b>になり、
/// 発光色(§7.1 のペースシンクロ色)が負の値でシェーダーへ渡っていた。0で下限を切る。</para>
/// <para>② 心拍が一度も届いていなくても「60bpm」の仮値で脈動していた。第1期は心拍センサーを
/// 既定OFFにしているので、実機ではほぼ常に<b>計測していない心拍で</b>光っていたことになる。
/// 心拍が無い間は脈動させず一定の強さで光らせる。</para>
/// </summary>
public static class GlowPulseMath
{
    public static float Intensity(float baseIntensity, float amplitude,
                                  bool hasHeartRate, int heartRateBpm, float timeSeconds)
    {
        if (!hasHeartRate || heartRateBpm <= 0)
            return Math.Max(0f, baseIntensity);

        double phase = timeSeconds * (heartRateBpm / 60.0) * Math.PI;
        float intensity = baseIntensity + (float)Math.Sin(phase) * amplitude;
        return Math.Max(0f, intensity);
    }
}
