using UnityEngine;

/// <summary>
/// ARグラスへ出す映像を「グラスの光学系・グラスの視点」で描くための出力リグ。
///
/// <para><b>解いている問題は2つ</b>:</para>
/// <list type="number">
/// <item><b>画角が違う</b>: グラス接続中も描画はiPhoneのARKitカメラのままで、投影行列は
///   iPhoneカメラの内部パラメータ由来だった。XREAL One の画角(対角50° → 垂直約25.7°)とは
///   別物なので、3.0m前方に置いたアバターが<b>実寸の角度で見えない</b>。
///   グラスへ出す間はグラスの画角で描く。</item>
/// <item><b>視点が違う</b>: iPhoneは胸マウント、グラスは目の位置。さらにARKitカメラの姿勢を
///   そのまま使うと、胸の上下動・ロールがそのまま頭固定スクリーンへ出て画面全体が揺れる
///   (§4.1が禁じている酔いの原因そのもの)。視点を眼高へ持ち上げ、向きは
///   §4.1の移動平均済み進行方向(ヨーのみ)から作る。</item>
/// </list>
///
/// <para><b>ARKitカメラは触らない</b>。トラッキングは従来どおりARKitが担当し、
/// 本リグは別カメラ(タグ無し = <c>Camera.main</c> を奪わない)を足して描画だけを引き取る。
/// グラス切断で元通りに戻す。</para>
///
/// <para>グラス側は<b>Follow(固定)モード</b>である前提。Anchorモードだとグラスが自前の3DoFで
/// 頭回転を打ち消すため二重補正になる(<see cref="HeadPoseMath"/> 参照)。</para>
/// </summary>
public sealed class GlassViewRig : MonoBehaviour
{
    /// <summary>検証で出力リグ自体を無効化したいときに false にする(既定は有効)。</summary>
    public static bool VerificationDefaultGlassOutput = true;

    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera arCamera;
    [SerializeField] private RunnerTrackingState tracking;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Head model")]
    [Tooltip("iPhoneの装着高(胸マウント)。GroundSnap.assumedCameraHeightMeters と対の想定値")]
    [SerializeField] private float chestMountHeightMeters = HeadPoseMath.DefaultChestMountHeightMeters;
    [Tooltip("眼の高さ。グラスはここにある")]
    [SerializeField] private float eyeHeightMeters = HeadPoseMath.DefaultEyeHeightMeters;

    [Header("Glass")]
    [SerializeField] private GlassScreenMode screenMode = GlassScreenMode.FollowLocked;
    [Tooltip("狭いFoVへアバターを収めるための下向き俯角の上限(度)")]
    [SerializeField] private float maxDownPitchDegrees = 15f;

    private Camera _outputCamera;
    private Quaternion _externalPose = Quaternion.identity;
    private float _externalPoseTime = -1f;

    // ARカメラの復帰用に退避する描画設定
    private int _savedCullingMask;
    private CameraClearFlags _savedClearFlags;
    private Color _savedBackground;
    private bool _savedValid;

    // ── 検証用の公開状態 ────────────────────────────────────────────
    public bool IsGlassOutputActive { get; private set; }
    public GlassDisplayProfile ActiveProfile { get; private set; } = GlassDisplayProfile.PhoneScreen;
    public HeadOrientationSource OrientationSource { get; private set; } = HeadOrientationSource.PhoneCamera;
    public Camera OutputCamera => _outputCamera;
    public float AppliedDownPitchDegrees { get; private set; }
    public bool FullBodyFitsInFov { get; private set; } = true;

    /// <summary>足元の地面が見え始める距離(m)。∞ なら地面は一切視野に入らない。</summary>
    public float NearestVisibleGroundMeters { get; private set; } = float.PositiveInfinity;

    /// <summary>出力カメラのビューポート縦横比(=実際の描画面の縦横比)。</summary>
    public float ViewportAspect => _outputCamera != null ? _outputCamera.aspect : 0f;

