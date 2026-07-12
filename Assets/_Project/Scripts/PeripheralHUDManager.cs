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
        
        // Always update tracking position to prevent accumulation of ignored deltas or jumps
        _lastUserPosition = userCamera.position;

        if (textDistance != null)
        {
            float totalKm = _cumulativeDistanceMeters / 1000f;
            textDistance.text = string.Format("{0:F2} km", totalKm);
        }

        // 3. Dynamic Performance Pace Formatting
        if (textPace != null && avatarEngine != null)
        {
            float targetPace = avatarEngine.TargetPaceMinutesPerKm;
            int paceMin = Mathf.FloorToInt(targetPace);
            int paceSec = Mathf.FloorToInt((targetPace - paceMin) * 60f);
            textPace.text = string.Format("Target {0}:{1:00}/km", paceMin, paceSec);
        }

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
        // fontMaterialへのアクセスでインスタンス化されるため共有マテリアルは汚さない
        text.outlineColor = new Color32(0, 0, 0, 255);
        text.outlineWidth = 0.15f; // TMPのSDF基準で約1px相当
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
