using System.Collections;
using UnityEngine;
using TMPro; // Essential for modern high-performance text rendering

public class PeripheralHUDManager : MonoBehaviour
{
    [Header("UI Text Components (Core)")]
    [SerializeField] private TextMeshProUGUI textHeartRate;
    [SerializeField] private TextMeshProUGUI textTime;
    [SerializeField] private TextMeshProUGUI textDistance;
    [SerializeField] private TextMeshProUGUI textPace;　

    [Header("UI Text Components (Advanced Telemetry)")]
    [SerializeField] private TextMeshProUGUI textPitch;              // Cadence (SPM)
    [SerializeField] private TextMeshProUGUI textSyncRate;           // Synchronicity rate (%)
    [SerializeField] private TextMeshProUGUI textFatigueIndex;       // Cumulative fatigue & Cf multiplier
    [SerializeField] private TextMeshProUGUI textGrade;              // Real-time run rating (S - D)
    [SerializeField] private TextMeshProUGUI textNotificationAlert;  // Fading overlay alert for splits

    [Header("F-07 周辺視野レイアウト")]
    [Tooltip("F-07の4ゾーン(左上=時間/距離・右上=現在ペース・中央=完全透過・下部=警告時のみ)へ絞る。" +
             "補助表示(心拍/ピッチ/シンクロ率/疲労/グレード)を隠し、残る3項目を規定の隅へ再配置する。" +
             "デバッグで全項目を見たい場合のみインスペクタでOFFにする")]
    [SerializeField] private bool peripheralModeOnly = true;

    [Header("Safety Warning (F-10)")]
    [Tooltip("HUD下部の警告行。未割当なら実行時に生成する(F-07: 下部=警告時のみ)")]
    [SerializeField] private TextMeshProUGUI textSafetyWarning;

    [Header("Engine Links")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private AvatarEngine avatarEngine;   // For fetching target speed
    [SerializeField] private AnalyticsManager analytics; // For tracking split alerts
    [SerializeField] private AvatarVisualsAndActions visualsEngine; // Simulated HR feed (editor)

    // Telemetry tracking state variables
    private float _elapsedTimeSeconds = 0.0f;
    private float _cumulativeDistanceMeters = 0.0f;
    private Vector3 _lastUserPosition;

    // Biometric mock baseline metrics for editor simulation
    private int _simulatedHeartRate = 135;
    private float _simulatedPitch = 172.0f;
    private Coroutine _splitAlertCoroutine;

    // Battery warning flash state (企画書 2 — バッテリー10%以下の黄色点滅)
    private static readonly Color BatteryWarningYellow = new Color(1f, 0.9f, 0.1f);
    private Color _hudDefaultColor = Color.white;
    private bool _hudColorCached = false;
    private bool _batteryFlashActive = false;

    // Editor: V key spikes HR to test the vital-warning (deep blue) state
    private float _hrSpikeUntil = -1f;

    // F-07 現在ペース: 実測(Swift)が無い環境では自前の移動量から算出する。
    // 生の瞬間速度は歩幅ごとに跳ねるため時定数3秒で平滑化する
    private float _smoothedSpeedMps;
    private const float SpeedSmoothingTauSeconds = 3.0f;
    private ARSessionManagerBridge _sessionBridge;

    // F-07/F-10 ペース色と警告色
    private static readonly Color PaceMaintainingGreen = new Color(0.30f, 0.92f, 0.45f);
    private static readonly Color PaceBehindRed        = new Color(1.00f, 0.36f, 0.30f);
    private static readonly Color PaceUnknownNeutral   = new Color(0.85f, 0.87f, 0.88f);

    // F-10: ロスト継続時にHUD下部へ出す赤字警告(設計書の文言そのまま)
    private const string GpsSearchingWarning = "GPS信号を探索中：安全のため減速してください";
    private GameStateController _gameState;
    private PaceHudDisplay.PaceState _currentPaceState = PaceHudDisplay.PaceState.Unknown;
    private bool _peripheralLayoutApplied;
    private bool _hudHidden;

    // HUD自動抑制 (企画書 2. スタビライズ — 横を向いた際は表示を自動抑制)
    private const float GazeSuppressYawRateDegPerSec = 120f; // この角速度を超えたら抑制
    private const float GazeSuppressHoldSeconds = 0.8f;      // 首振り終了後の抑制保持
    private const float SuppressedAlpha = 0.15f;
    private float _lastCameraYaw;
    private bool _yawInitialized = false;
    private float _suppressUntil = -1f;
    private float _hudVisibility = 1.0f;

    // Session read access for the result/stop flow
    public float ElapsedTimeSeconds => _elapsedTimeSeconds;
    public float DistanceMeters => _cumulativeDistanceMeters;
    public int CurrentHeartRate => _simulatedHeartRate;

    /// <summary>HUDの現在可視度(1=通常、首振り抑制中は0.15へフェード)。E2E検証用。</summary>
    public float CurrentHudVisibility => _hudVisibility;

    /// <summary>再走行対応: タイム・距離の累積をリセットする。</summary>
    public void ResetSession()
    {
        _elapsedTimeSeconds = 0.0f;
        _cumulativeDistanceMeters = 0.0f;
        _runStartUtc = System.DateTime.MinValue;
        if (userCamera != null)
            _lastUserPosition = userCamera.position;
    }

    // 走行開始の実時刻(壁時計)。バックグラウンド中も経過時間が正しく進む
    private System.DateTime _runStartUtc = System.DateTime.MinValue;

    void Start()
    {
        if (userCamera != null)
        {
            _lastUserPosition = userCamera.position;
        }

        if (_sessionBridge == null)
            _sessionBridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
        if (_gameState == null)
            _gameState = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        // レイアウト確定を先に済ませる。装飾(アウトライン)より前に置くことで、
        // 装飾側で何かあってもF-07の配置だけは確実に適用される
        ApplyPeripheralLayout();

        if (textSafetyWarning == null)
            BuildSafetyWarningLabel();

        if (visualsEngine == null)
        {
            visualsEngine = FindFirstObjectByType<AvatarVisualsAndActions>(FindObjectsInactive.Include);
        }

        // 企画書 §2 アダプティブ表示: 1pxアウトラインで高コントラストを確保
        ApplyHighContrastOutline();

        // Initialize HUD text displays if optional
        if (textNotificationAlert != null)
        {
            Color baseColor = textNotificationAlert.color;
            textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.0f); // Initially invisible
        }
    }

