/// <summary>
/// 走行開始時にアバターをどの方向へ置くかの判断 (F-03 / F-08 視線固定との両立)。
/// Unity非依存の純ロジック。
///
/// <para><b>問題</b>: 走行中はカメラ正面を進行方向に使わない(視線固定・F-08 — 首を振るたびに
/// アバターが振り回されるのを防ぐ)。だが<b>開始の瞬間</b>だけは話が別で、走者は
/// これから走る方向を向いて立っている。ここで「移動履歴があるから」と過去の推定を
/// 優先すると、待機中の手ブレ(1.5秒で2cmもあれば履歴が立つ)で決まった
/// ほぼランダムな方向の3m先にアバターが現れ、立ち止まっている限りそこに留まる —
/// 実機で「開始したのにアバターが見えない」の一因。</para>
///
/// <para><b>判断</b>: 開始直前の短い窓での<b>実移動量</b>で決める。ほぼ立ち止まっていれば
/// 視線方向へ置き、既に走り出していれば移動方向を信じる。手ブレは移動量としては
/// 小さいので、しきい値をメートル単位に置けば自然に弾ける。</para>
/// </summary>
public static class StartHeadingPolicy
{
    /// <summary>判断に使う直前の窓(秒)。F-08 の移動平均と同じ長さ。</summary>
    public const float WindowSeconds = 1.5f;

    /// <summary>
    /// 窓内の実移動がこれ未満なら「立ち止まっている」とみなし、視線方向へ置く(m)。
    /// 1.0m/1.5s ≒ 0.67m/s = ゆっくり歩く速さの下。手ブレ(数cm)は遥かに下回る。
    /// </summary>
    public const float StandingStillMeters = 1.0f;

    /// <summary>
    /// 開始時にカメラ正面へアバターを置くべきか。
    /// </summary>
    /// <param name="recentDisplacementMeters">直前 <see cref="WindowSeconds"/> 秒の水平移動量(m)</param>
    public static bool ShouldAlignToView(float recentDisplacementMeters)
    {
        if (float.IsNaN(recentDisplacementMeters) || float.IsInfinity(recentDisplacementMeters))
            return true; // 測れていないなら視線方向(唯一確かな情報)へ
        return recentDisplacementMeters < StandingStillMeters;
    }
}