    /// <summary>
    /// 描画面がグラスの縦横比と一致しているか。**false なら画面を埋めきれていない** —
    /// Swift側でUnityビューを移設したときに描画面(CAMetalLayer)の作り直しが
    /// 効いていないことを意味する。この場合でも横方向の画角は実物に合わせるため
    /// 見え方は歪まないが、上下に描かない帯が出る(または上下がはみ出す)。
    /// </summary>
    public bool ViewportMatchesGlass =>
        ActiveProfile != null && ActiveProfile.OverridesProjection && _outputCamera != null
        && System.Math.Abs(_outputCamera.aspect - ActiveProfile.Aspect) <= 0.01;
    public string LastFitReport { get; private set; } = string.Empty;

    void Awake() => Resolve();

    private void Resolve()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (tracking == null) tracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
        if (avatarEngine == null) avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
    }

    // ────────────────────────────────────────────────────────────────
    // 有効化 / 無効化 (DeviceManagerBridge から呼ばれる)
    // ────────────────────────────────────────────────────────────────

    /// <summary>グラス接続。プロファイルの画角・視点で描画を引き取る。</summary>
    public void EnableGlassOutput(GlassDisplayProfile profile)
    {
        Resolve();

        if (!VerificationDefaultGlassOutput)
        {
            Debug.Log("[GLASS] 出力リグは検証フラグで無効化されています(ARKitカメラのまま描画)。");
            return;
        }
        if (profile == null || !profile.OverridesProjection)
        {
            Debug.LogWarning("[GLASS] 表示プロファイルが不明なため画角の上書きを見送ります。");
            return;
        }
        if (arCamera == null)
        {
            Debug.LogWarning("[GLASS] ARカメラが見つからないため出力リグを起動できません。");
            return;
        }

        ActiveProfile = profile;
        EnsureOutputCamera();
        TakeOverFromArCamera();
        IsGlassOutputActive = true;

        ReportOpticalFit();
        ApplyHudSafeArea();
        UpdateOutputCamera();

        Debug.Log($"[GLASS] 出力リグ起動: {profile} / 描画面 {Screen.width}x{Screen.height} " +
                 $"(アスペクト {ViewportAspect:F3} 期待 {profile.Aspect:F3} " +
                 $"{(ViewportMatchesGlass ? "一致" : "不一致=画面を埋めきれていない")})");
    }

    /// <summary>グラス切断。ARKitカメラの描画へ戻す。</summary>
    public void DisableGlassOutput()
    {
        if (!IsGlassOutputActive) return;

        IsGlassOutputActive = false;
        ActiveProfile = GlassDisplayProfile.PhoneScreen;
        OrientationSource = HeadOrientationSource.PhoneCamera;
        AppliedDownPitchDegrees = 0f;

        if (_outputCamera != null) _outputCamera.enabled = false;
        RestoreArCamera();
        RestoreHudSafeArea();

        Debug.Log("[GLASS] 出力リグ停止 — iPhone画面(ARKitカメラ)の描画へ戻しました。");
    }

    /// <summary>
    /// グラス実機の頭部姿勢を受け取る(将来用)。供給が途切れれば自動で進行方向ヨーへ落ちる。
    /// iOSでは現状供給元が無い — 詳細は Docs/XREAL_ONE_INTEGRATION.md。
    /// </summary>
    public void SetExternalHeadPose(Quaternion rotation, float timestampSeconds)
    {
        _externalPose = rotation;
        _externalPoseTime = timestampSeconds > 0f ? timestampSeconds : Time.time;
    }

    public bool HasFreshExternalPose =>
        HeadPoseMath.IsExternalPoseFresh(Time.time, _externalPoseTime, HeadPoseMath.DefaultExternalPoseMaxAgeSeconds);

    // ────────────────────────────────────────────────────────────────
    // 毎フレームの姿勢・投影更新
    // ────────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!IsGlassOutputActive) return;
        UpdateOutputCamera();
    }

    private void UpdateOutputCamera()
    {
        if (_outputCamera == null || arCamera == null) return;

        OrientationSource = HeadPoseMath.ResolveOrientationSource(true, screenMode, HasFreshExternalPose);

        // 視点: ARKitカメラ(胸)の位置を眼高へ持ち上げる
        Vector3 eye = arCamera.transform.position;
        eye.y += HeadPoseMath.ChestToEyeOffsetMeters(chestMountHeightMeters, eyeHeightMeters);
        _outputCamera.transform.position = eye;

        // 向き
        float downPitch = 0f;
        Quaternion rotation;
        switch (OrientationSource)
        {
            case HeadOrientationSource.ExternalGlassPose:
                // 実姿勢にはピッチが含まれるので、こちらで俯角を足さない
                rotation = _externalPose;
                break;

            case HeadOrientationSource.TravelHeading:
            default:
                downPitch = (float)ResolveDownPitch();
                rotation = Quaternion.Euler(downPitch, ResolveHeadingYawDegrees(), 0f);
                break;
        }

        _outputCamera.transform.rotation = rotation;
        AppliedDownPitchDegrees = downPitch;

        ApplyProjection();
    }

    /// <summary>§4.1の移動平均済み進行方向からヨー角を作る(ロール・ピッチは持ち込まない)。</summary>
    private float ResolveHeadingYawDegrees()
    {
        Vector3 heading = Vector3.zero;
        if (tracking != null) heading = tracking.CurrentHeading;

        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-6f && arCamera != null)
        {
            heading = arCamera.transform.forward;
            heading.y = 0f;
        }
        if (heading.sqrMagnitude < 1e-6f) return _outputCamera.transform.eulerAngles.y;

        return Quaternion.LookRotation(heading.normalized, Vector3.up).eulerAngles.y;
    }

    private double ResolveDownPitch()
    {
        double avatarHeight = AvatarHeightMeters();
        double distance = LeadDistanceMeters();
        return HeadPoseMath.ResolveDownPitchDegrees(eyeHeightMeters, avatarHeight, distance,
                                                    ActiveProfile.VerticalFovDegrees, maxDownPitchDegrees);
    }

    /// <summary>
    /// グラスの画角で投影する。ARFoundationがARKit由来の投影行列を毎フレーム書き戻しうるため、
    /// 出力カメラ側で必ず <c>ResetProjectionMatrix</c> してから画角を入れ直す。
    /// </summary>
    private void ApplyProjection()
    {
        double viewportAspect = _outputCamera.aspect;
        double vFov = ActiveProfile.VerticalFovDegrees;

        // 外部ディスプレイが16:9ならそのまま。違う場合は水平画角を正として垂直を求め直す
        // (横方向の角度スケールを実物に合わせる = 横に潰れない)
        if (viewportAspect > 0.0 && System.Math.Abs(viewportAspect - ActiveProfile.Aspect) > 0.01)
            vFov = GlassOpticsMath.VerticalFovForViewport(ActiveProfile.HorizontalFovDegrees, viewportAspect);

        _outputCamera.ResetProjectionMatrix();
        _outputCamera.fieldOfView = (float)vFov;
    }

    // ────────────────────────────────────────────────────────────────
    // カメラの生成と受け渡し
    // ────────────────────────────────────────────────────────────────

    private void EnsureOutputCamera()
    {
        if (_outputCamera != null)
        {
            _outputCamera.enabled = true;
            return;
        }

        var go = new GameObject("GlassOutputCamera");
        go.transform.SetParent(null);
        // タグは Untagged のまま: Camera.main を奪うと既存の視野判定・診断が全部こちらを見てしまう
        _outputCamera = go.AddComponent<Camera>();
        _outputCamera.clearFlags = CameraClearFlags.SolidColor;
        _outputCamera.backgroundColor = Color.black; // 光学シースルーでは黒=透過
        _outputCamera.nearClipPlane = 0.05f;
        _outputCamera.farClipPlane = arCamera != null ? arCamera.farClipPlane : 300f;
        _outputCamera.cullingMask = arCamera != null ? arCamera.cullingMask : ~0;
        _outputCamera.depth = arCamera != null ? arCamera.depth + 1f : 1f;
        _outputCamera.allowHDR = false;
        _outputCamera.allowMSAA = false;
    }

    /// <summary>
    /// ARカメラは<b>有効なまま</b>(ARKitのフレーム供給・トラッキングを止めないため)、
    /// 描画内容だけを空にして出力カメラへ譲る。
    /// </summary>
    private void TakeOverFromArCamera()
    {
        if (arCamera == null || _savedValid) return;

        _savedCullingMask = arCamera.cullingMask;
        _savedClearFlags = arCamera.clearFlags;
        _savedBackground = arCamera.backgroundColor;
        _savedValid = true;

        arCamera.cullingMask = 0;
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
    }

    private void RestoreArCamera()
    {
        if (arCamera == null || !_savedValid) return;

        arCamera.cullingMask = _savedCullingMask;
        arCamera.clearFlags = _savedClearFlags;
        arCamera.backgroundColor = _savedBackground;
        _savedValid = false;
    }

    // ────────────────────────────────────────────────────────────────
    // 光学的な成立可否の報告 (F-03/F-05/§7.2 への影響を実測値で出す)
    // ────────────────────────────────────────────────────────────────

    private float LeadDistanceMeters()
        => avatarEngine != null ? avatarEngine.LeadDistanceMeters : 3.0f;

    private float AvatarHeightMeters()
    {
        float h = avatarEngine != null ? avatarEngine.MeasuredAvatarHeightMeters : 0f;
        return h > 0.01f ? h : 1.75f;
    }

    /// <summary>
    /// 「この画角でアバターの全身と足元が見えるのか」を接続時に1回だけ実測して残す。
    /// 収まらない場合は仕様側の判断が要るため警告で出す(距離を伸ばす / 足元の演出を諦める等)。
    /// </summary>
    private void ReportOpticalFit()
    {
        double vFov = ActiveProfile.VerticalFovDegrees;
        double distance = LeadDistanceMeters();
        double height = AvatarHeightMeters();
        double pitch = ResolveDownPitch();

        double span = GlassOpticsMath.BodySpanDegrees(eyeHeightMeters, height, distance);
        FullBodyFitsInFov = GlassOpticsMath.FullBodyFits(eyeHeightMeters, height, distance, vFov);
        double minDistance = GlassOpticsMath.MinimumFullBodyDistanceMeters(eyeHeightMeters, height, vFov);
        double ground = GlassOpticsMath.NearestVisibleGroundDistanceMeters(eyeHeightMeters, vFov, pitch);
        NearestVisibleGroundMeters = (float)ground;

        string groundText = double.IsInfinity(ground) ? "なし" : ground.ToString("F2") + "m";
        LastFitReport =
            $"{ActiveProfile.Model}: 垂直FoV {vFov:F1}° / アバター(高さ{height:F2}m・{distance:F1}m前方)の占有角 {span:F1}° / " +
            $"俯角 {pitch:F1}° / 全身 {(FullBodyFitsInFov ? "収まる" : "収まらない")} / " +
            $"全身に必要な距離 {minDistance:F2}m / 地面が見え始める距離 {groundText}";

        if (FullBodyFitsInFov)
            Debug.Log($"[GLASS] 光学適合: {LastFitReport}");
        else
            Debug.LogWarning($"[GLASS] 光学適合(要判断): {LastFitReport}");
    }

    // ────────────────────────────────────────────────────────────────
    // HUD (F-07) をグラスのセーフエリア内へ寄せる
    // ────────────────────────────────────────────────────────────────

    private void ApplyHudSafeArea()
    {
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (hud != null) hud.ApplyEdgeInsetFraction((float)ActiveProfile.SafeAreaFraction);
    }

    private void RestoreHudSafeArea()
    {
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (hud != null) hud.ApplyEdgeInsetFraction(1f);
    }
}
