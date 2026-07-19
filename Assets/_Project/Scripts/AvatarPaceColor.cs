/// <summary>
/// ペースシンクロ・カラーエフェクトの判定 (基本設計書 §7.1)。Unity非依存の純ロジック。
/// アバターの符号付きリード距離(signedLead: 進行方向にアバターが前方へどれだけ
/// 離れているか。負=ユーザーが追い抜いた)から3状態を判定する:
///   - ジャスト(目標±justTol以内): 緑
///   - 遅延(アバターが目標より前方へ離れ): 橙→赤 グラデ(t=0橙, t=1赤)
///   - 超過(ユーザーが追い抜き): 緑→青 グラデ(t=0緑側, t=1青=signedLead≤0)
/// 色そのものはMonoBehaviour側でState+tから合成する(Unity非依存を保つため)。
/// </summary>
public static class AvatarPaceColor
{
    public enum PaceState { OverPace, Just, Behind }

    /// <param name="signedLead">進行方向へのアバター先行距離(m・符号付き)</param>
    /// <param name="targetLead">目標リード距離(m。設計書=3.0)</param>
    /// <param name="justTol">ジャスト判定の許容(±m。設計書=1.5)</param>
    /// <param name="overSpan">超過グラデ幅(m)。signedLead=justLow-overSpanで完全に青</param>
    /// <param name="redSpan">遅延グラデ幅(m)。justHigh+redSpanで完全に赤</param>
    /// <param name="t">グラデ係数[0,1](Behind: 橙→赤 / OverPace: 緑→青)</param>
    public static PaceState Evaluate(float signedLead, float targetLead, float justTol,
        float overSpan, float redSpan, out float t)
    {
        float justLow = targetLead - justTol;   // 例: 1.5
        float justHigh = targetLead + justTol;   // 例: 4.5

        if (signedLead > justHigh)
        {
            t = Clamp01((signedLead - justHigh) / SafeSpan(redSpan));
            return PaceState.Behind;
        }
        if (signedLead < justLow)
        {
            t = Clamp01((justLow - signedLead) / SafeSpan(overSpan));
            return PaceState.OverPace;
        }
        t = 0f;
        return PaceState.Just;
    }

    private static float SafeSpan(float span) => span > 0.0001f ? span : 0.0001f;

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
