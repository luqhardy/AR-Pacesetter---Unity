using System;

/// <summary>
/// グラス側(X1チップ)が映像をどう扱っているか。ここを間違えると<b>二重補正</b>になる。
/// </summary>
public enum GlassScreenMode
{
    /// <summary>Follow(固定): 仮想スクリーンが常に目の前に貼り付く。頭の回転をグラスは補正しない。</summary>
    FollowLocked,

    /// <summary>Follow(スムーズ): 目の前に追従するが遅れて付いてくる。追従ラグぶん見かけが揺れる。</summary>
    FollowSmooth,

    /// <summary>Anchor: グラスが自前の3DoF/6DoFで空間にスクリーンを固定する(=グラスが頭回転を補正する)。</summary>
    Anchor,

    /// <summary>不明(ユーザーが設定を触れる以上、判らないことがある)。</summary>
    Unknown,
}

/// <summary>描画カメラの向きを何から作るか。</summary>
public enum HeadOrientationSource
{
    /// <summary>iPhoneのARKitカメラの姿勢そのまま(iPhone画面へのビデオシースルー表示用)。</summary>
    PhoneCamera,

    /// <summary>§4.1の移動平均済み進行方向(ヨーのみ)。胸マウントの揺れ・ロールを持ち込まない。</summary>
    TravelHeading,

    /// <summary>グラス実機から供給された頭部姿勢(将来用。iOSでは現状供給元が無い)。</summary>
    ExternalGlassPose,
}

/// <summary>
/// 「グラスへ出す映像をどの姿勢で描くか」の判断を1か所に閉じた純ロジック (依存ゼロ)。
///
/// <para><b>背景</b>: XREAL One の映像入力はDisplayPort Alt Modeの平面映像で、
/// グラス側のX1チップが Follow / Anchor いずれかのモードでその平面を提示する。
/// つまりUnityが描くのは「頭に貼り付いた1枚のスクリーン」であって、
/// ステレオでも頭部追従レンダリングでもない。</para>
///
/// <para><b>二重補正の罠</b>: Anchorモードではグラスが自前のIMUで頭の回転を打ち消す。
/// そこへUnity側でも頭部回転を適用すると補正が2回かかり、首を振るたびに
/// 映像が逆方向へ流れる。<b>Unityが姿勢を持つならグラスはFollow(固定)であること</b>。</para>
///
/// <para><b>第1期の帰結</b>: iOSにはグラスの頭部姿勢を取る手段が無い
/// (XREAL SDKはAndroid専用、USB-HIDはiOSアプリから触れない)。したがって
/// Follow(固定) × <see cref="HeadOrientationSource.TravelHeading"/> が唯一成立する組み合わせで、
/// これは §4.1 が要求する「グラスのコンパスではなくiPhoneの移動平均を使う」と一致する。</para>
/// </summary>
public static class HeadPoseMath
{
    /// <summary>外部頭部姿勢を「新しい」とみなす上限(秒)。100Hz供給なら十分に緩い。</summary>
    public const float DefaultExternalPoseMaxAgeSeconds = 0.25f;

    /// <summary>胸マウント高(m) — GroundSnap.assumedCameraHeightMeters と対になる想定値。</summary>
    public const float DefaultChestMountHeightMeters = 1.2f;

    /// <summary>眼高(m) — 成人ランナーの想定。胸マウントとの差ぶんだけ視点を持ち上げる。</summary>
    public const float DefaultEyeHeightMeters = 1.55f;

    /// <summary>
    /// 描画カメラの向きをどこから取るかを決める。
    /// グラス非接続ならiPhone画面に出しているのでARKitカメラそのまま。
    /// </summary>
    public static HeadOrientationSource ResolveOrientationSource(bool glassConnected, GlassScreenMode screenMode,
                                                                 bool hasFreshExternalPose)
    {
        if (!glassConnected) return HeadOrientationSource.PhoneCamera;

        // グラスが自前で頭回転を補正している(Anchor)なら、こちらは姿勢を足してはいけない。
        // 進行方向ヨーのみを使い、頭の向きはグラスに任せる。
        if (screenMode == GlassScreenMode.Anchor) return HeadOrientationSource.TravelHeading;

        if (hasFreshExternalPose) return HeadOrientationSource.ExternalGlassPose;

        return HeadOrientationSource.TravelHeading;
    }

    /// <summary>
    /// 二重補正が起きうる組み合わせか。true のときは実機で「首を振ると映像が逆に流れる」。
    /// グラスの画面モードをFollow(固定)へ変えてもらう以外に回避手段は無い。
    /// </summary>
    public static bool IsDoubleCompensationRisk(GlassScreenMode screenMode, HeadOrientationSource source)
        => screenMode == GlassScreenMode.Anchor && source == HeadOrientationSource.ExternalGlassPose;

    // ── 首振り(タスク表 ARグラス「首を振り向けた時に想定する場所を走る」2026-10-19) ──
    // グラスは Follow(固定) なので、首を振ると画面ごと顔に付いて回り、アバターも視線の先へ付いてくる。
    // グラスの姿勢は iOS から取れないが、AirPods(Pro・第3世代以降・Max)の頭の向きは取れる。
    // Swift が AirPods の頭のヨー(基準は任意・右が正)を送り、進行方向ヨーとの差から首振りを作って
    // 描画の向きへ足す — 右を向けばアバターは画面の左へ流れ、進行方向の3m先に留まって見える。

    /// <summary>頭の向きの供給を「新しい」とみなす上限(秒)。Swift は約30Hzで送る</summary>
    public const float DefaultHeadYawMaxAgeSeconds = 0.5f;

