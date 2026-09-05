using System;
using System.Collections;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Editor/development-only end-to-end demonstration of the invisible POV runner.
///
/// The physical runner is intentionally not rendered. In the real product ARKit/CoreLocation
/// represents that runner; this component feeds the same public Swift-to-Unity command boundary
/// with deterministic pace, heart-rate, GPS and distance samples. The existing avatar, HUD,
/// goal-line and session-result systems therefore run without a second "user avatar".
///
/// This component is never auto-created by the production bootstrap. Use its context menu or
/// Tools > AR Pacesetter > POV Demo while the Editor is in Play Mode.
/// </summary>
[DisallowMultipleComponent]
public sealed class PovRunnerDemoController : MonoBehaviour
{
    private const double MetersPerLatitudeDegree = 111_111.0;

    [Header("Demo Route")]
    [Tooltip("Short virtual route length. 60m leaves enough time to see the goal appear.")]
    [SerializeField, Min(10f)] private float routeDistanceMeters = 60f;
    [Tooltip("Pace delivered at the same km/h bridge boundary used by Swift.")]
    [SerializeField, Range(4f, 25f)] private float paceKmH = 12f;
    [Tooltip("Accelerates virtual distance while keeping reported pace realistic. Demo-only.")]
    [SerializeField, Range(0.25f, 10f)] private float playbackMultiplier = 2f;
    [SerializeField, Range(1f, 10f)] private float forwardOffsetMeters = 3f;

    [Header("Demo Telemetry")]
    [SerializeField, Range(60, 220)] private int startingHeartRate = 132;
    [SerializeField, Range(60, 220)] private int finishingHeartRate = 154;
    [SerializeField, Range(0.05f, 1f)] private float metricIntervalSeconds = 0.2f;
    [Tooltip("Safety timeout while waiting for AvatarEngine's authoritative START transition.")]
    [SerializeField, Range(0f, 8f)] private float countdownLeadInSeconds = 4.05f;
    [SerializeField] private double startLatitude = 35.681236;
    [SerializeField] private double startLongitude = 139.767125;
    [SerializeField, Range(1f, 50f)] private float gpsAccuracyMeters = 3f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private ARSessionManagerBridge sessionBridge;

    private Coroutine _demoRoutine;
    private bool _isRunning;
    private float _simulatedDistanceMeters;

    public bool IsRunning => _isRunning;
    public float SimulatedDistanceMeters => _simulatedDistanceMeters;
    public float RouteDistanceMeters => routeDistanceMeters;
    public float Progress01 => routeDistanceMeters > 0f
        ? Mathf.Clamp01(_simulatedDistanceMeters / routeDistanceMeters)
        : 0f;

    /// <summary>Starts the configured deterministic route through the real bridge flow.</summary>
    [ContextMenu("POV Demo/Start Automatic Short Run")]
    public void StartDemo()
    {
        if (!IsDemoBuild())
        {
            Debug.LogWarning("[POV DEMO] Disabled in production builds.");
            return;
        }

        if (_isRunning)
        {
            Debug.LogWarning("[POV DEMO] A demo route is already running.");
            return;
        }

        ResolveBridge();
        if (sessionBridge == null)
        {
            Debug.LogError("[POV DEMO] ARSessionManagerBridge was not found. Open SampleScene and enter Play Mode first.");
            return;
        }

        AvatarEngine engine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (engine != null && engine.HasStarted && !engine.IsSessionEnded)
        {
            Debug.LogWarning("[POV DEMO] An active run already exists. Finish it before starting the automatic demo.");
            return;
        }

        _demoRoutine = StartCoroutine(RunDemoRoute());
    }

    /// <summary>Ends an active demo using the same EndSession command sent by Swift.</summary>
    [ContextMenu("POV Demo/Stop and End Session")]
    public void StopDemo()
    {
        if (!_isRunning)
            return;

        if (_demoRoutine != null)
            StopCoroutine(_demoRoutine);
        _demoRoutine = null;
        _isRunning = false;

        ResolveBridge();
        if (sessionBridge != null)
            sessionBridge.OnSwiftCommand("{\"command\":\"EndSession\"}");

        Debug.Log($"[POV DEMO] Stopped at {_simulatedDistanceMeters:F1}m.");
    }

    /// <summary>Convenience shortcut for checking the finish VFX without waiting.</summary>
    [ContextMenu("POV Demo/Reach Goal Now")]
    public void ReachGoalNow()
    {
        if (!_isRunning)
        {
            Debug.LogWarning("[POV DEMO] Start the automatic demo before jumping to its goal.");
            return;
        }

        if (_demoRoutine != null)
            StopCoroutine(_demoRoutine);
        _demoRoutine = null;

        _simulatedDistanceMeters = Mathf.Max(10f, routeDistanceMeters);
        SendMetric(_simulatedDistanceMeters, 1f);
        _isRunning = false;
        Debug.Log($"[POV DEMO] Goal reached at {_simulatedDistanceMeters:F1}m.");
    }

