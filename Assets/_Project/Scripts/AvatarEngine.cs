using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core avatar pacing engine.
/// Implements AGENTS.md §4.1 (Vector_Forward Purification), §4.2 (Ground Snap integration),
/// §3 (Jitter Guard), §5 (GPS FSM), and the overtake / speed-maintenance behaviours.
/// </summary>
public class AvatarEngine : MonoBehaviour
{
    // ── Overtake state (read by OvertakeBehaviourController) ─────────────────
    public enum OvertakeState { None, BeingOvertaken, Overtaking }

    // ═══════════════════════════════════════════════════════════════════════
    // Inspector fields
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera;                  // XR Origin Main Camera
    [SerializeField] private GameStateController gameStateController;

    [Header("Pacing Settings")]
    [Tooltip("Target running pace in minutes per kilometer (e.g. 5.0 = 5:00/km)")]
    [SerializeField] private float targetPaceMinutesPerKm = 5.0f;
    [SerializeField] private float leadDistanceMeters = 3.0f;

    [Header("Speed Maintenance (Feature #3)")]
    [Tooltip("Avatar slows to this fraction of target speed when user is >= maxLeadBeforeSlow ahead")]
    [SerializeField] private float slowdownFraction = 0.5f;
    [Tooltip("Avatar slows when lead distance exceeds leadDistanceMeters + this value")]
    [SerializeField] private float maxLeadBeforeSlow = 1.0f;
    [Tooltip("Avatar speeds up to this fraction when user is catching up aggressively")]
    [SerializeField] private float catchupBoostFraction = 1.2f;
    [Tooltip("Distance below lead target that triggers catchup boost")]
    [SerializeField] private float catchupTriggerUnderrun = 0.5f;

    [Header("Curved Motion (Feature #5)")]
    [Tooltip("Maximum avatar rotation speed in degrees/second (45 = comfortable AR)")]
    [SerializeField] private float maxTurnDegreesPerSecond = 45.0f;

    [Header("Overtake Behaviour (Features #8 & #9)")]
    [Tooltip("Seconds user must be faster before 'being overtaken' triggers")]
    [SerializeField] private float overtakenConfirmSeconds = 1.5f;
    [Tooltip("Sprint speed multiplier when avatar surges to avoid being passed")]
    [SerializeField] private float sprintMultiplier = 1.25f;
    [Tooltip("Seconds the avatar holds sprint pace after an overtake surge")]
    [SerializeField] private float sprintHoldSeconds = 3.0f;
    [Tooltip("Lateral step distance when avatar yields to the user (metres, rightward)")]
    [SerializeField] private float overtakenSidestepMeters = 0.8f;

    // ── Native C++ Plugin Bridge ─────────────────────────────────────────────
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void InitKalmanFilter(float processNoise, float measurementNoise, float lteWeight);

    [DllImport("__Internal")]
    private static extern void UpdateKalmanFilter(float rawX, float rawY, float rawZ,
        out float smoothX, out float smoothY, out float smoothZ);

    private bool _isKalmanInitialized = false;
#endif

    // ═══════════════════════════════════════════════════════════════════════
    // Private state
    // ═══════════════════════════════════════════════════════════════════════

    private Vector3 _targetPacingPosition;
    private float   _calculatedTargetSpeedMetersPerSecond;

    private Vector3 _lastFrameUserPosition;

    // ── Vector_Forward Purification buffers (AGENTS.md §4.1) ────────────────
    private struct MovementFrame
    {
        public Vector3 delta;
        public float   time;
        public MovementFrame(Vector3 d, float t) { delta = d; time = t; }
    }
    private Queue<MovementFrame> _movementHistory = new Queue<MovementFrame>();

    // ── Heading history with per-frame weights (Feature #6) ─────────────────
    private struct WeightedHeading
    {
        public Vector3 dir;
        public float   timestamp;
        public WeightedHeading(Vector3 d, float t) { dir = d; timestamp = t; }
    }
    private Queue<WeightedHeading> _headingHistory = new Queue<WeightedHeading>();

