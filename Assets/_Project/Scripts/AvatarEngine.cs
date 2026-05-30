using System.Runtime.InteropServices;
using UnityEngine;

public class AvatarEngine : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera; // XR Origin Main Camera

    [Header("Pacing Settings")]
    [Tooltip("Target running pace set in minutes per kilometer (e.g., 5.0 = 5:00/km pace)")]
    [SerializeField] private float targetPaceMinutesPerKm = 5.0f;
    [SerializeField] private float leadDistanceMeters = 3.0f;
    [SerializeField] private float accelerationCatchupSpeed = 2.5f;

    // Native C++ Plugin Bridge Architecture
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void InitKalmanFilter(float processNoise, float measurementNoise, float lteWeight);
    
    [DllImport("__Internal")]
    private static extern void UpdateKalmanFilter(float rawX, float rawY, float rawZ, out float smoothX, out float smoothY, out float smoothZ);

    private bool _isKalmanInitialized = false;
#endif

    private Vector3 _targetPacingPosition;
    private float _calculatedTargetSpeedMetersPerSecond;

    // Linear Movement (’¼ü‰^“®) State Tracking Variables
    private Vector3 _lastFrameUserPosition;
    private Vector3 _currentLinearDirection = Vector3.forward;

    void Start()
    {
        if (userCamera != null)
        {
            _lastFrameUserPosition = userCamera.position;

            _currentLinearDirection = userCamera.forward;
            _currentLinearDirection.y = 0;
            _currentLinearDirection.Normalize();

            // FIX: Instantly snap to the 3m mark on frame 1 to avoid spawning at (0,0,0)
            _targetPacingPosition = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
        }

        CalculateVelocityMatrix(targetPaceMinutesPerKm);

#if UNITY_IOS && !UNITY_EDITOR
        // Initialize native C++ plugin filter parameters
        InitKalmanFilter(0.05f, 0.8f, 0.12f);
        _isKalmanInitialized = true;
#endif
    }

    void Update()
    {
        if (userCamera == null) return;

        // 1. Calculate true movement vector based on spatial displacement (’¼ü‰^“® Implementation)
        Vector3 movementDelta = userCamera.position - _lastFrameUserPosition;
        movementDelta.y = 0; // Lock calculations strictly to the horizontal running plane

        Vector3 trueMovementDirection = _currentLinearDirection;

        // Update tracking path direction only if the user is moving past a micro-GPS noise threshold
        if (movementDelta.magnitude > 0.02f)
        {
            trueMovementDirection = movementDelta.normalized;
            _currentLinearDirection = trueMovementDirection; // Cache verified heading
        }
        else if (_currentLinearDirection == Vector3.zero)
        {
            // Fallback baseline if player is completely stationary at application boot
            trueMovementDirection = userCamera.forward;
            trueMovementDirection.y = 0;
            trueMovementDirection.Normalize();
            _currentLinearDirection = trueMovementDirection;
        }

        // 2. Position the target anchor 3.0m ahead along your actual path of physical progression
        Vector3 rawPacingAnchor = userCamera.position + (trueMovementDirection * leadDistanceMeters);

        // 3. Pass raw target data coordinates through the Low-Latency Filtering Layer
        Vector3 filteredPosition = SmoothSpatialData(rawPacingAnchor);

        // 4. Apply Fluid Acceleration Matrix to eliminate tracking jitters smoothly
        _targetPacingPosition = Vector3.Lerp(_targetPacingPosition, filteredPosition, Time.deltaTime * accelerationCatchupSpeed);

        // 5. Finalize Engine Position and orient avatar gaze down the track path
        transform.position = _targetPacingPosition;
        if (trueMovementDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(trueMovementDirection);
        }

        // Cache historical coordinate frames for next frame vector evaluation
        _lastFrameUserPosition = userCamera.position;
    }

    // --- THE PACING OVERLAY GATEWAY ---
    public void UpdateTargetPace(float newPaceMinutesPerKm)
    {
        targetPaceMinutesPerKm = newPaceMinutesPerKm;
        CalculateVelocityMatrix(newPaceMinutesPerKm);
    }

    private void CalculateVelocityMatrix(float pace)
    {
        float totalSecondsPerKm = pace * 60f;
        _calculatedTargetSpeedMetersPerSecond = 1000f / totalSecondsPerKm;
        Debug.Log($"[SPEED CALCULATOR] Pace recalculated to: {pace:F2}/km -> {_calculatedTargetSpeedMetersPerSecond:F2} m/s");
    }

    private Vector3 SmoothSpatialData(Vector3 rawAnchor)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (_isKalmanInitialized)
        {
            float outX, outY, outZ;
            UpdateKalmanFilter(rawAnchor.x, rawAnchor.y, rawAnchor.z, out outX, out outY, out outZ);
            return new Vector3(outX, outY, outZ);
        }
#endif
        // Windows Editor Dev Fallback
        return rawAnchor;
    }

    public float GetTargetSpeed()
    {
        return _calculatedTargetSpeedMetersPerSecond;
    }
}