    private void OnEnable()
    {
        // Bind splits event callback (Requirement 4.3)
        if (analytics != null)
        {
            analytics.OnSplitReached += HandleSplitReachedNotification;
        }
    }

    private void OnDisable()
    {
        // Unbind event callback to avoid memory leaks
        if (analytics != null)
        {
            analytics.OnSplitReached -= HandleSplitReachedNotification;
        }
    }

    void Update()
    {
        if (userCamera == null) return;

        // Clock and distance only accumulate once the run has started,
        // so the session result reflects the actual run (企画書 §4)
        bool runInProgress = avatarEngine == null || avatarEngine.HasStarted;

        // 1. Calculate Runtime Clock (Format: MM:SS)
        // 壁時計ベース: 画面ロック等でUnityが一時停止しても実経過時間が欠落しない
        // (Time.deltaTime累積だとバックグラウンド走行中の時間がタイムから消える)
        if (runInProgress)
        {
            if (_runStartUtc == System.DateTime.MinValue)
                _runStartUtc = System.DateTime.UtcNow;
            _elapsedTimeSeconds = (float)(System.DateTime.UtcNow - _runStartUtc).TotalSeconds;
        }
        int minutes = Mathf.FloorToInt(_elapsedTimeSeconds / 60f);
        int seconds = Mathf.FloorToInt(_elapsedTimeSeconds % 60f);
        if (textTime != null)
        {
            textTime.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        // 2. Track Cumulative Distance Covered (Meters -> Kilometers)
        Vector3 currentHorizontal = new Vector3(userCamera.position.x, 0f, userCamera.position.z);
        Vector3 lastHorizontal = new Vector3(_lastUserPosition.x, 0f, _lastUserPosition.z);
        float frameMovementDistance = Vector3.Distance(currentHorizontal, lastHorizontal);
        
        // Calculate instantaneous speed from horizontal movement to filter out tracking jitter
        float instSpeed = frameMovementDistance / Mathf.Max(Time.deltaTime, 0.001f);
        
        // Only accumulate distance if movement is significant and speed is within normal human range (0.2 m/s to 15 m/s)
        if (runInProgress && frameMovementDistance > 0.005f && instSpeed > 0.2f && instSpeed < 15.0f)
        {
            _cumulativeDistanceMeters += frameMovementDistance;

            // Pass the distance directly to the analytics manager for split testing.
            // Swift(GPS)がメトリクス供給中はそちらが正 — 二重供給によるスプリット
            // 判定の揺れを防ぐためUnity内部計測からは流さない
            if (analytics != null && !ARSessionManagerBridge.ExternalMetricsActive)
            {
                analytics.CheckDistanceIntervalSplits(_cumulativeDistanceMeters);
            }
        }
        
        // F-07 現在ペース用の速度平滑化。停止時も減衰するよう毎フレーム更新する
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float instantSpeed = frameMovementDistance / dt;
            float k = 1f - Mathf.Exp(-dt / SpeedSmoothingTauSeconds);
            _smoothedSpeedMps += (instantSpeed - _smoothedSpeedMps) * k;
        }

        // Always update tracking position to prevent accumulation of ignored deltas or jumps
        _lastUserPosition = userCamera.position;

        if (textDistance != null)
        {
            float totalKm = _cumulativeDistanceMeters / 1000f;
            textDistance.text = string.Format("{0:F2} km", totalKm);
        }

        // 3. F-07 右上=現在ペース(遅れ=赤 / 維持=緑)
        //    従来は目標ペース(定数)を出しており走行中のフィードバックになっていなかった。
        //    実測(Swift/CoreLocation)を優先し、無ければ自前の平滑化速度から算出する。
        if (textPace != null && avatarEngine != null)
        {
            float measuredKmh = _sessionBridge != null ? _sessionBridge.MeasuredPaceKmH : 0f;
            float currentPace = measuredKmh > 0f
                ? PaceHudDisplay.KmhToPaceMinutesPerKm(measuredKmh)
                : PaceHudDisplay.SpeedToPaceMinutesPerKm(_smoothedSpeedMps);

            textPace.text = PaceHudDisplay.Format(currentPace);

            PaceHudDisplay.PaceState state = PaceHudDisplay.Evaluate(
                currentPace, avatarEngine.TargetPaceMinutesPerKm,
                PaceHudDisplay.DefaultBehindTolerance);
            _currentPaceState = state;

            Color paceColor = state == PaceHudDisplay.PaceState.Behind ? PaceBehindRed
                            : state == PaceHudDisplay.PaceState.Maintaining ? PaceMaintainingGreen
                            : PaceUnknownNeutral;

            // 首振り抑制のアルファを潰さないよう、色だけ差し替える
            textPace.color = new Color(paceColor.r, paceColor.g, paceColor.b, textPace.color.a);
        }

        UpdateSafetyWarning();

        // 4. Update HUD Fields dynamically from Analytics (Requirement 4.3)
        if (analytics != null)
        {
            // Real-Time Synchronicity Rate
            float syncRate = analytics.GetLiveSyncRate();
            if (textSyncRate != null)
            {
                textSyncRate.text = string.Format("Sync: {0:F1}%", syncRate);
            }

            // Cumulative Fatigue Index & Temp multiplier display
            float fatigue = analytics.GetCumulativeFatigue();
            float multiplier = analytics.GetFatigueMultiplier();
            if (textFatigueIndex != null)
            {
                textFatigueIndex.text = string.Format("Fatigue: {0:F2} (x{1:F1})", fatigue, multiplier);
            }

            // Real-Time Session Grade
            string grade = analytics.EvaluateFinalSessionPerformanceRank();
            if (textGrade != null)
            {
                textGrade.text = string.Format("Grade: {0}", grade);
            }
        }

        // 5. Run biometric updates & background simulation if inside Editor fallback
        UpdateBiometricsDisplay();

        // 6. Battery <=10% -> flash HUD text yellow (企画書 2. AR HUD ダイナミック・フィードバック)
        UpdateBatteryWarningFlash();

        // 7. 首振り検知 -> HUD自動フェード (企画書 2. スタビライズ)
        UpdateGazeSuppression();
    }

