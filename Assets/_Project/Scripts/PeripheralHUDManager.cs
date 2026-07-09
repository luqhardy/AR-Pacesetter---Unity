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

    // Session read access for the result/stop flow
    public float ElapsedTimeSeconds => _elapsedTimeSeconds;
    public float DistanceMeters => _cumulativeDistanceMeters;
    public int CurrentHeartRate => _simulatedHeartRate;

    /// <summary>再走行対応: タイム・距離の累積をリセットする。</summary>
    public void ResetSession()
    {
        _elapsedTimeSeconds = 0.0f;
        _cumulativeDistanceMeters = 0.0f;
        if (userCamera != null)
            _lastUserPosition = userCamera.position;
    }

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
        if (runInProgress)
            _elapsedTimeSeconds += Time.deltaTime;
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

            // Pass the distance directly to the analytics manager for split testing
            if (analytics != null)
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

        // 1. Fade In over 0.4 seconds
        float elapsed = 0.0f;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, elapsed / 0.4f);
            yield return null;
        }
        textNotificationAlert.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1.0f);

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
