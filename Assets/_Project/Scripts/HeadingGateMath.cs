/// <summary>
/// 進行方向の推定に「どの観測を信じるか」の判断 (F-03 / F-08)。Unity非依存の純ロジック。
///
/// <para><b>背景</b>: 実機(iPhone 15 Pro Max)で「アバターが不規則に飛び回る」。
/// アバターは常に ユーザー位置 + 進行方向 × 3.0m に置かれるので、**進行方向がふらつくと
/// アバターはユーザーの周りを3mの半径で振り回される**。エディタには手ブレも測位ノイズも
/// 無いので、この経路は実機でしか現れなかった。</para>
///
/// <para>ふらつきの元は2つ:</para>
/// <list type="number">
/// <item><b>GPSの方位</b>: 連続する2つの測位の差から方位を出すが、立ち止まっていても
///   精度8mの測位は1秒ごとに数m「動く」。0.75mの最小区間はそれを素通しし、
///   無作為な方位が65%の重みで混ざる。方位が意味を持つのは**移動量が精度より十分大きい**時だけ。</item>
/// <item><b>ARの移動</b>: 1.5秒で2cm動けば「移動方向あり」としていたが、手ブレは2cmを軽く超える。
///   立ち止まって手が揺れるだけで方位が毎フレーム変わる。</item>
/// </list>
/// </summary>
public static class HeadingGateMath
{
    /// <summary>
    /// GPS区間を方位として採用するのに必要な、区間長 ÷ 精度 の下限。
    /// 2倍 = 「区間の両端の誤差円が重ならない」程度。これ未満は向きが定まらない。
    /// </summary>
    public const float MinSegmentToAccuracyRatio = 2.0f;

    /// <summary>
    /// ARの移動から方位を出すのに必要な、窓内の積算移動量(m)。
    /// 手ブレ(数cm)と体の揺れ(10〜20cm)を弾き、歩き出し(1.5秒で0.5m ≒ 0.33m/s)から拾う。
    /// </summary>
    public const float MinArMotionMeters = 0.5f;

    /// <summary>
    /// 連続する測位の区間を方位に使ってよいか。
    /// </summary>
    /// <param name="segmentMeters">2つの測位の水平距離(m)</param>
    /// <param name="accuracyMeters">測位の水平精度(m)。負値は無効</param>
    /// <param name="minSegmentMeters">従来からある固定の最小区間(m)</param>
    public static bool IsGpsSegmentUsableForHeading(double segmentMeters, float accuracyMeters,
                                                    float minSegmentMeters)
    {
        if (double.IsNaN(segmentMeters) || double.IsInfinity(segmentMeters)) return false;
        if (float.IsNaN(accuracyMeters) || float.IsInfinity(accuracyMeters) || accuracyMeters < 0f) return false;
        if (segmentMeters < minSegmentMeters) return false;

        // 精度が悪いほど長い区間が要る。精度0(理想)なら固定の最小区間だけで足りる
        return segmentMeters >= accuracyMeters * MinSegmentToAccuracyRatio;
    }

    /// <summary>ARの積算移動量から方位を更新してよいか。</summary>
    public static bool IsArMotionUsableForHeading(float integratedMotionMeters)
    {
        if (float.IsNaN(integratedMotionMeters) || float.IsInfinity(integratedMotionMeters)) return false;
        return integratedMotionMeters >= MinArMotionMeters;
    }

    /// <summary>
    /// AR↔GPS の向きの対応(ヨー差)を初期化してよいか。
    /// ARの移動が無い状態で初期化すると「カメラの向き vs 無作為な方位」で
    /// 対応が決まり、以降のGPS方位が全部ずれる。移動で決める以外に信頼できる手段は無い。
    /// </summary>
    public static bool CanSeedWorldAlignment(float arDeltaMeters, float minArAlignmentMeters)
    {
        if (float.IsNaN(arDeltaMeters) || float.IsInfinity(arDeltaMeters)) return false;
        return arDeltaMeters >= minArAlignmentMeters;
    }
}