    private void UpdateGazeSuppression()
    {
        if (userCamera == null) return;

        float currentYaw = userCamera.eulerAngles.y;
        if (!_yawInitialized)
        {
            _lastCameraYaw = currentYaw;
            _yawInitialized = true;
            return;
        }

        float yawRate = Mathf.Abs(Mathf.DeltaAngle(_lastCameraYaw, currentYaw))
                        / Mathf.Max(Time.deltaTime, 0.001f);
        _lastCameraYaw = currentYaw;

        // 素早い首振り(横を向く動作)を検知したら一定時間HUDを薄くする
        if (yawRate > GazeSuppressYawRateDegPerSec)
            _suppressUntil = Time.time + GazeSuppressHoldSeconds;

        float targetVisibility = Time.time < _suppressUntil ? SuppressedAlpha : 1.0f;
        _hudVisibility = Mathf.MoveTowards(_hudVisibility, targetVisibility, Time.deltaTime * 4.0f);

        ApplyHudAlpha(_hudVisibility);
    }

    /// <summary>
    /// F-10: GPSロスト中はHUD下部に赤字警告を出す。設計書が「下部=警告時のみ」と
    /// 定めるゾーンで、平常時は完全に空にしておく。
    /// フェード完了(Standby)まで出し続け、通常追従へ復帰したら消す。
    /// </summary>
    private void UpdateSafetyWarning()
    {
        if (textSafetyWarning == null) return;
        if (_hudHidden) return; // Unity側HUDが非表示のときはSwiftのバナーが担当する

        bool gpsLost = false;
        if (_gameState != null)
        {
            var st = _gameState.currentState;
            gpsLost = st == GameStateController.ARVisionState.InertialMovement
                   || st == GameStateController.ARVisionState.FadeOut
                   || st == GameStateController.ARVisionState.Standby;
        }

        // 走行中のみ。準備画面・終了後に警告を残さない
        bool running = avatarEngine != null && avatarEngine.HasStarted && !avatarEngine.IsSessionEnded;
        bool show = gpsLost && running;

        if (textSafetyWarning.gameObject.activeSelf != show)
            textSafetyWarning.gameObject.SetActive(show);

        if (show)
            textSafetyWarning.text = GpsSearchingWarning;
    }

