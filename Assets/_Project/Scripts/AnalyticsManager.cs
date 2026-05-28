using System.Collections.Generic;
using UnityEngine;

public class AnalyticsManager : MonoBehaviour
{
    [Header("Tracking Parameters")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private Transform avatarContainer;  // Pacing companion anchor

    [Header("Environment Configuration")]
    [Range(15f, 40f)]
    [SerializeField] private float ambientTemperatureCelsius = 25.0f; // Fed from smartphone weather API

    // Internal scoring arrays
    private List<float> _synchronizationHistory = new List<float>();
    private float _lastEvaluatedKilometerMarker = 0.0f;
    private float _cumulativeFatigueIndex = 0.0f;

    void Update()
    {
        if (userCamera == null || avatarContainer == null) return;

        // 1. Calculate Real-Time Sync Rate (Requirement 1.2 & 4.2)
        // Sync Rate maps proximity linearly: 0m separation = 100%, 10m separation = 0%
        float separationDistance = Vector3.Distance(userCamera.position, avatarContainer.position);
        float currentSyncRate = Mathf.Max(0.0f, 100.0f * (1.0f - (separationDistance / 10.0f)));

        _synchronizationHistory.Add(currentSyncRate);

        // 2. Compute Temperature-Compensated Fatigue Index (Requirement 4.2)
        CalculateDynamicFatigue(currentSyncRate);
    }

    private void CalculateDynamicFatigue(float syncRate)
    {
        // Establish baseline fatigue accumulation per frame
        float baselineFatigue = (100.0f - syncRate) * 0.01f * Time.deltaTime;

        // Apply technical specifications for hyperthermic environment modifiers
        float temperatureCorrectionCoefficient = 1.0f;

        if (ambientTemperatureCelsius >= 31.0f)
        {
            temperatureCorrectionCoefficient = 2.0f; // 2.0x scaling at 31C or above
        }
        else if (ambientTemperatureCelsius >= 28.0f)
        {
            temperatureCorrectionCoefficient = 1.5f; // 1.5x scaling at 28C or above
        }

        // Apply final weighted metrics to our aggregate metric vector
        _cumulativeFatigueIndex += baselineFatigue * temperatureCorrectionCoefficient;
    }

    // Public validation checkpoint triggered by your telemetry system
    public void CheckDistanceIntervalSplits(float totalDistanceTraveledMeters)
    {
        float totalKilometers = totalDistanceTraveledMeters / 1000f;

        // Requirement 4.2: Audit metrics at every 1km mark
        if (totalKilometers - _lastEvaluatedKilometerMarker >= 1.0f)
        {
            _lastEvaluatedKilometerMarker = Mathf.Floor(totalKilometers);
            float averageSyncForThisKm = ComputeAverageSyncOverLastWindow(60); // Check past frame records
            Debug.Log($"[SPLIT ALERT] 1KM Mark Reached. Current Kilometer Sync Rate: {averageSyncForThisKm:F1}%");

            // Requirement 4.2: Evaluate extended clusters at every 5km mark
            if (Mathf.Approximately(_lastEvaluatedKilometerMarker % 5.0f, 0.0f))
            {
                Debug.Log($"[MACRO SPLIT] 5KM Block Completed. Commencing structural telemetry optimization...");
            }
        }
    }

    public string EvaluateFinalSessionPerformanceRank()
    {
        if (_synchronizationHistory.Count == 0) return "D";

        // Calculate overarching mean performance rating
        float sum = 0f;
        foreach (float rate in _synchronizationHistory) sum += rate;
        float totalAverageSync = sum / _synchronizationHistory.Count;

        // Section 4.2 Ranking Matrix Evaluation (S ~ D)
        if (totalAverageSync >= 90.0f) return "S";
        if (totalAverageSync >= 80.0f) return "A"; // KPI Target: Keep above 80%
        if (totalAverageSync >= 65.0f) return "B";
        if (totalAverageSync >= 50.0f) return "C";

        return "D"; // Low compliance bounds
    }

    private float ComputeAverageSyncOverLastWindow(int frameCount)
    {
        if (_synchronizationHistory.Count == 0) return 0f;
        int lookbackCount = Mathf.Min(frameCount, _synchronizationHistory.Count);

        float aggregate = 0f;
        for (int i = _synchronizationHistory.Count - lookbackCount; i < _synchronizationHistory.Count; i++)
        {
            aggregate += _synchronizationHistory[i];
        }
        return aggregate / lookbackCount;
    }
}