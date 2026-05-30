using UnityEngine;
using TMPro; // Essential for modern high-performance text rendering

public class PeripheralHUDManager : MonoBehaviour
{
    [Header("UI Text Components")]
    [SerializeField] private TextMeshProUGUI textHeartRate;
    [SerializeField] private TextMeshProUGUI textTime;
    [SerializeField] private TextMeshProUGUI textDistance;
    [SerializeField] private TextMeshProUGUI textPace;

    [Header("Engine Links")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private AvatarEngine avatarEngine;   // For fetching target speed
    [SerializeField] private AnalyticsManager analytics; // For tracking split alerts

    // Telemetry tracking state variables
    private float _elapsedTimeSeconds = 0.0f;
    private float _cumulativeDistanceMeters = 0.0f;
    private Vector3 _lastUserPosition;
    private int _simulatedHeartRate = 135; // Mock baseline for editor testing

    void Start()
    {
        if (userCamera != null)
        {
            _lastUserPosition = userCamera.position;
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

        // 4. Run background simulation if inside editor
        UpdateHeartRateDisplay();
    }

    // --- THE BLE INPUT GATEWAY ---
    // This receives the live data package routed from the HeartRateReceiver script
    public void UpdateLiveHeartRate(int realBpm)
    {
        _simulatedHeartRate = realBpm;
        if (textHeartRate != null)
        {
            textHeartRate.text = string.Format("{0} BPM", realBpm);
        }
    }

    private void UpdateHeartRateDisplay()
    {
        if (textHeartRate == null) return;

#if UNITY_EDITOR
        // Only run fake jitter logic inside the Windows Editor layout
        if (Time.frameCount % 60 == 0)
        {
            _simulatedHeartRate += Random.Range(-2, 3);
            _simulatedHeartRate = Mathf.Clamp(_simulatedHeartRate, 120, 175);
        }
        textHeartRate.text = string.Format("{0} BPM", _simulatedHeartRate);
#endif
    }
}