    /// <summary>
    /// F-10の警告行をHUD下部へ実行時生成する(シーン配線不要)。
    /// 首振り抑制のフェードにもバッテリー点滅の色変更にも巻き込まないため、
    /// ApplyHudAlpha / ApplyHudTextColor の対象からは意図的に外している。
    /// </summary>
    private void BuildSafetyWarningLabel()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) return;

        GameObject go = new GameObject("SafetyWarning", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(canvas.transform, false);

        textSafetyWarning = go.AddComponent<TextMeshProUGUI>();
        // 既存HUDと同じフォントを引き継ぐ(未指定だとマテリアル未解決でアウトラインが効かない)
        if (textTime != null && textTime.font != null)
            textSafetyWarning.font = textTime.font;
        textSafetyWarning.fontSize = 22;
        textSafetyWarning.alignment = TextAlignmentOptions.Center;
        textSafetyWarning.color = PaceBehindRed;
        textSafetyWarning.raycastTarget = false;
        textSafetyWarning.enableWordWrapping = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 64f);
        rt.sizeDelta = new Vector2(900f, 44f);

        ApplyOutline(textSafetyWarning); // 明るい路面でも読めるよう黒アウトライン
        go.SetActive(false);
    }

    /// <summary>
    /// UnityのHUDを丸ごと出すか隠すか。表示面がどちらかで所有者を切り替える:
    ///   グラス接続中 = UnityのF-07 HUD(周辺視野レイアウトはグラス用に設計されている)
    ///   iPhone表示中 = SwiftUI側のHUD(手に持って正面から見る画面に適した意匠)
    /// 二重に描かないための単一所有者スイッチ。既定は表示(エディタ/E2Eは従来どおり)。
    /// </summary>
    public void SetHudVisible(bool visible)
    {
        _hudHidden = !visible;

        SetReadoutVisible(textTime, visible);
        SetReadoutVisible(textDistance, visible);
        SetReadoutVisible(textPace, visible);
        SetReadoutVisible(textNotificationAlert, visible);

        // 隠すときは警告も一緒に伏せる(Swift側がGPSバナーを出すため二重にならない)
        if (!visible && textSafetyWarning != null)
            textSafetyWarning.gameObject.SetActive(false);

        Debug.Log($"[HUD] Unity側HUDを{(visible ? "表示" : "非表示")}に切り替え");
    }

    /// <summary>E2E/検証用: UnityのHUDが表示されているか。</summary>
    public bool IsHudVisible => !_hudHidden;

    /// <summary>E2E/検証用: F-10の警告が今表示されているか。</summary>
    public bool IsSafetyWarningVisible =>
        textSafetyWarning != null && textSafetyWarning.gameObject.activeSelf;

    /// <summary>E2E/検証用: 現在ペース表示の文字列。</summary>
    public string CurrentPaceText => textPace != null ? textPace.text : string.Empty;

    /// <summary>E2E/検証用: 現在ペースの判定状態(緑=維持 / 赤=遅れ)。</summary>
    public PaceHudDisplay.PaceState CurrentPaceState => _currentPaceState;

    /// <summary>
    /// F-07 周辺視野レイアウトを適用する。
    ///
    /// シーンの配置は設計書と一致していなかった(時間=右上・距離=左下・ペース=右下、
    /// さらに補助表示5件が右下に積まれ、通知が Text_Pitch と同座標で重なっていた)。
    /// 走行中に視線を這わせずに読めることが F-07 の目的なので、
    /// 補助表示を伏せ、残る3項目を規定のゾーンへ再配置する。
    ///
    /// シーンを書き換えず実行時に行うため、OFFにすれば元の配置に戻る。
    /// </summary>
    private void ApplyPeripheralLayout()
    {
        if (!peripheralModeOnly || _peripheralLayoutApplied) return;
        _peripheralLayoutApplied = true;

        // 中央を空けるため補助表示を伏せる。値の算出自体は継続する
        // (アバターのバイタル警告やリザルト集計はこれらの数値に依存しているため)
        SetReadoutVisible(textHeartRate, false);
        SetReadoutVisible(textPitch, false);
        SetReadoutVisible(textSyncRate, false);
        SetReadoutVisible(textFatigueIndex, false);
        SetReadoutVisible(textGrade, false);

        // 左上: 時間・距離 / 右上: 現在ペース
        AnchorTo(textTime,     new Vector2(0f, 1f), new Vector2(100f, -100f), TextAlignmentOptions.TopLeft);
        AnchorTo(textDistance, new Vector2(0f, 1f), new Vector2(100f, -170f), TextAlignmentOptions.TopLeft);
        AnchorTo(textPace,     new Vector2(1f, 1f), new Vector2(-100f, -100f), TextAlignmentOptions.TopRight);

        // スプリット通知は上部中央へ退避(元は右下で Text_Pitch と重なっていた)。
        // 下部は F-10 の警告専用ゾーンなので使わない
        AnchorTo(textNotificationAlert, new Vector2(0.5f, 1f), new Vector2(0f, -260f),
                 TextAlignmentOptions.Center);

        Debug.Log("[HUD] F-07 周辺視野レイアウトを適用: 補助表示5件を非表示、時間/距離=左上・ペース=右上へ再配置");
    }

    private static void SetReadoutVisible(TextMeshProUGUI text, bool visible)
    {
        if (text == null) return;
        if (text.gameObject.activeSelf != visible)
            text.gameObject.SetActive(visible);
    }

    private static void AnchorTo(TextMeshProUGUI text, Vector2 anchor, Vector2 offset,
                                 TextAlignmentOptions alignment)
    {
        if (text == null) return;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(560f, 64f);

        text.alignment = alignment;
        text.enableWordWrapping = false;
    }

    /// <summary>E2E/検証用: 表示中の補助readout数(F-07適用時は0であること)。</summary>
    public int VisibleAuxiliaryReadoutCount
    {
        get
        {
            int n = 0;
            if (textHeartRate != null && textHeartRate.gameObject.activeSelf) n++;
            if (textPitch != null && textPitch.gameObject.activeSelf) n++;
            if (textSyncRate != null && textSyncRate.gameObject.activeSelf) n++;
            if (textFatigueIndex != null && textFatigueIndex.gameObject.activeSelf) n++;
            if (textGrade != null && textGrade.gameObject.activeSelf) n++;
            return n;
        }
    }

    /// <summary>E2E/検証用: 各要素のアンカー((0,1)=左上 / (1,1)=右上)。</summary>
    public Vector2 TimeAnchor => textTime != null ? textTime.rectTransform.anchorMin : Vector2.zero;
    public Vector2 DistanceAnchor => textDistance != null ? textDistance.rectTransform.anchorMin : Vector2.zero;
    public Vector2 PaceAnchor => textPace != null ? textPace.rectTransform.anchorMin : Vector2.zero;

    private void ApplyHudAlpha(float alpha)
    {
        SetTextAlpha(textHeartRate, alpha);
        SetTextAlpha(textTime, alpha);
        SetTextAlpha(textDistance, alpha);
        SetTextAlpha(textPace, alpha);
        SetTextAlpha(textPitch, alpha);
        SetTextAlpha(textSyncRate, alpha);
        SetTextAlpha(textFatigueIndex, alpha);
        SetTextAlpha(textGrade, alpha);
    }

    private static void SetTextAlpha(TextMeshProUGUI text, float alpha)
    {
        if (text == null) return;
        Color c = text.color;
        text.color = new Color(c.r, c.g, c.b, alpha);
    }

    private void UpdateBatteryWarningFlash()
    {
        float battery = SystemInfo.batteryLevel;
        bool lowBattery = battery > 0f && battery <= 0.1f;

#if UNITY_EDITOR
        // Editor: hold Y to preview the low-battery flash
        if (Input.GetKey(KeyCode.Y)) lowBattery = true;
#endif

        if (!_hudColorCached && textTime != null)
        {
            _hudDefaultColor = textTime.color;
            _hudColorCached = true;
        }

        if (lowBattery)
        {
            _batteryFlashActive = true;
            // 2Hz ping-pong between default and warning yellow
            Color flash = Color.Lerp(_hudDefaultColor, BatteryWarningYellow, Mathf.PingPong(Time.time * 2f, 1f));
            ApplyHudTextColor(flash);
        }
        else if (_batteryFlashActive)
        {
            _batteryFlashActive = false;
            ApplyHudTextColor(_hudDefaultColor);
        }
    }

    private void ApplyHudTextColor(Color color)
    {
        if (textHeartRate != null) textHeartRate.color = color;
        if (textTime != null) textTime.color = color;
        if (textDistance != null) textDistance.color = color;
        if (textPace != null) textPace.color = color;
    }

    // 企画書 §2: 各HUDテキストに黒の細アウトラインを付与(明るい路面でも視認可能に)
    private void ApplyHighContrastOutline()
    {
        ApplyOutline(textHeartRate);
        ApplyOutline(textTime);
        ApplyOutline(textDistance);
        ApplyOutline(textPace);
        ApplyOutline(textPitch);
        ApplyOutline(textSyncRate);
        ApplyOutline(textFatigueIndex);
        ApplyOutline(textGrade);
        ApplyOutline(textNotificationAlert);
    }

    private static void ApplyOutline(TextMeshProUGUI text)
    {
        if (text == null) return;

        // アウトラインは装飾であり、これが原因で初期化を止めてはならない。
        // 実行時生成直後のTextMeshProUGUIでは TMP 内部の SetOutlineThickness が
        // NullReferenceException を投げることがあり(マテリアルインスタンスが
        // まだ生成されていない)、実際に Start() が中断して F-07レイアウト適用と
        // visualsEngine の自動解決が丸ごと飛んでいた。
        try
        {
            // fontMaterialへのアクセスでインスタンス化されるため共有マテリアルは汚さない
            text.outlineColor = new Color32(0, 0, 0, 255);
            text.outlineWidth = 0.15f; // TMPのSDF基準で約1px相当
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HUD] アウトライン適用をスキップ ({text.name}): {e.GetType().Name}");
        }
    }

    // --- THE BLE INPUT GATEWAYS ---
    public void UpdateLiveHeartRate(int realBpm)
    {
        _simulatedHeartRate = realBpm;
        if (textHeartRate != null)
        {
            textHeartRate.text = string.Format("{0} BPM", realBpm);
        }
    }

    public void UpdateLivePitch(float pitch)
    {
        _simulatedPitch = pitch;
        if (textPitch != null)
        {
            textPitch.text = string.Format("{0:F0} SPM", pitch);
        }
    }

    private void UpdateBiometricsDisplay()
    {
#if UNITY_EDITOR
        // V key: spike HR to 195 BPM for 6 seconds to exercise the vital-warning state
        if (Input.GetKeyDown(KeyCode.V))
        {
            _hrSpikeUntil = Time.time + 6.0f;
            Debug.Log("[SIMULATOR] HR spike to 195 BPM for 6s (vital warning test).");
        }

        // Only run fake jitter simulator inside the Windows/Mac Editor layout
        if (Time.frameCount % 60 == 0 || Time.time < _hrSpikeUntil)
        {
            // Simulate natural heart rate drift
            _simulatedHeartRate += Random.Range(-2, 3);
            _simulatedHeartRate = Mathf.Clamp(_simulatedHeartRate, 120, 175);

            if (Time.time < _hrSpikeUntil)
                _simulatedHeartRate = 195;

            if (textHeartRate != null)
            {
                textHeartRate.text = string.Format("{0} BPM", _simulatedHeartRate);
            }

            // Feed the simulated HR to the avatar so bio-luminescence and
            // vital warning behave in the editor exactly as with real BLE data
            if (visualsEngine != null)
            {
                visualsEngine.UpdateHeartRate(_simulatedHeartRate);
            }

            // Simulate natural runner cadence jitter between 165 and 185 SPM (Requirement 2)
            _simulatedPitch += Random.Range(-2f, 3f);
            _simulatedPitch = Mathf.Clamp(_simulatedPitch, 165f, 185f);
            if (textPitch != null)
            {
                textPitch.text = string.Format("{0:F0} SPM", _simulatedPitch);
            }
        }
#else
        // In real builds, keep static values updated unless overridden by native watch callbacks
        if (textHeartRate != null && _simulatedHeartRate > 0)
        {
            textHeartRate.text = string.Format("{0} BPM", _simulatedHeartRate);
        }
        if (textPitch != null && _simulatedPitch > 0)
        {
            textPitch.text = string.Format("{0:F0} SPM", _simulatedPitch);
        }
#endif
    }

    // --- splits notifications display coroutine ---
    private void HandleSplitReachedNotification(float kmMarker, float avgSync)
    {
        if (_splitAlertCoroutine != null)
        {
            StopCoroutine(_splitAlertCoroutine);
        }
        _splitAlertCoroutine = StartCoroutine(ExecuteSplitAlertFade(kmMarker, avgSync));
    }

    private IEnumerator ExecuteSplitAlertFade(float kmMarker, float avgSync)
    {
        if (textNotificationAlert == null) yield break;

        textNotificationAlert.text = string.Format("[SPLIT] {0}KM: Average Sync {1:F1}%!", kmMarker, avgSync);
        Color baseColor = textNotificationAlert.color;
        Transform alertTransform = textNotificationAlert.transform;
        Vector3 baseScale = Vector3.one;

        // 1. Fade In + 拡大演出 (企画書 §2 ダイナミック・フィードバック — 目標達成時の拡大)
        //    0.7倍 → 1.15倍にオーバーシュートしてから 1.0倍へ収束
        float elapsed = 0.0f;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.4f);
            textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, t);

            float overshoot = Mathf.Sin(t * Mathf.PI * 0.75f) * 0.45f; // 0→0.45→0.32付近
            alertTransform.localScale = baseScale * (0.7f + overshoot);
            yield return null;
        }
        textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1.0f);

        // 収束: 1.15倍前後 → 1.0倍
        elapsed = 0.0f;
        Vector3 fromScale = alertTransform.localScale;
        while (elapsed < 0.2f)
        {
            elapsed += Time.deltaTime;
            alertTransform.localScale = Vector3.Lerp(fromScale, baseScale, elapsed / 0.2f);
            yield return null;
        }
        alertTransform.localScale = baseScale;

        // 2. High-intensity overlay wait block (2.2 seconds)
        yield return new WaitForSeconds(2.2f);

        // 3. Fade Out over 0.4 seconds
        elapsed = 0.0f;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1.0f - (elapsed / 0.4f));
            yield return null;
        }
        textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.0f);
    }
}
