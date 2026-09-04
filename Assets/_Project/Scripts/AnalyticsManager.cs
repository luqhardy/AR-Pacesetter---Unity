using UnityEngine;

public class AnalyticsManager : MonoBehaviour
{
    [Header("Tracking Parameters")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private Transform avatarContainer;  // Legacy scene link; never used as the runner metric
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private RunnerTrackingState runnerTracking;
    [SerializeField] private ARSessionManagerBridge sessionBridge;

    [Header("Environment Configuration")]
    [Range(15f, 40f)]
    [SerializeField] private float ambientTemperatureCelsius = 25.0f; // Fed from smartphone weather API

    // Internal scoring aggregates (Requirement 4.3)
    private float _totalSyncSum = 0.0f;
    private int   _totalSyncCount = 0;
    private float _currentKmSyncSum = 0.0f;
    private int   _currentKmSyncCount = 0;

    private float _lastEvaluatedKilometerMarker = 0.0f;
    private float _cumulativeFatigueIndex = 0.0f;

    // Pace-derived synchronicity state. The avatar intentionally remains about
    // 3m ahead, so its transform must never be treated as runner deviation.
    private const float InternalSpeedSmoothingSeconds = 1.0f;
    private const float MaximumHumanSpeedMetersPerSecond = 15.0f;
    private const float MaximumIntegrationStepSeconds = 0.25f;
    private Vector3 _lastUserPosition;
    private bool _hasUserPositionSample;
    private bool _hasActualSpeedSample;
    private float _smoothedInternalSpeedMetersPerSecond;
    private float _paceDistanceDeviationMeters;
    private float _liveSyncRate;

    // Splits Alert Event (Requirement 4.3)
    public delegate void SplitReachedDelegate(float kmMarker, float avgSync);
    public event SplitReachedDelegate OnSplitReached;

    public float AmbientTemperature
    {
        get => ambientTemperatureCelsius;
        set => ambientTemperatureCelsius = value;
    }

    void Start()
    {
        if (avatarEngine == null && avatarContainer != null)
        {
            avatarEngine = avatarContainer.GetComponent<AvatarEngine>();
        }
        if (avatarEngine == null)
        {
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        }
        if (sessionBridge == null)
        {
            sessionBridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
        }
        if (runnerTracking == null)
        {
            runnerTracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
        }
        ResetPositionSample();
    }

    void Update()
    {
        if (userCamera == null) return;
        if (avatarEngine == null || !avatarEngine.IsRunMotionActive) return;

        // Requirement 4.3: compare the target pace to the runner's measured
        // pace. Fresh CoreLocation data wins; editor/standalone runs fall back
        // to smoothed horizontal XR-camera speed.
        if (!TryGetActualSpeed(out float actualSpeedMetersPerSecond))
            return; // Do not grade the sensor warm-up period as a failed run.

        float targetSpeedMetersPerSecond =
            PaceSynchronicityMath.PaceToMetersPerSecond(avatarEngine.TargetPaceMinutesPerKm);
        float integrationStep = Mathf.Clamp(Time.deltaTime, 0f, MaximumIntegrationStepSeconds);
        _paceDistanceDeviationMeters = PaceSynchronicityMath.AccumulateDistanceDeviation(
            _paceDistanceDeviationMeters,
            targetSpeedMetersPerSecond,
            actualSpeedMetersPerSecond,
            integrationStep);

        _liveSyncRate = PaceSynchronicityMath.CalculateSyncPercent(
            targetSpeedMetersPerSecond,
            actualSpeedMetersPerSecond,
            _paceDistanceDeviationMeters);

        // Update aggregates instead of adding to a list (Fix: Memory Bloat)
        _totalSyncSum += _liveSyncRate;
        _totalSyncCount++;
        _currentKmSyncSum += _liveSyncRate;
        _currentKmSyncCount++;

        // 2. Compute Temperature-Compensated Fatigue Index (Requirement 4.3)
        CalculateDynamicFatigue(_liveSyncRate);
    }

    private void CalculateDynamicFatigue(float syncRate)
    {
        // Establish baseline fatigue accumulation per frame
        float baselineFatigue = (100.0f - syncRate) * 0.01f * Time.deltaTime;

        // Apply technical specifications for hyperthermic environment modifiers
        float temperatureCorrectionCoefficient = GetFatigueMultiplier();

        // Apply final weighted metrics to our aggregate metric vector
        _cumulativeFatigueIndex += baselineFatigue * temperatureCorrectionCoefficient;
    }

    // Public validation checkpoint triggered by your telemetry system
    public void CheckDistanceIntervalSplits(float totalDistanceTraveledMeters)
    {
        float totalKilometers = totalDistanceTraveledMeters / 1000f;

        // Requirement 4.3: Audit metrics at every 1km mark
        if (totalKilometers - _lastEvaluatedKilometerMarker >= 1.0f)
        {
            _lastEvaluatedKilometerMarker = Mathf.Floor(totalKilometers);
            
            float averageSyncForThisKm = _currentKmSyncCount > 0 
                ? _currentKmSyncSum / _currentKmSyncCount 
                : 0f;

            // Reset window for next km
            _currentKmSyncSum = 0.0f;
            _currentKmSyncCount = 0;

            Debug.Log($"[SPLIT ALERT] 1KM Mark Reached. Current Kilometer Sync Rate: {averageSyncForThisKm:F1}%");

            // Trigger the split event for HUD display
            OnSplitReached?.Invoke(_lastEvaluatedKilometerMarker, averageSyncForThisKm);

            // Requirement 4.3: Evaluate extended clusters at every 5km mark
            if (Mathf.Approximately(_lastEvaluatedKilometerMarker % 5.0f, 0.0f))
            {
                Debug.Log($"[MACRO SPLIT] 5KM Block Completed. Commencing structural telemetry optimization...");
            }
        }
    }

    /// <summary>再走行対応: 集計値をすべて初期化する。</summary>
    public void ResetSession()
    {
        _totalSyncSum = 0.0f;
        _totalSyncCount = 0;
        _currentKmSyncSum = 0.0f;
        _currentKmSyncCount = 0;
        _lastEvaluatedKilometerMarker = 0.0f;
        _cumulativeFatigueIndex = 0.0f;
        _paceDistanceDeviationMeters = 0.0f;
        _liveSyncRate = 0.0f;
        _smoothedInternalSpeedMetersPerSecond = 0.0f;
        _hasActualSpeedSample = false;
        ResetPositionSample();
    }

    public float GetSessionAverageSync()
    {
        if (_totalSyncCount == 0) return 0f;
        return _totalSyncSum / _totalSyncCount;
    }

    public string EvaluateFinalSessionPerformanceRank()
    {
        if (_totalSyncCount == 0) return "D";

        // Calculate overarching mean performance rating
        float totalAverageSync = _totalSyncSum / _totalSyncCount;

        // Section 4.3 Ranking Matrix Evaluation (S ~ D)
        if (totalAverageSync >= 90.0f) return "S";
        if (totalAverageSync >= 80.0f) return "A"; // KPI Target: Keep above 80%
        if (totalAverageSync >= 65.0f) return "B";
        if (totalAverageSync >= 50.0f) return "C";

        return "D"; // Low compliance bounds
    }

    private bool TryGetActualSpeed(out float actualSpeedMetersPerSecond)
    {
        // The invisible RunnerTrackingState is the preferred shared source for
        // GPS and ARKit/IMU motion. This keeps scoring, avatar heading and future
        // demo simulation on the same representation of the physical runner.
        if (runnerTracking != null)
        {
            if (runnerTracking.HasValidSpeedMeasurement)
            {
                // A genuinely fresh CoreLocation sample with 0km/h is a valid
                // stopped-runner reading, not "missing". Cached timer messages do
                // not refresh HasValidSpeedMeasurement in RunnerTrackingState.
                actualSpeedMetersPerSecond =
                    Mathf.Max(0f, runnerTracking.ExternalSpeedKmH) / 3.6f;
                _hasActualSpeedSample = true;
                return true;
            }

            actualSpeedMetersPerSecond = runnerTracking.CurrentSpeedKmH / 3.6f;
            if (actualSpeedMetersPerSecond >= 0.2f)
                _hasActualSpeedSample = true;
            return _hasActualSpeedSample;
        }

        // Backward-compatible fallback for scenes that predate
        // RunnerTrackingState.
        float externalPaceKilometersPerHour = sessionBridge != null
            ? sessionBridge.MeasuredPaceKmH
            : 0f;

        // Keep the XR baseline current even while external pace is authoritative,
        // preventing a large fallback delta if CoreLocation temporarily expires.
        float internalSpeed = SampleInternalSpeed();

        if (externalPaceKilometersPerHour > 0f
            && !float.IsNaN(externalPaceKilometersPerHour)
            && !float.IsInfinity(externalPaceKilometersPerHour))
        {
            actualSpeedMetersPerSecond = externalPaceKilometersPerHour / 3.6f;
            _hasActualSpeedSample = true;
            return true;
        }

        // Before the first credible movement sample, zero means "not measured"
        // rather than "runner stopped". Once movement has been seen, zero is a
        // legitimate stopped-runner sample and should produce 0% sync.
        if (!_hasActualSpeedSample && internalSpeed >= 0.2f)
            _hasActualSpeedSample = true;

        actualSpeedMetersPerSecond = internalSpeed;
        return _hasActualSpeedSample;
    }

    public float GetLiveSyncRate()
    {
        return _liveSyncRate;
    }

    /// <summary>
    /// Signed pace-derived gap: negative is behind target, positive is ahead.
    /// Exposed for diagnostics/demo telemetry; it is unrelated to avatar lead.
    /// </summary>
    public float PaceDistanceDeviationMeters => _paceDistanceDeviationMeters;

    private float SampleInternalSpeed()
    {
        if (userCamera == null)
            return 0f;

        Vector3 current = userCamera.position;
        current.y = 0f;
        if (!_hasUserPositionSample)
        {
            _lastUserPosition = current;
            _hasUserPositionSample = true;
            return _smoothedInternalSpeedMetersPerSecond;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float distance = Vector3.Distance(current, _lastUserPosition);
        _lastUserPosition = current;

        float instantaneousSpeed = distance / dt;
        // Tracking relocalisation can teleport the XR origin. Ignore that sample
        // instead of interpreting it as impossible running speed.
        if (instantaneousSpeed > MaximumHumanSpeedMetersPerSecond
            || float.IsNaN(instantaneousSpeed)
            || float.IsInfinity(instantaneousSpeed))
        {
            instantaneousSpeed = _smoothedInternalSpeedMetersPerSecond;
        }

        float smoothing = 1f - Mathf.Exp(-dt / InternalSpeedSmoothingSeconds);
        _smoothedInternalSpeedMetersPerSecond +=
            (instantaneousSpeed - _smoothedInternalSpeedMetersPerSecond) * smoothing;
        return Mathf.Max(0f, _smoothedInternalSpeedMetersPerSecond);
    }

    private void ResetPositionSample()
    {
        _hasUserPositionSample = userCamera != null;
        if (userCamera != null)
        {
            _lastUserPosition = userCamera.position;
            _lastUserPosition.y = 0f;
        }
    }

    public float GetCumulativeFatigue()
    {
        return _cumulativeFatigueIndex;
    }

    public float GetFatigueMultiplier()
    {
        if (ambientTemperatureCelsius >= 31.0f)
        {
            return 2.0f; // 2.0x scaling at 31C or above
        }
        else if (ambientTemperatureCelsius >= 28.0f)
        {
            return 1.5f; // 1.5x scaling at 28C or above
        }
        return 1.0f;
    }
}
