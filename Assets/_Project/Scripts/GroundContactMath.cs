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

    // ════════════════════════════════════════════════════════════════════
    // 足のめり込み補正 (FootPlanting)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>1フレームで持ち上げる上限(m)。計測の異常値でアバターが宙へ飛ぶのを防ぐ。</summary>
    public const float MaxLiftMeters = 0.25f;

    /// <summary>足裏の面とみなす厚み(m)。安静姿勢で最下点からこの範囲の頂点を足裏とする。</summary>
    public const float SoleBandMeters = 0.015f;

    /// <summary>
    /// モデルを持ち上げる量(m)。最も低い足裏が床(=ルートの高さ)より下なら、その分だけ上げる。
    ///
    /// <para><b>上げるだけで、下げない</b>: 走行の遊脚期は両足が床から離れているのが正しい姿。
    /// 最下点を常に床へ吸着させると空中局面が消え、アバターが床を滑るように見える。</para>
    /// </summary>
    /// <param name="lowestSoleAboveRoot">補正前の最下足裏のルートからの高さ(m)。負ならめり込み。</param>
    public static float ComputeLift(float lowestSoleAboveRoot)
    {
        if (float.IsNaN(lowestSoleAboveRoot) || float.IsInfinity(lowestSoleAboveRoot)) return 0f;
        if (lowestSoleAboveRoot >= 0f) return 0f;
        return System.Math.Min(-lowestSoleAboveRoot, MaxLiftMeters);
    }

    /// <summary>
    /// 1本の骨(足/つま先)に属する頂点から、足裏を代表する点を選ぶ。
    ///
    /// <para>安静姿勢の最下点から <paramref name="band"/> 以内を足裏の面とし、その面の
    /// 最下点・前後端・左右端を返す。前後端があるので、踵接地(つま先が上がる)でも
    /// 蹴り出し(踵が上がる)でも、実際に床へ最も近い点が含まれる。</para>
    /// </summary>
    /// <param name="heights">各頂点の安静姿勢での高さ(m)。</param>
    /// <param name="forwards">各頂点の前後方向の座標(m)。</param>
    /// <param name="sides">各頂点の左右方向の座標(m)。</param>
    /// <returns>選んだ頂点のインデックス(重複なし・最大5点)。</returns>
    public static List<int> SelectSolePoints(
        IReadOnlyList<float> heights, IReadOnlyList<float> forwards, IReadOnlyList<float> sides, float band)
    {
        var result = new List<int>();
        if (heights == null || forwards == null || sides == null) return result;
        int n = System.Math.Min(heights.Count, System.Math.Min(forwards.Count, sides.Count));
        if (n == 0) return result;

        int lowest = 0;
        for (int i = 1; i < n; i++)
            if (heights[i] < heights[lowest]) lowest = i;
        float limit = heights[lowest] + band;

        int back = -1, front = -1, left = -1, right = -1;
        for (int i = 0; i < n; i++)
        {
            if (heights[i] > limit) continue;
            if (back  < 0 || forwards[i] < forwards[back])  back  = i;
            if (front < 0 || forwards[i] > forwards[front]) front = i;
            if (left  < 0 || sides[i]    < sides[left])     left  = i;
            if (right < 0 || sides[i]    > sides[right])    right = i;
        }

        foreach (int i in new[] { lowest, back, front, left, right })
            if (i >= 0 && !result.Contains(i)) result.Add(i);
        return result;
    }
}
