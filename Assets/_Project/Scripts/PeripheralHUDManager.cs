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

    // Telemetry tracking state variables
    private float _elapsedTimeSeconds = 0.0f;
    private float _cumulativeDistanceMeters = 0.0f;
    private Vector3 _lastUserPosition;
    
    // Biometric mock baseline metrics for editor simulation
    private int _simulatedHeartRate = 135;
    private float _simulatedPitch = 172.0f;
    private Coroutine _splitAlertCoroutine;

    void Start()
    {
        if (userCamera != null)
        {
            _lastUserPosition = userCamera.position;
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

        // 1. Calculate Runtime Clock (Format: MM:SS)
        _elapsedTimeSeconds += Time.deltaTime;
        int minutes = Mathf.FloorToInt(_elapsedTimeSeconds / 60f);
        int seconds = Mathf.FloorToInt(_elapsedTimeSeconds % 60f);
        if (textTime != null)
        {
            textTime.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        // 2. Track Cumulative Distance Covered (Meters -> Kilometers)
        float frameMovementDistance = Vector3.Distance(userCamera.position, _lastUserPosition);
        if (frameMovementDistance > 0.01f)
        {
            _cumulativeDistanceMeters += frameMovementDistance;
            _lastUserPosition = userCamera.position;

            // Pass the distance directly to the analytics manager for split testing
            if (analytics != null)
            {
                analytics.CheckDistanceIntervalSplits(_cumulativeDistanceMeters);
            }
        }

        if (textDistance != null)
        {
            float totalKm = _cumulativeDistanceMeters / 1000f;
            textDistance.text = string.Format("{0:F2} km", totalKm);
        }

        // 3. Dynamic Performance Pace Formatting
        if (textPace != null && avatarEngine != null)
        {
            textPace.text = "Target 5:00/km";
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
        // Only run fake jitter simulator inside the Windows/Mac Editor layout
        if (Time.frameCount % 60 == 0)
        {
            // Simulate natural heart rate drift
            _simulatedHeartRate += Random.Range(-2, 3);
            _simulatedHeartRate = Mathf.Clamp(_simulatedHeartRate, 120, 175);
            if (textHeartRate != null)
            {
                textHeartRate.text = string.Format("{0} BPM", _simulatedHeartRate);
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