    private IEnumerator RunDemoRoute()
    {
        _isRunning = true;
        _simulatedDistanceMeters = 0f;

        float safeRouteMeters = Mathf.Max(10f, routeDistanceMeters);
        float safePaceKmH = Mathf.Clamp(paceKmH, 4f, 25f);
        float safePlayback = Mathf.Clamp(playbackMultiplier, 0.25f, 10f);

        // Keep the closing JSON brace outside String.Format. Some Unity/.NET
        // runtimes parse the adjacent `:F2}}}` sequence as a custom numeric
        // format and emit the literal value "F2", producing invalid JSON.
        string startCommand = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"command\":\"StartSession\",\"targetPaceKmH\":{0:F3},\"distanceKm\":{1:F6}," +
            "\"avatarHeightCm\":175,\"forwardOffsetM\":{2:F2}",
            safePaceKmH, safeRouteMeters / 1000f, Mathf.Clamp(forwardOffsetMeters, 1f, 10f)) + "}";
        sessionBridge.OnSwiftCommand(startCommand);

        Debug.Log($"[POV DEMO] Invisible runner started: {safeRouteMeters:F0}m at {safePaceKmH:F1}km/h " +
                  $"({safePlayback:F1}x demo playback). No user model is expected.");

        AvatarEngine engine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        float startWaitRemaining = Mathf.Max(0.1f, countdownLeadInSeconds) + 1f;
        while (_isRunning && engine != null && !engine.IsRunMotionActive
               && startWaitRemaining > 0f)
        {
            startWaitRemaining -= Time.deltaTime;
            yield return null;
        }

        if (!_isRunning)
            yield break;

        if (engine != null && !engine.IsRunMotionActive)
        {
            _isRunning = false;
            _demoRoutine = null;
            Debug.LogError("[POV DEMO] Timed out waiting for 3-2-1-START; no distance was simulated.");
            yield break;
        }

        float metersPerSecond = safePaceKmH / 3.6f;
        float virtualElapsed = 0f;
        float nextMetricAt = 0f;
        float interval = Mathf.Clamp(metricIntervalSeconds, 0.05f, 1f);
        bool approachLogged = false;

        while (_isRunning && _simulatedDistanceMeters < safeRouteMeters)
        {
            virtualElapsed += Mathf.Max(0f, Time.deltaTime) * safePlayback;
            _simulatedDistanceMeters = Mathf.Min(
                safeRouteMeters, metersPerSecond * virtualElapsed);

            if (Time.time >= nextMetricAt)
            {
                nextMetricAt = Time.time + interval;
                SendMetric(_simulatedDistanceMeters, _simulatedDistanceMeters / safeRouteMeters);
            }

            if (!approachLogged && safeRouteMeters - _simulatedDistanceMeters <= 25f)
            {
                approachLogged = true;
                Debug.Log("[POV DEMO] Final 25m — the AR finish gate should now be visible ahead.");
            }

            yield return null;
        }

        if (_isRunning)
        {
            // Send one exact final sample so floating-point accumulation cannot miss the goal.
            _simulatedDistanceMeters = safeRouteMeters;
            SendMetric(safeRouteMeters, 1f);
            Debug.Log($"[POV DEMO] Completed {safeRouteMeters:F0}m — existing goal/session flow handled the finish.");
        }

        _isRunning = false;
        _demoRoutine = null;
    }

    private void SendMetric(float distanceMeters, float progress01)
    {
        ResolveBridge();
        if (sessionBridge == null)
            return;

        float progress = Mathf.Clamp01(progress01);
        int heartRate = Mathf.RoundToInt(Mathf.Lerp(startingHeartRate, finishingHeartRate, progress));
        // Small deterministic variation reads naturally without making tests non-repeatable.
        heartRate += Mathf.RoundToInt(Mathf.Sin(progress * Mathf.PI * 6f) * 2f);

        // A due-north synthetic route. These coordinates are telemetry only; no visible user
        // GameObject is created or moved.
        double latitude = startLatitude + distanceMeters / MetersPerLatitudeDegree;
        string json = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"command\":\"UpdateMetrics\",\"paceKmH\":{0:F3},\"heartRate\":{1}," +
            "\"distanceKm\":{2:F6},\"gpsLatitude\":{3:F7},\"gpsLongitude\":{4:F7}," +
            "\"gpsAccuracy\":{5:F2},\"locationSampleFresh\":true," +
            "\"speedSampleValid\":true}}",
            Mathf.Clamp(paceKmH, 4f, 25f), heartRate, Math.Max(0f, distanceMeters) / 1000.0,
            latitude, startLongitude, Mathf.Max(1f, gpsAccuracyMeters));
        sessionBridge.OnSwiftCommand(json);
    }

    private void ResolveBridge()
    {
        if (sessionBridge == null)
            sessionBridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
    }

    private static bool IsDemoBuild()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    private void OnDisable()
    {
        // Do not silently finish/save a run when scripts recompile or the object is destroyed.
        if (_demoRoutine != null)
            StopCoroutine(_demoRoutine);
        _demoRoutine = null;
        _isRunning = false;
    }
}