    // Purified world-forward heading — never driven by gaze (Feature #7)
    private Vector3 _currentLinearDirection = Vector3.forward;

    // Smoothed rotation target (curved-motion arc)
    private Quaternion _smoothRotation = Quaternion.identity;

    // ── Jitter Guard (AGENTS.md §3 — ±5ms jitter tolerance) ─────────────────
    private float   _lastFrameDeltaTime       = 0.0f;
    private Vector3 _lastCleanKalmanVelocity  = Vector3.zero;
    private const float JitterThresholdSeconds = 0.005f;

    // ── Speed Maintenance state (Feature #3) ────────────────────────────────
    private float _effectiveSpeedMultiplier = 1.0f; // blended each frame

    // ── Overtake state machine (Features #8 & #9) ───────────────────────────
    private OvertakeState _overtakeState = OvertakeState.None;
    private float _overtakenTimer   = 0f;  // how long user has been faster
    private float _sprintTimer      = 0f;  // how long sprint has been held
    private Vector3 _sidestepOffset = Vector3.zero; // lateral shift when yielding

    // ── Public API ───────────────────────────────────────────────────────────
    public bool IsHalted { get; set; } = false;
    public float TargetPaceMinutesPerKm => targetPaceMinutesPerKm;
    public OvertakeState CurrentOvertakeState => _overtakeState;

    // ════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ════════════════════════════════════════════════════════════════════════
    void Start()
    {
        if (userCamera != null)
        {
            _lastFrameUserPosition   = userCamera.position;
            _currentLinearDirection  = userCamera.forward;
            _currentLinearDirection.y = 0;
            _currentLinearDirection.Normalize();
            _targetPacingPosition    = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
            _smoothRotation          = Quaternion.LookRotation(_currentLinearDirection);
        }

        if (gameStateController == null)
            gameStateController = FindObjectOfType<GameStateController>();

        CalculateVelocityMatrix(targetPaceMinutesPerKm);

#if UNITY_IOS && !UNITY_EDITOR
        InitKalmanFilter(0.05f, 0.8f, 0.12f);
        _isKalmanInitialized = true;
#endif
    }