    /// <summary>首振りとして足す最大角(度)。それ以上は振り返り・センサーの外れ値として打ち切る</summary>
    public const float MaxHeadYawOffsetDegrees = 90f;

    /// <summary>
    /// 「まっすぐ前を向いている」基準を追う時定数(秒)。AirPods の向きは基準が任意で、時間とともにずれる
    /// (ドリフト)。数秒の首振りはほぼそのまま残り、横を向いたままの差は数十秒かけて0へ戻る
    /// </summary>
    public const float DefaultHeadYawBaselineSeconds = 30f;

    /// <summary>
    /// 首振りを足すか。Anchor ではグラスが自前で頭回転を打ち消すので足すと二重補正になる。
    /// </summary>
    public static bool ShouldApplyHeadYaw(GlassScreenMode screenMode, bool headYawFresh)
        => headYawFresh && screenMode != GlassScreenMode.Anchor;

    /// <summary>
    /// 進行方向ヨーに首振り(右向きが正・度)を足した描画カメラのヨー(度)。
    /// 足さない場合・数でない値は進行方向ヨーのまま。首振りは ±<see cref="MaxHeadYawOffsetDegrees"/> に収める。
    /// </summary>
    public static float ResolveViewYawDegrees(float travelYawDegrees, float headYawOffsetDegrees, bool applyOffset)
    {
        if (!applyOffset || float.IsNaN(headYawOffsetDegrees) || float.IsInfinity(headYawOffsetDegrees))
            return travelYawDegrees;
        float clamped = Math.Max(-MaxHeadYawOffsetDegrees, Math.Min(MaxHeadYawOffsetDegrees, headYawOffsetDegrees));
        return travelYawDegrees + clamped;
    }

    /// <summary>角度差を -180〜180° へ折り返す</summary>
    public static float WrapDegrees(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;
        float d = degrees % 360f;
        if (d > 180f) d -= 360f;
        if (d <= -180f) d += 360f;
        return d;
    }

    /// <summary>外部姿勢が新鮮か(供給が途切れたら自動で進行方向ヨーへ落ちる)。</summary>
    public static bool IsExternalPoseFresh(float nowSeconds, float sampleSeconds, float maxAgeSeconds)
    {
        if (sampleSeconds <= 0f) return false;
        float age = nowSeconds - sampleSeconds;
        return age >= 0f && age <= maxAgeSeconds;
    }

    /// <summary>
    /// 胸マウントのARKitカメラ高から眼の高さへ持ち上げる量(m)。
    /// グラスは目の位置にあるので、視点をカメラ位置のままにすると地面が近く見え、
    /// 3.0m前方のアバターの見かけの俯角がずれる。
    /// </summary>
    public static float ChestToEyeOffsetMeters(float chestMountHeightMeters, float eyeHeightMeters)
        => eyeHeightMeters - chestMountHeightMeters;

    /// <summary>
    /// グラスのFoVは狭いので、光軸を水平のままにするとアバターの足元が視野の下に落ちる。
    /// アバターが視野中心に来る俯角を返し、視野外へはみ出さないようクランプする。
    /// </summary>
    public static double ResolveDownPitchDegrees(double eyeHeightMeters, double avatarHeightMeters,
                                                 double distanceMeters, double verticalFovDegrees,
                                                 double maxDownPitchDegrees)
    {
        double centering = GlassOpticsMath.CenteringDownPitchDegrees(eyeHeightMeters, avatarHeightMeters, distanceMeters);
        if (double.IsNaN(centering)) return 0.0;
        return Math.Max(0.0, Math.Min(centering, maxDownPitchDegrees));
    }
}

/// <summary>
/// AirPods の頭のヨー(基準は任意)と進行方向ヨーから首振り(度・右が正)を作る。
/// 差の長周期の平均を「まっすぐ前」の基準として追い続けるので、AirPods と進行方向の基準のずれや
/// ドリフトは消え、数秒の首振りだけが残る。カーブでは頭も進行方向も一緒に回るので差は変わらない。
/// 依存ゼロ(Tests/UnitTests でテストする)。
/// </summary>
public struct HeadYawBaseline
{
    private float _baselineDegrees;
    private bool _initialized;

    /// <summary>基準が決まっているか(最初のサンプルで「まっすぐ前」とみなす)</summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// 1フレーム進めて首振り(度・右が正)を返す。最初のサンプルは0(開始時はまっすぐ前を向いている前提)。
    /// </summary>
    public float Update(float headYawDegrees, float travelYawDegrees, float deltaSeconds,
                        float baselineSeconds = HeadPoseMath.DefaultHeadYawBaselineSeconds)
    {
        float diff = HeadPoseMath.WrapDegrees(headYawDegrees - travelYawDegrees);
        if (!_initialized)
        {
            _baselineDegrees = diff;
            _initialized = true;
            return 0f;
        }

        if (deltaSeconds > 0f && baselineSeconds > 0f)
        {
            float alpha = 1f - (float)Math.Exp(-deltaSeconds / baselineSeconds);
            _baselineDegrees = HeadPoseMath.WrapDegrees(
                _baselineDegrees + alpha * HeadPoseMath.WrapDegrees(diff - _baselineDegrees));
        }
        return HeadPoseMath.WrapDegrees(diff - _baselineDegrees);
    }

    /// <summary>供給が途切れたら基準を捨てる(AirPods を付け直すと向きの基準が変わる)</summary>
    public void Reset()
    {
        _initialized = false;
        _baselineDegrees = 0f;
    }
}
