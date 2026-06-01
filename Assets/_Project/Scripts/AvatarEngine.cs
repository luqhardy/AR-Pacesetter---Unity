using System.Runtime.InteropServices;
using UnityEngine;

public class AvatarEngine : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera; // XR Origin Main Camera

    [Header("Pacing Settings")]
    [Tooltip("Target running pace in minutes per kilometer (e.g. 5.0 = 5:00/km)")]
    [SerializeField] private float targetPaceMinutesPerKm = 5.0f;
    [SerializeField] private float leadDistanceMeters = 3.0f;
    [SerializeField] private float accelerationCatchupSpeed = 2.5f;

    // Native C++ Plugin Bridge
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void InitKalmanFilter(float processNoise, float measurementNoise, float lteWeight);

    [DllImport("__Internal")]
    private static extern void UpdateKalmanFilter(float rawX, float rawY, float rawZ,
        out float smoothX, out float smoothY, out float smoothZ);

    private bool _isKalmanInitialized = false;
#endif

    private Vector3 _targetPacingPosition;
    private float _calculatedTargetSpeedMetersPerSecond;

    private Vector3 _lastFrameUserPosition;
    private Vector3 _currentLinearDirection = Vector3.forward;

    // ── Vector_Forward Purification buffers (AGENTS.md §4.1) ────────────────
    private struct MovementFrame
    {
        public Vector3 delta;
        public float time;
        public MovementFrame(Vector3 d, float t) { delta = d; time = t; }
    }
    private System.Collections.Generic.Queue<MovementFrame> _movementHistory
        = new System.Collections.Generic.Queue<MovementFrame>();
    private System.Collections.Generic.Queue<Vector3> _headingHistory
        = new System.Collections.Generic.Queue<Vector3>();

    // ── Jitter Guard (AGENTS.md §3 — ±5 ms jitter tolerance) ────────────────
    private float _lastFrameDeltaTime = 0.0f;
    private Vector3 _lastCleanKalmanVelocity = Vector3.zero; // fallback velocity on spike frames
    private const float JitterThresholdSeconds = 0.005f;     // 5 ms

    // ── Public state ────────────────────────────────────────────────────────
    public bool IsHalted { get; set; } = false;

    // ────────────────────────────────────────────────────────────────────────
    void Start()
    {
        if (userCamera != null)
        {
            _lastFrameUserPosition = userCamera.position;
            _currentLinearDirection = userCamera.forward;
            _currentLinearDirection.y = 0;
            _currentLinearDirection.Normalize();
            _targetPacingPosition = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
        }

        CalculateVelocityMatrix(targetPaceMinutesPerKm);

#if UNITY_IOS && !UNITY_EDITOR
        InitKalmanFilter(0.05f, 0.8f, 0.12f);
        _isKalmanInitialized = true;
#endif
    }

    void Update()
    {
        if (userCamera == null) return;

        // ── Cliff/Obstacle halting (AGENTS.md §4.2) ─────────────────────────
        if (IsHalted)
        {
            Vector3 dirToUser = (userCamera.position - transform.position);
            dirToUser.y = 0;
            if (dirToUser != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dirToUser.normalized), Time.deltaTime * 5.0f);
            _lastFrameUserPosition = userCamera.position;
            _lastFrameDeltaTime = Time.deltaTime;
            return;
        }

        // ── Jitter Guard (AGENTS.md §3) ──────────────────────────────────────
        float frameDeltaDrift = Mathf.Abs(Time.deltaTime - _lastFrameDeltaTime);
        bool jitterSpikeDetected = (_lastFrameDeltaTime > 0f) && (frameDeltaDrift > JitterThresholdSeconds);

        if (jitterSpikeDetected)
        {
            // Discard raw measurements — advance using last clean Kalman velocity
            Debug.LogWarning($"[JITTER GUARD] Frame-delta spike: {frameDeltaDrift * 1000f:F2}ms. Using Kalman prediction.");
            _targetPacingPosition += _lastCleanKalmanVelocity * Time.deltaTime;
            transform.position = _targetPacingPosition;
            _lastFrameDeltaTime = Time.deltaTime;
            _lastFrameUserPosition = userCamera.position;
            return;
        }

        // ── Vector_Forward Purification (AGENTS.md §4.1) ────────────────────
        Vector3 movementDelta = userCamera.position - _lastFrameUserPosition;
        movementDelta.y = 0;

        _movementHistory.Enqueue(new MovementFrame(movementDelta, Time.time));
        while (_movementHistory.Count > 0 && Time.time - _movementHistory.Peek().time > 1.5f)
            _movementHistory.Dequeue();

        Vector3 integratedGPS = Vector3.zero;
        foreach (var frame in _movementHistory) integratedGPS += frame.delta;

        Vector3 trueDir = _currentLinearDirection;
        if (integratedGPS.magnitude > 0.02f)
        {
            trueDir = integratedGPS.normalized;
            _currentLinearDirection = trueDir;
        }
        else if (_currentLinearDirection == Vector3.zero)
        {
            trueDir = userCamera.forward;
            trueDir.y = 0;
            trueDir.Normalize();
            _currentLinearDirection = trueDir;
        }

        // Moving Average tremor filter — 15-frame heading smoothing
        _headingHistory.Enqueue(trueDir);
        while (_headingHistory.Count > 15) _headingHistory.Dequeue();

        Vector3 headingSum = Vector3.zero;
        foreach (var h in _headingHistory) headingSum += h;
        if (headingSum != Vector3.zero) trueDir = headingSum.normalized;

        // ── Compute filtered anchor position ─────────────────────────────────
        Vector3 rawAnchor = userCamera.position + (trueDir * leadDistanceMeters);
        Vector3 filtered  = SmoothSpatialData(rawAnchor);

        // Track Kalman velocity for jitter-fallback use next frame
        _lastCleanKalmanVelocity = (filtered - _targetPacingPosition) / Mathf.Max(Time.deltaTime, 0.001f);

        _targetPacingPosition = Vector3.Lerp(_targetPacingPosition, filtered, Time.deltaTime * accelerationCatchupSpeed);

        transform.position = _targetPacingPosition;
        if (trueDir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(trueDir);

        _lastFrameUserPosition  = userCamera.position;
        _lastFrameDeltaTime     = Time.deltaTime;
    }

    // ── Public API ───────────────────────────────────────────────────────────
    public void UpdateTargetPace(float newPaceMinutesPerKm)
    {
        targetPaceMinutesPerKm = newPaceMinutesPerKm;
        CalculateVelocityMatrix(newPaceMinutesPerKm);
    }

    public float GetTargetSpeed() => _calculatedTargetSpeedMetersPerSecond;

    // ── Private helpers ──────────────────────────────────────────────────────
    private void CalculateVelocityMatrix(float pace)
    {
        _calculatedTargetSpeedMetersPerSecond = 1000f / (pace * 60f);
        Debug.Log($"[SPEED CALCULATOR] Pace {pace:F2}/km → {_calculatedTargetSpeedMetersPerSecond:F2} m/s");
    }

    private Vector3 SmoothSpatialData(Vector3 raw)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (_isKalmanInitialized)
        {
            float ox, oy, oz;
            UpdateKalmanFilter(raw.x, raw.y, raw.z, out ox, out oy, out oz);
            return new Vector3(ox, oy, oz);
        }
#endif
        return raw;
    }
}