    void Update()
    {
        if (userCamera == null) return;

        // ── GPS Drop-out / State Machine (AGENTS.md §5) ──────────────────────
        bool gpsLost = false;
        if (gameStateController != null)
        {
            var state = gameStateController.currentState;
            gpsLost = (state == GameStateController.ARVisionState.InertialMovement
                    || state == GameStateController.ARVisionState.FadeOut
                    || state == GameStateController.ARVisionState.Standby);
        }

        // Always update speed maintenance to ensure multipliers are fresh
        UpdateSpeedMaintenance();

        if (gpsLost)
        {
            RunInertialLinearMotion();
            return;
        }

        // ── Cliff / Obstacle halting (AGENTS.md §4.2) ────────────────────────
        if (IsHalted)
        {
            RunHaltedFaceUser();
            return;
        }

        // ── Jitter Guard (AGENTS.md §3) ──────────────────────────────────────
        float frameDeltaDrift = Mathf.Abs(Time.deltaTime - _lastFrameDeltaTime);
        bool  jitterSpike     = (_lastFrameDeltaTime > 0f) && (frameDeltaDrift > JitterThresholdSeconds);

        Vector3 filtered;
        if (jitterSpike)
        {
            Debug.LogWarning($"[JITTER GUARD] Frame-delta spike: {frameDeltaDrift * 1000f:F2}ms. Using prediction based on last good delta.");
            
            // Fix: Use last GOOD delta time instead of the spiked one to prevent jumps
            float predictDelta = _lastFrameDeltaTime;
            filtered = _targetPacingPosition + _lastCleanKalmanVelocity * predictDelta;
            
            // Update _lastFrameDeltaTime slightly so we adapt to new framerates and don't get permanently stuck
            _lastFrameDeltaTime = Mathf.Lerp(_lastFrameDeltaTime, Time.deltaTime, 0.1f);
        }
        else
        {
            // ── Vector_Forward Purification (AGENTS.md §4.1) ─────────────────────
            UpdatePurifiedHeading();

            // ── Overtake detection & state machine (Features #8 & #9) ────────────
            UpdateOvertakeState();

            // ── Compute filtered anchor position ──────────────────────────────────
            Vector3 rawAnchor = userCamera.position
                              + (_currentLinearDirection * leadDistanceMeters)
                              + _sidestepOffset;
            filtered  = SmoothSpatialData(rawAnchor);
            
            // Safety guard against C++ Kalman Filter returning NaN during initialization
            if (float.IsNaN(filtered.x) || float.IsNaN(filtered.y) || float.IsNaN(filtered.z))
            {
                filtered = rawAnchor;
            }
        }

        // Track Kalman velocity for jitter-fallback use next frame, clamped to a safe sprint speed
        Vector3 rawVelocity = (filtered - _targetPacingPosition) / Mathf.Max(Time.deltaTime, 0.001f);
        _lastCleanKalmanVelocity = Vector3.ClampMagnitude(rawVelocity, 10.0f);

        // Blend position with elastic catchup speed (Feature #3)
        float posLerpSpeed = GetEffectivePositionLerpSpeed();
        _targetPacingPosition = Vector3.Lerp(_targetPacingPosition, filtered,
                                             Time.deltaTime * posLerpSpeed);
        
        // Align our internal tracking position with GroundSnap's actual height to avoid Y drift fighting
        _targetPacingPosition.y = transform.position.y;
        transform.position = _targetPacingPosition;

        // ── Smooth curved rotation (Feature #5) ──────────────────────────────
        ApplySmoothRotation(_currentLinearDirection);

        _lastFrameUserPosition = userCamera.position;
        _lastFrameDeltaTime    = Time.deltaTime;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Feature #2 — Inertial Linear Motion (GPS lost)
    // ════════════════════════════════════════════════════════════════════════
    private void RunInertialLinearMotion()
    {
        if (!IsHalted)
        {
            float speed = GetTargetSpeed();
            _targetPacingPosition += _currentLinearDirection * speed * Time.deltaTime;
            transform.position     = _targetPacingPosition;
            ApplySmoothRotation(_currentLinearDirection);
        }
        _lastFrameDeltaTime    = Time.deltaTime;
        _lastFrameUserPosition = userCamera.position;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cliff halt — face the user in-place
    // ════════════════════════════════════════════════════════════════════════
    private void RunHaltedFaceUser()
    {
        Vector3 dirToUser = userCamera.position - transform.position;
        dirToUser.y = 0;
        if (dirToUser.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToUser.normalized);
            _smoothRotation      = Quaternion.RotateTowards(_smoothRotation, targetRot,
                                                            maxTurnDegreesPerSecond * Time.deltaTime);
            transform.rotation   = _smoothRotation;
        }
        _lastFrameUserPosition = userCamera.position;
        _lastFrameDeltaTime    = Time.deltaTime;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Feature #6 — Purified heading: GPS-only, weighted MA (1.5s window)
    // Feature #7 — Gaze lock: never use userCamera.forward as heading fallback
    // ════════════════════════════════════════════════════════════════════════
    private void UpdatePurifiedHeading()
    {
        // Accumulate movement delta (horizontal plane only)
        Vector3 movementDelta = userCamera.position - _lastFrameUserPosition;
        movementDelta.y = 0;

        _movementHistory.Enqueue(new MovementFrame(movementDelta, Time.time));
        while (_movementHistory.Count > 0 && Time.time - _movementHistory.Peek().time > 1.5f)
            _movementHistory.Dequeue();

        Vector3 integratedGPS = Vector3.zero;
        foreach (var frame in _movementHistory) integratedGPS += frame.delta;

        // Feature #7: Only update direction from GPS. If GPS is tiny, HOLD current direction.
        // Never fall back to userCamera.forward (that causes gaze-drift).
        if (integratedGPS.magnitude > 0.02f)
        {
            Vector3 gpsDir = integratedGPS.normalized;

            // Feature #6: Weighted angular velocity — recent frames weighted heavier
            // Weight = (age from newest / window) inverted → newest = weight 1.0
            _headingHistory.Enqueue(new WeightedHeading(gpsDir, Time.time));
            while (_headingHistory.Count > 0 && Time.time - _headingHistory.Peek().timestamp > 1.5f)
                _headingHistory.Dequeue();

            Vector3 weightedSum  = Vector3.zero;
            float   totalWeight  = 0f;
            float   newestTime   = Time.time;

            foreach (var h in _headingHistory)
            {
                float age    = newestTime - h.timestamp;          // 0 = newest
                float weight = Mathf.Exp(-age * 2.5f);            // exponential decay, recent = heavier
                weightedSum  += h.dir * weight;
                totalWeight  += weight;
            }

            if (totalWeight > 0f)
                _currentLinearDirection = (weightedSum / totalWeight).normalized;
        }
        // else: _currentLinearDirection is held exactly as-is (gaze lock — Feature #7)
    }

    // ════════════════════════════════════════════════════════════════════════
    // Feature #3 — Elastic-band speed maintenance
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateSpeedMaintenance()
    {
        // Current real distance between user and avatar anchor
        Vector3 toAvatar         = _targetPacingPosition - userCamera.position;
        toAvatar.y               = 0;
        float currentLead        = toAvatar.magnitude;  // positive = avatar ahead of user

        float targetSpeed        = _calculatedTargetSpeedMetersPerSecond;
        float targetMultiplier   = 1.0f;

        // User is falling behind → avatar is too far ahead → slow down
        if (currentLead > leadDistanceMeters + maxLeadBeforeSlow)
        {
            // Proportion: fully stopped at 2× maxLeadBeforeSlow overshoot
            float overshoot   = currentLead - (leadDistanceMeters + maxLeadBeforeSlow);
            float slowFraction = Mathf.Clamp01(overshoot / maxLeadBeforeSlow);
            targetMultiplier  = Mathf.Lerp(1.0f, slowdownFraction, slowFraction);
        }
        // User is catching up hard → avatar is almost within user's reach → accelerate slightly
        else if (currentLead < leadDistanceMeters - catchupTriggerUnderrun)
        {
            targetMultiplier = catchupBoostFraction;
        }

        // Sprint surge overrides (Feature #9)
        if (_overtakeState == OvertakeState.Overtaking)
            targetMultiplier = sprintMultiplier;

        // Smooth the multiplier so speed changes aren't abrupt
        _effectiveSpeedMultiplier = Mathf.Lerp(_effectiveSpeedMultiplier, targetMultiplier,
                                               Time.deltaTime * 3.0f);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Features #8 & #9 — Overtake state machine
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateOvertakeState()
    {
        // Estimate user's instantaneous speed from recent GPS movement
        Vector3 userFrameMove = userCamera.position - _lastFrameUserPosition;
        userFrameMove.y       = 0;
        float userSpeed       = userFrameMove.magnitude / Mathf.Max(Time.deltaTime, 0.001f);

        float avatarSpeed     = GetTargetSpeed();

        switch (_overtakeState)
        {
            case OvertakeState.None:
                // User is moving faster than avatar for sustained period → being overtaken
                if (userSpeed > avatarSpeed + 0.3f)   // 0.3 m/s buffer to avoid noise
                {
                    _overtakenTimer += Time.deltaTime;
                    if (_overtakenTimer >= overtakenConfirmSeconds)
                    {
                        EnterBeingOvertakenState();
                    }
                }
                else
                {
                    _overtakenTimer = Mathf.Max(0f, _overtakenTimer - Time.deltaTime);
                }

                // Avatar is closing to very near user → user almost catches avatar → avatar surges
                float currentLead = Vector3.Distance(
                    new Vector3(_targetPacingPosition.x, 0, _targetPacingPosition.z),
                    new Vector3(userCamera.position.x,   0, userCamera.position.z));
                if (currentLead < 0.5f && userSpeed > avatarSpeed * 0.9f)
                {
                    EnterOvertakingState();
                }
                break;

            case OvertakeState.BeingOvertaken:
                // Hold sidestep until user has passed (user back to behind avatar)
                float leadAgain = Vector3.Distance(
                    new Vector3(_targetPacingPosition.x, 0, _targetPacingPosition.z),
                    new Vector3(userCamera.position.x,   0, userCamera.position.z));
                if (leadAgain > leadDistanceMeters * 0.8f)
                {
                    ExitOvertakeState();
                }
                break;

            case OvertakeState.Overtaking:
                _sprintTimer += Time.deltaTime;
                if (_sprintTimer >= sprintHoldSeconds)
                {
                    ExitOvertakeState();
                }
                break;
        }
    }

    private void EnterBeingOvertakenState()
    {
        _overtakeState  = OvertakeState.BeingOvertaken;
        _overtakenTimer = 0f;

        // Step right relative to current forward direction (race etiquette)
        Vector3 rightDir = Vector3.Cross(Vector3.up, _currentLinearDirection).normalized;
        _sidestepOffset  = rightDir * overtakenSidestepMeters;

        // Notify animator via AvatarVisualsAndActions / OvertakeBehaviourController
        SendOvertakeAnimatorTrigger("Overtaken");
        Debug.Log("[OVERTAKE] User is overtaking — avatar stepping right.");
    }

    private void EnterOvertakingState()
    {
        _overtakeState = OvertakeState.Overtaking;
        _sprintTimer   = 0f;
        SendOvertakeAnimatorTrigger("Sprint");
        Debug.Log("[OVERTAKE] Avatar surging to avoid being passed.");
    }

    private void ExitOvertakeState()
    {
        _overtakeState  = OvertakeState.None;
        _overtakenTimer = 0f;
        _sprintTimer    = 0f;

        // Smoothly return sidestep to zero over next frames (handled via Lerp in Update)
        _sidestepOffset = Vector3.zero;
        Debug.Log("[OVERTAKE] Returning to normal pacing.");
    }

    private void SendOvertakeAnimatorTrigger(string triggerName)
    {
        OvertakeBehaviourController overtake = GetComponent<OvertakeBehaviourController>();
        Animator anim = (overtake != null && overtake.ActiveAnimator != null) ? overtake.ActiveAnimator : GetComponentInChildren<Animator>();
        if (anim != null) anim.SetTrigger(triggerName);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Feature #5 — Smooth arc rotation (45°/s cap)
    // ════════════════════════════════════════════════════════════════════════
    private void ApplySmoothRotation(Vector3 targetDirection)
    {
        if (targetDirection.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(targetDirection);
        _smoothRotation      = Quaternion.RotateTowards(_smoothRotation, targetRot,
                                                        maxTurnDegreesPerSecond * Time.deltaTime);
        transform.rotation   = _smoothRotation;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Public API
    // ════════════════════════════════════════════════════════════════════════
    public void UpdateTargetPace(float newPaceMinutesPerKm)
    {
        targetPaceMinutesPerKm = newPaceMinutesPerKm;
        CalculateVelocityMatrix(newPaceMinutesPerKm);
    }

    public float GetTargetSpeed() => _calculatedTargetSpeedMetersPerSecond * _effectiveSpeedMultiplier;

    public float GetBaseTargetSpeed() => _calculatedTargetSpeedMetersPerSecond;

    // ════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ════════════════════════════════════════════════════════════════════════
    private void CalculateVelocityMatrix(float pace)
    {
        _calculatedTargetSpeedMetersPerSecond = 1000f / (pace * 60f);
        Debug.Log($"[SPEED CALCULATOR] Pace {pace:F2}/km → {_calculatedTargetSpeedMetersPerSecond:F2} m/s");
    }

    private float GetEffectivePositionLerpSpeed()
    {
        // Faster lerp during sprint, slower when waiting for user to catch up
        return _overtakeState == OvertakeState.Overtaking ? 4.0f : 2.5f;
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
