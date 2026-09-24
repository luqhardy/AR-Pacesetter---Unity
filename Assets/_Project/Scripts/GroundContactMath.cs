using System.Collections.Generic;

/// <summary>
/// 接地誤差 (基本設計書 §10: 上下5cm以内) の判定。Unity非依存の純ロジック。
///
/// <para><b>1フレームで測ってはいけない理由</b>: 走っているアバターの足裏は歩幅の位相で上下する。
/// 遊脚期は両足とも床から離れているので、ある瞬間の「足裏 − 床」は接地の良し悪しではなく
/// <b>計測した瞬間が歩幅のどこに当たったか</b>を測ってしまう(実測 -0.036m 〜 +0.064m)。
/// シナリオの前段にフレームを足すだけで合否が変わる。</para>
///
/// <para>接地の品質は「足が床に着いた瞬間に、足裏が床の高さにあるか」なので、
/// 少なくとも1歩幅ぶんの時間サンプルを取り、<b>最も低い足裏</b>(=立脚期の足)で判定する。
/// 浮いていれば最小値も正、沈んでいれば負になるので、どちらの不具合も検出できる。</para>
/// </summary>
public static class GroundContactMath
{
    /// <summary>§10 の許容誤差(m)。</summary>
    public const float ToleranceMeters = 0.05f;

    /// <summary>
    /// サンプルを取る時間(秒、シナリオ時間)。Y Bot の歩行・走行サイクルはどちらも1.2秒未満なので、
    /// この窓には必ず立脚期が1回以上含まれる。
    /// </summary>
    public const float StrideWindowSeconds = 1.2f;

    /// <summary>
    /// 窓内の「足裏 − 床」(m) のサンプルから接地誤差を求める。立脚期の値 = 最小値。
    /// </summary>
    /// <returns>接地誤差(m)。サンプルが無ければ false。</returns>
    public static bool TryContactError(IReadOnlyList<float> soleAboveFloorSamples, out float error)
    {
        error = 0f;
        if (soleAboveFloorSamples == null || soleAboveFloorSamples.Count == 0) return false;

        bool any = false;
        foreach (float s in soleAboveFloorSamples)
        {
            if (float.IsNaN(s) || float.IsInfinity(s)) continue;
            if (!any || s < error) error = s;
            any = true;
        }
        return any;
    }

    /// <summary>接地誤差が §10 の許容内か。</summary>
    public static bool IsWithinTolerance(float error) => System.Math.Abs(error) < ToleranceMeters;
}
