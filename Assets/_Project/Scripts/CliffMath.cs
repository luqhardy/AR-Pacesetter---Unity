using System.Collections.Generic;

/// <summary>
/// 断崖判定 (基本設計書 §4.2 / F-05) の純ロジック。Unity非依存。
///
/// <para>断崖は「ユーザー真下の地面」と「進行方向3m先の地面」の<b>落差</b>で判定する。
/// その2点の「地面」をレイキャストのヒット候補から選ぶのがこのクラスの仕事。</para>
///
/// <para><b>なぜ「最初のヒット」や「最も高いヒット」ではだめか</b>:
/// ARKitは天井も床と同じ「水平・法線上向き」の平面としてコライダー付きで返す。
/// 頭上から真下へ撃つレイは天井を<b>先に</b>貫くので、最初のヒットを地面にすると
/// 「ユーザーの地面 = 天井高」になる。3m先の天井がまだ未検出なら、そこでは床が
/// 選ばれ、「天井 − 床 ≒ 2m 以上」が断崖として成立してしまう。アバターは足踏み停止し、
/// ユーザーが追い越して視界から消える(=「壁・天井があるとアバターが消える」の一因)。</para>
///
/// <para>地面は必ず<b>上向きの面</b>で、かつ<b>カメラより下</b>にある — この幾何的事実だけを
/// 制約として課す(<see cref="GroundFloorTracker.IsPlausibleFloorCandidate"/> と同じ考え方)。
/// 上限は設けない: 3m先が本物の断崖なら地面はいくらでも深くてよい。</para>
/// </summary>
public static class CliffMath
{
    /// <summary>これ以上「上向き」の面のみ地面として採用する(cos45°≒0.7)。壁・天井の縁を弾く。</summary>
    public const float GroundNormalMinDot = 0.7f;

    /// <summary>レイキャストのヒット1件。Unityの RaycastHit から必要な2値だけを写す。</summary>
    public readonly struct GroundCandidate
    {
        /// <summary>ヒット点のワールドY。</summary>
        public readonly float Y;
        /// <summary>面法線と上方向の内積(1=真上向き, 0=垂直面, -1=下向き)。</summary>
        public readonly float NormalUpDot;

        public GroundCandidate(float y, float normalUpDot)
        {
            Y = y;
            NormalUpDot = normalUpDot;
        }
    }

    /// <summary>
    /// ヒット候補から「地面」を選ぶ: 上向きの面で、カメラより <paramref name="minCameraToGroundMeters"/> 以上
    /// 下にあるもののうち<b>最も高い</b>面。天井(カメラより上)・壁(垂直)・机(カメラに近すぎる)は候補にならない。
    /// </summary>
    /// <param name="candidates">真下へのレイキャストの全ヒット(順不同でよい)</param>
    /// <param name="cameraY">カメラ(端末)のワールドY</param>
    /// <param name="minCameraToGroundMeters">地面はカメラから最低これだけ下にあること(m)</param>
    /// <param name="normalMinDot">地面とみなす法線・上方向の内積の下限</param>
    /// <param name="groundY">選ばれた地面のワールドY</param>
    /// <returns>地面が見つかったか</returns>
    public static bool TrySelectGround(IReadOnlyList<GroundCandidate> candidates, float cameraY,
                                       float minCameraToGroundMeters, float normalMinDot,
                                       out float groundY)
    {
        groundY = 0f;
        if (candidates == null || !IsUsable(cameraY) || !IsUsable(minCameraToGroundMeters))
            return false;

        bool found = false;
        float highestAllowedY = cameraY - minCameraToGroundMeters; // 地面はこれより下

        for (int i = 0; i < candidates.Count; i++)
        {
            GroundCandidate c = candidates[i];
            if (!IsUsable(c.Y) || !IsUsable(c.NormalUpDot)) continue;
            if (c.NormalUpDot < normalMinDot) continue;   // 壁・縁
            if (c.Y > highestAllowedY) continue;          // 天井・机・頭上のもの

            if (!found || c.Y > groundY)
            {
                groundY = c.Y;
                found = true;
            }
        }

        return found;
    }

    /// <summary>既定の法線しきい値での判定。</summary>
    public static bool TrySelectGround(IReadOnlyList<GroundCandidate> candidates, float cameraY,
                                       float minCameraToGroundMeters, out float groundY)
        => TrySelectGround(candidates, cameraY, minCameraToGroundMeters, GroundNormalMinDot, out groundY);

    /// <summary>
    /// 進行方向の地面がユーザーの地面より <paramref name="minDropMeters"/> 以上低ければ断崖。
    /// 「前方に地面が見つからない」は断崖の証拠にならない(平面検出はまばら)ので、
    /// 呼び出し側は両方の地面が<b>実測できた時だけ</b>これを呼ぶこと。
    /// </summary>
    public static bool IsCliffDrop(float userGroundY, float aheadGroundY, float minDropMeters)
    {
        if (!IsUsable(userGroundY) || !IsUsable(aheadGroundY) || !IsUsable(minDropMeters))
            return false;
        return userGroundY - aheadGroundY >= minDropMeters;
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
