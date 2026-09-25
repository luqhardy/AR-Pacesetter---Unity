/// <summary>
/// 「アバターが見えない理由」の判定 (純ロジック・Unity非依存)。
///
/// <para><b>なぜ要るか</b>: アバターを非表示にする経路はコード上に少なくとも7つある
/// (FSMのStandby / フェードアウト / 実測フロア待ち / 障害物停止 / 離隔待機 /
/// 視野外 / 平面遮蔽)。実機で「消えた」と報告されても、どの経路かが分からないと
/// 毎回コード全体を疑い直すことになる。**症状ではなく経路を報告させる**のがこのクラスの目的。</para>
///
/// <para>優先順位は「より根本的な原因」を先に出す — GameObjectが無効なら
/// 描画設定を見ても意味が無いし、GPSロストで消えているなら視野角は関係ない。</para>
/// </summary>
public static class AvatarVisibilityReason
{
    /// <summary>判定に必要な観測値。MonoBehaviour側が毎フレーム埋める。</summary>
    public struct Inputs
    {
        /// <summary>アバターのGameObjectがヒエラルキー上で有効か。</summary>
        public bool GameObjectActive;
        /// <summary>有効なRendererが1つでもあるか(全て無効=実測フロア待ちの抑止など)。</summary>
        public bool AnyRendererEnabled;
        /// <summary>GPSロストFSMの状態名(GameStateController.ARVisionState の文字列)。</summary>
        public string FsmState;
        /// <summary>障害物停止中(§4.2)。</summary>
        public bool Halted;
        /// <summary>離隔待機中(10m離れて座標固定)。</summary>
        public bool WaitingForUser;
        /// <summary>サイレント復帰による位置上書き中。</summary>
        public bool OverriddenByRecovery;
        /// <summary>カメラ視野内か(ビューポート判定・余白込み)。</summary>
        public bool InCameraView;
        /// <summary>カメラからの水平距離(m)。</summary>
        public float DistanceMeters;
        /// <summary>カメラ正面からの水平角(度・絶対値)。</summary>
        public float HorizontalAngleDegrees;
        /// <summary>検出平面によるアバター遮蔽が有効か。</summary>
        public bool PlaneOcclusionEnabled;
    }

    /// <summary>これより遠いと「遠すぎる」を理由に出す(m)。通常追従は3.0m。</summary>
    public const float FarThresholdMeters = 15f;

    /// <summary>
    /// 見えているかと、見えていない場合の理由(日本語1行)を返す。
    /// 見えている場合の理由は "visible"。
    /// </summary>
    public static bool Resolve(in Inputs i, out string reason)
    {
        if (!i.GameObjectActive)
        {
            reason = IsGpsLostState(i.FsmState)
                ? $"GPSロスト: FSM={i.FsmState} でアバターを無効化(F-10 フェードアウト後のスタンバイ)"
                : "アバターのGameObjectが無効(グラス切断のスタンバイ or 外部からのSetActive(false))";
            return false;
        }

        if (!i.AnyRendererEnabled)
        {
            reason = "全Rendererが無効(実測フロア待ちの描画抑止 hideUntilMeasuredFloor の可能性)";
            return false;
        }

        if (i.FsmState == "FadeOut")
        {
            reason = "GPSロスト: フェードアウト中(α→0、1秒後にスタンバイで消える)";
            return false;
        }

        if (i.FsmState == "Reaccumulation")
        {
            reason = "GPSロスト復帰中: 精度5m以内を待っている(屋内では戻らないことがある)";
            return false;
        }

        if (!i.InCameraView)
        {
            string where = i.HorizontalAngleDegrees > 90f ? "背後" : "視野外";
            reason = $"{where}にいる(正面から{i.HorizontalAngleDegrees:F0}°・{i.DistanceMeters:F1}m)" +
                     (i.Halted ? " — 障害物停止で置き去り" :
                      i.WaitingForUser ? " — 離隔待機で座標固定" :
                      i.OverriddenByRecovery ? " — サイレント復帰中" :
                      " — 進行方向の推定ずれの可能性");
            return false;
        }

        if (i.DistanceMeters > FarThresholdMeters)
        {
            reason = $"遠すぎる({i.DistanceMeters:F1}m)" +
                     (i.WaitingForUser ? " — 離隔待機中" : "");
            return false;
        }

        // ここまで来れば描画はされている。ただし「壁の向こう」は判定できないので注記だけ出す
        reason = i.PlaneOcclusionEnabled
            ? "visible(注: 平面遮蔽ONのため壁・机の向こう側では描画されない)"
            : "visible";

        if (i.FsmState == "InertialMovement")
            reason = "visible(GPSロスト慣性移動中 — 5秒続くとフェードアウトする)";

        return true;
    }

    /// <summary>GPSロスト系の状態か(Normal以外)。</summary>
    public static bool IsGpsLostState(string fsmState)
        => fsmState == "InertialMovement" || fsmState == "FadeOut"
        || fsmState == "Standby" || fsmState == "Reaccumulation";
}
