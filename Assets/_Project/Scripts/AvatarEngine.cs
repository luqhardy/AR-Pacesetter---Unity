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
    [SerializeField] private RunnerTrackingState runnerTrackingState;

    [Header("Pacing Settings")]
    [Tooltip("Target running pace in minutes per kilometer (e.g. 5.0 = 5:00/km)")]
    [SerializeField] private float targetPaceMinutesPerKm = 5.0f;
    [SerializeField] private float leadDistanceMeters = 3.0f;

    [Header("Start Sequence")]
    [Tooltip("Safety fallback if the visual countdown is unavailable or interrupted")]
    [SerializeField] private float countdownFallbackSeconds = 4.25f;

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
    // True after actual horizontal movement has established the authoritative heading.
    private bool _hasMovementHeading = false;

    // Smoothed rotation target (curved-motion arc)
    private Quaternion _smoothRotation = Quaternion.identity;

    // ── Jitter Guard (AGENTS.md §3 — ±5ms jitter tolerance) ─────────────────
    private float   _lastFrameDeltaTime       = 0.0f;
    private Vector3 _lastCleanKalmanVelocity  = Vector3.zero;
    private const float JitterThresholdSeconds = 0.005f;
    private float   _lastJitterWarningTime    = -99f;

    // ── Speed Maintenance state (Feature #3) ────────────────────────────────
    private float _effectiveSpeedMultiplier = 1.0f; // blended each frame

    // ── Overtake state machine (Features #8 & #9) ───────────────────────────
    private OvertakeState _overtakeState = OvertakeState.None;
    private float _overtakenTimer   = 0f;  // how long user has been faster
    private float _sprintTimer      = 0f;  // how long sprint has been held
    private Vector3 _sidestepOffset = Vector3.zero; // lateral shift when yielding
    
    private bool _hasStarted = false; // Start command state
    private bool _runMotionActive = false;
    private float _runMotionFallbackAt = -1f;
    private CountdownDisplay _countdownDisplay;

    // ── 離隔待機 (企画書 4.1) ────────────────────────────────────────────────
    private const float WaitForUserEnterMeters = 10.0f; // これ以上離れたら待機
    private const float WaitForUserExitMeters  = 7.0f;  // ここまで戻ったら再開
    private bool _isWaitingForUser = false;

    // ── Public API ───────────────────────────────────────────────────────────
    public bool IsHalted { get; set; } = false;
    // Set by RunSessionController when the run finishes. Kept separate from
    // IsHalted because GroundSnap overwrites IsHalted every frame.
    public bool IsSessionEnded { get; set; } = false;
    public float TargetPaceMinutesPerKm => targetPaceMinutesPerKm;
    public OvertakeState CurrentOvertakeState => _overtakeState;
    public bool HasStarted => _hasStarted;
    /// <summary>True only after 3-2-1-START has completed.</summary>
    public bool IsRunMotionActive => _hasStarted && _runMotionActive && !IsSessionEnded;
    public bool IsStartCountdownActive => _hasStarted && !_runMotionActive && !IsSessionEnded;
    public bool IsOverriddenByRecovery { get; set; } = false;
    public bool IsWaitingForUser => _isWaitingForUser;

    /// <summary>純化済みの進行方向(水平・単位ベクトル)。ペースシンクロ色の
    /// 符号付きリード距離算出などに使う。待機/停止中も直近の向きを保持する。</summary>
    public Vector3 CurrentHeading => _currentLinearDirection;
    public float LeadDistanceMeters => leadDistanceMeters;

    public void StartPacing()
    {
        if (!_hasStarted)
        {
            AlignStartHeadingToUserView();
            _hasStarted = true;
            _runMotionActive = false;
            _countdownDisplay = FindFirstObjectByType<CountdownDisplay>(FindObjectsInactive.Include);
            _runMotionFallbackAt = Time.time + Mathf.Max(0f, countdownFallbackSeconds);
            Debug.Log("[PACER ENGINE] Session armed — waiting for 3-2-1-START before movement and metrics begin.");

            // Older scenes without CountdownDisplay must remain usable.
            if (_countdownDisplay == null || countdownFallbackSeconds <= 0f)
                ActivateRunMotion();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ════════════════════════════════════════════════════════════════════════
    void Start()
    {
        if (userCamera != null)
        {
            _lastFrameUserPosition   = userCamera.position;
            UpdatePurifiedHeading(); // Initial heading calculation
            
            // Set initial position at current lead distance but keep HasStarted false
            _targetPacingPosition    = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
            _targetPacingPosition.y  = transform.position.y;
            
            _smoothRotation          = Quaternion.LookRotation(_currentLinearDirection);
            transform.position       = _targetPacingPosition;
            transform.rotation       = _smoothRotation;
        }

        if (gameStateController == null)
            gameStateController = FindFirstObjectByType<GameStateController>();

        CalculateVelocityMatrix(targetPaceMinutesPerKm);

#if UNITY_IOS && !UNITY_EDITOR
        InitKalmanFilter(0.05f, 0.8f, 0.12f);
        _isKalmanInitialized = true;
#endif
        Debug.Log("[PACER ENGINE] Initialized and waiting for Start command.");
    }

    void Update()
    {
        if (userCamera == null) return;

        // Always update speed maintenance to ensure multipliers are fresh
        UpdateSpeedMaintenance();

        if (IsOverriddenByRecovery)
        {
            // Skip positioning, but keep histories and trackers fresh
            UpdatePurifiedHeading();
            _lastFrameUserPosition = userCamera.position;
            _lastFrameDeltaTime = Time.deltaTime;
            return;
        }

        // ── Pre-start logic ─────────────────────────────────────────────────
        if (!_hasStarted)
        {
            // Update purified heading but don't move forward yet
            UpdatePurifiedHeading();
            
            // Calculate where the avatar SHOULD be (lead distance ahead of user)
            Vector3 rawAnchor = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
            _targetPacingPosition = rawAnchor;
            
            // Apply the position immediately so it stays stuck 3m ahead of user
            // GroundSnap will handle the Y in LateUpdate
            _targetPacingPosition.y = transform.position.y;
            transform.position = _targetPacingPosition;
            
            // While waiting, look at the user or stay ahead
            RunHaltedFaceUser();
            return;
        }

        // Start command arms the experience, but the runner has not crossed the
        // temporal start line until the complete countdown presentation ends.
        // Keep the pacer visibly staged ahead without accumulating movement.
        if (!_runMotionActive)
        {
            UpdatePurifiedHeading();
            Vector3 readyAnchor = userCamera.position
                                + _currentLinearDirection * leadDistanceMeters;
            readyAnchor.y = transform.position.y;
            _targetPacingPosition = readyAnchor;
            transform.position = readyAnchor;
            ApplySmoothRotation(_currentLinearDirection);

            bool countdownFinished = _countdownDisplay != null
                                  && _countdownDisplay.HasCompleted;
            if (!IsSessionEnded
                && (countdownFinished || Time.time >= _runMotionFallbackAt))
            {
                ActivateRunMotion();
            }

            _lastFrameUserPosition = userCamera.position;
            _lastFrameDeltaTime = Time.deltaTime;
            return;
        }

        // ── GPS Drop-out / State Machine (AGENTS.md §5) ──────────────────────
        bool gpsLost = false;
        if (gameStateController != null)
        {
            var state = gameStateController.currentState;
            gpsLost = (state == GameStateController.ARVisionState.InertialMovement
                    || state == GameStateController.ARVisionState.FadeOut
                    || state == GameStateController.ARVisionState.Standby);
        }

        if (gpsLost)
        {
            RunInertialLinearMotion();
            return;
        }

        // ── 離隔待機 (企画書 4.1 自律アクション) ─────────────────────────────
        // 10m以上離れたら座標を固定してユーザーへ向き、手招きで待つ。7mまで
        // 戻ったら走行再開(ヒステリシスでチャタリング防止)。
        UpdateWaitForUserState();

        // ── Cliff / Obstacle halting (AGENTS.md §4.2) / Session end ─────────
        if (IsHalted || IsSessionEnded || _isWaitingForUser)
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
            if (Time.time - _lastJitterWarningTime > 2.0f)
            {
                Debug.LogWarning($"[JITTER GUARD] Frame-delta spike: {frameDeltaDrift * 1000f:F2}ms. Using prediction based on last good delta.");
                _lastJitterWarningTime = Time.time;
            }
            
            // Fix: Advance by the current spiked Time.deltaTime since that represents actual elapsed time
            filtered = _targetPacingPosition + _lastCleanKalmanVelocity * Time.deltaTime;
            
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
            rawAnchor.y = transform.position.y;
            filtered  = SmoothSpatialData(rawAnchor);
            
            // Safety guard against C++ Kalman Filter returning NaN during initialization
            if (float.IsNaN(filtered.x) || float.IsNaN(filtered.y) || float.IsNaN(filtered.z))
            {
                filtered = rawAnchor;
            }
        }

        // Track Kalman velocity for jitter-fallback use next frame, clamped to a safe sprint speed
        Vector3 rawVelocity = (filtered - _targetPacingPosition) / Mathf.Max(Time.deltaTime, 0.001f);
        rawVelocity.y = 0;
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
        if (!_hasStarted)
        {
            RunHaltedFaceUser();
            return;
        }

        if (!IsHalted)
        {
            float speed = GetTargetSpeed();
            _targetPacingPosition += _currentLinearDirection * speed * Time.deltaTime;
            
            // Fix: Align Y with ground snap height even during inertial motion
            _targetPacingPosition.y = transform.position.y;
            transform.position     = _targetPacingPosition;
            
            ApplySmoothRotation(_currentLinearDirection);
        }
        _lastFrameDeltaTime    = Time.deltaTime;
        _lastFrameUserPosition = userCamera.position;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 離隔待機 — 10m離れたら座標固定+手招き、7mまで戻ったら再開
    // ════════════════════════════════════════════════════════════════════════
    private void UpdateWaitForUserState()
    {
        Vector3 toAvatar = transform.position - userCamera.position;
        toAvatar.y = 0;
        float separation = toAvatar.magnitude;

        if (!_isWaitingForUser && separation >= WaitForUserEnterMeters)
        {
            _isWaitingForUser = true;
            SendSafeAnimatorTrigger("Beckon"); // 手招きアクション (Animator側に用意)
            Debug.Log($"[PACER ENGINE] User fell {separation:F1}m behind — holding position and beckoning.");
        }
        else if (_isWaitingForUser && separation <= WaitForUserExitMeters)
        {
            _isWaitingForUser = false;
            // 内部追従位置を現在位置に同期してから再開(ワープ防止)
            _targetPacingPosition = transform.position;
            SendSafeAnimatorTrigger("RunResume");
            Debug.Log("[PACER ENGINE] User caught up — resuming pace.");
        }
    }

    // Animator にパラメータが存在する場合のみトリガーを発火する
    private void SendSafeAnimatorTrigger(string triggerName)
    {
        OvertakeBehaviourController overtake = GetComponent<OvertakeBehaviourController>();
        Animator anim = (overtake != null && overtake.ActiveAnimator != null)
            ? overtake.ActiveAnimator
            : AvatarRigLocator.FindBestAnimator(transform);
        if (anim == null) return;

        foreach (var param in anim.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger && param.name == triggerName)
            {
                anim.SetTrigger(triggerName);
                return;
            }
        }
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
        if (runnerTrackingState == null)
            runnerTrackingState = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);

        // 実機ではRunnerTrackingStateがCoreLocation方位とARKit/IMU移動を1.5秒窓で
        // 融合済み。これを進行方向の一次情報にし、旧シーンでは下のローカル計測へ
        // フォールバックする。
        if (runnerTrackingState != null && runnerTrackingState.HasMovementHeading)
        {
            Vector3 trackedHeading = runnerTrackingState.CurrentHeading;
            trackedHeading.y = 0f;
            if (trackedHeading.sqrMagnitude > 0.0001f)
            {
                _currentLinearDirection = trackedHeading.normalized;
                _hasMovementHeading = true;
                // Do not advance _lastFrameUserPosition here. Overtake detection
                // later in this frame needs the same previous-frame sample to
                // calculate the runner's actual instantaneous speed.
                return;
            }
        }

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
            _hasMovementHeading = true;
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
        Animator anim = (overtake != null && overtake.ActiveAnimator != null) ? overtake.ActiveAnimator : AvatarRigLocator.FindBestAnimator(transform);
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

    /// <summary>Swiftブリッジ用: 先行距離 (forwardOffsetM) の外部設定。</summary>
    /// <summary>
    /// 現在の設定で表示される公称身長(m)。0なら計測できていない。E2E/検証用。
    /// VFXの起動演出(ルートのスケールを0.15→1.0で動かす)の途中経過は含めない。
    /// </summary>
    public float MeasuredAvatarHeightMeters
    {
        get
        {
            Transform model = ResolveModelRoot();
            return model == null ? 0f : MeasureUnitHeight(model) * model.localScale.y;
        }
    }

    /// <summary>直近に指定された身長(cm)。モデル差し替え後の再適用に使う。</summary>
    public float RequestedHeightCm { get; private set; } = AvatarScale.BaselineHeightCm;

    /// <summary>
    /// アバターを指定身長(cm)の実寸へ合わせる (企画書 §4.1)。
    ///
    /// **モデル側のTransformを縮尺する**。ルートの localScale は
    /// AvatarVFXController が起動/消滅演出で動かしており、そこへ身長を混ぜると
    /// 演出とスケールが取り合いになるため触らない。
    ///
    /// 倍率は固定値ではなく**実測した描画高さから逆算**する。FBXの単位や
    /// インポート設定でモデルの素の大きさは変わるため、決め打ちだと実物と合わない。
    /// </summary>
    /// <returns>適用できたか(モデル未解決・計測失敗時は false で現状維持)</returns>
    public bool ApplyHeightCm(float heightCm)
    {
        if (heightCm > 0f) RequestedHeightCm = heightCm;

        Transform model = ResolveModelRoot();
        if (model == null)
        {
            Debug.LogWarning("[AVATAR SCALE] モデルのTransformを解決できず身長を適用できません。");
            return false;
        }

        // スケール1相当の素の高さ。これを基準に必要な倍率を出す
        float unitHeight = MeasureUnitHeight(model);
        if (!AvatarScale.TryComputeScale(unitHeight, 1f, RequestedHeightCm, out float scale))
        {
            Debug.LogWarning($"[AVATAR SCALE] 素の身長を計測できず適用を見送りました " +
                             $"(unitHeight={unitHeight:F3}m)");
            return false;
        }

        float before = unitHeight * model.localScale.y;
        model.localScale = Vector3.one * scale;

        Debug.Log($"[AVATAR SCALE] 身長 {RequestedHeightCm:F0}cm へ調整: " +
                  $"変更前 {before:F2}m → scale {scale:F3} (公称 {MeasuredAvatarHeightMeters:F2}m)");
        return true;
    }

    /// <summary>モデル差し替え後などに、同じ身長指定で再適用する。</summary>
    public void ReapplyHeight() => ApplyHeightCm(RequestedHeightCm);

    /// <summary>表示中のモデル(Animatorが載っているTransform)。</summary>
    private Transform ResolveModelRoot()
    {
        Animator anim = AvatarRigLocator.FindBestAnimator(transform);
        return anim != null ? anim.transform : null;
    }

    /// <summary>
    /// モデルの「スケール1相当」の身長(m)を測る。
    ///
    /// ワールド空間の <c>Renderer.bounds</c> ではなく、**オーサリング時の
    /// <c>localBounds</c> をモデル空間へ変換**して測る。理由が2つある:
    ///   1. ワールドboundsはアニメーションのポーズで変わる(走行中は脚が曲がって縮む)
    ///   2. ワールドboundsは親のスケールを含む。AvatarVFXControllerが起動演出で
    ///      ルートのスケールを0.15→1.0と動かすため、その途中で測ると小さく出て
    ///      演出ぶんまで打ち消す方向に補正してしまう
    /// モデル空間へ落とすことで、ポーズにも親のスケールにも左右されない素の高さが得られる。
    ///
    /// スキンメッシュのみを対象にするので、足元の影(MeshRenderer)や
    /// オーラのLineRendererは巻き込まない。
    /// </summary>
    private static float MeasureUnitHeight(Transform model)
    {
        if (model == null) return 0f;

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0) return 0f;

        bool any = false;
        float minY = 0f, maxY = 0f;

        foreach (var r in renderers)
        {
            Bounds lb = r.localBounds; // ポーズ非依存のオーサリング値
            Matrix4x4 toModel = model.worldToLocalMatrix * r.transform.localToWorldMatrix;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = new Vector3(
                    (corner & 1) == 0 ? lb.min.x : lb.max.x,
                    (corner & 2) == 0 ? lb.min.y : lb.max.y,
                    (corner & 4) == 0 ? lb.min.z : lb.max.z);

                float y = toModel.MultiplyPoint3x4(p).y;
                if (!any) { minY = maxY = y; any = true; }
                else { if (y < minY) minY = y; if (y > maxY) maxY = y; }
            }
        }

        return any ? (maxY - minY) : 0f;
    }

    public void SetLeadDistance(float meters)
    {
        leadDistanceMeters = Mathf.Clamp(meters, 1.0f, 10.0f);
    }

    /// <summary>
    /// 追従アンカーの内部状態を外部座標へ再同期する(SilentRouteRecoverer用)。
    /// サイレント復帰の解除時など、アバターの位置を外部が動かした後に呼ぶことで、
    /// 次フレームのペーシング再開時のワープを防ぐ。
    /// (旧実装はリフレクションで private フィールドを書き換えていた — 型安全化)
    /// </summary>
    public void ResyncPacingAnchor(Vector3 avatarWorldPosition, Vector3 userWorldPosition)
    {
        _targetPacingPosition  = avatarWorldPosition;
        _lastFrameUserPosition = userWorldPosition;
    }

    /// <summary>
    /// 再走行対応: 終了済みセッションの状態を破棄し、次の StartPacing を受け付ける。
    /// アバターはユーザー前方の初期位置へ戻り待機状態になる。
    /// </summary>
    public void ResetSession()
    {
        _hasStarted = false;
        _runMotionActive = false;
        _runMotionFallbackAt = -1f;
        IsSessionEnded = false;
        IsHalted = false;
        IsOverriddenByRecovery = false;
        _isWaitingForUser = false;

        _movementHistory.Clear();
        _headingHistory.Clear();
        _hasMovementHeading = false;
        _overtakeState = OvertakeState.None;
        _overtakenTimer = 0f;
        _sprintTimer = 0f;
        _sidestepOffset = Vector3.zero;
        _effectiveSpeedMultiplier = 1.0f;
        _lastCleanKalmanVelocity = Vector3.zero;

        if (userCamera != null)
        {
            _lastFrameUserPosition = userCamera.position;
            _targetPacingPosition = userCamera.position + (_currentLinearDirection * leadDistanceMeters);
            _targetPacingPosition.y = transform.position.y; // GroundSnapのY管理を尊重
            transform.position = _targetPacingPosition;
        }

        Debug.Log("[PACER ENGINE] Session reset — waiting for next Start command.");
    }

    public float GetTargetSpeed() => _calculatedTargetSpeedMetersPerSecond * _effectiveSpeedMultiplier;

    public float GetBaseTargetSpeed() => _calculatedTargetSpeedMetersPerSecond;

    // ════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// 静止状態ではGPS移動ベクトルがまだ存在せず、既定のworld +Zがユーザーの背後に
    /// なる場合がある。開始時に限り現在の視線方向を初期アンカーへ使い、移動検出後は
    /// UpdatePurifiedHeadingだけが方位を更新する。従って走行中の見回しでは横揺れしない。
    /// </summary>
    private void AlignStartHeadingToUserView()
    {
        if (_hasMovementHeading || userCamera == null)
            return;

        Vector3 initialForward = userCamera.forward;
        initialForward.y = 0f;
        if (initialForward.sqrMagnitude < 0.0001f)
            return;

        _currentLinearDirection = initialForward.normalized;
        _targetPacingPosition = userCamera.position
                              + _currentLinearDirection * leadDistanceMeters;
        _targetPacingPosition.y = transform.position.y;
        _smoothRotation = Quaternion.LookRotation(_currentLinearDirection, Vector3.up);
        transform.SetPositionAndRotation(_targetPacingPosition, _smoothRotation);
    }

    private void ActivateRunMotion()
    {
        if (!_hasStarted || _runMotionActive || IsSessionEnded)
            return;

        if (runnerTrackingState == null)
            runnerTrackingState = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
        if (runnerTrackingState != null)
            runnerTrackingState.MarkRunMotionStarted();

        _runMotionActive = true;
        _targetPacingPosition = transform.position;
        _lastFrameUserPosition = userCamera != null ? userCamera.position : Vector3.zero;
        _lastFrameDeltaTime = Time.deltaTime;
        SendSafeAnimatorTrigger("RunResume");
        Debug.Log("[PACER ENGINE] START complete — runner timing, tracking and pacer motion are active.");
    }

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
