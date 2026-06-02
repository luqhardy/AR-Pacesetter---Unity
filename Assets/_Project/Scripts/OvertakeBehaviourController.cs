using UnityEngine;

/// <summary>
/// Handles the visual and animator feedback for overtake interactions.
///
/// 追い抜かされる動作 (Feature #8): When the user runs faster than the avatar for
/// >= overtakenConfirmSeconds, the avatar steps right, turns its head toward the
/// user and plays the "Overtaken" animator trigger.
///
/// 追い抜かせる動作 (Feature #9): When the user closes to within 0.5 m of the avatar,
/// the avatar surges at sprintMultiplier speed and plays the "Sprint" trigger.
///
/// This script reads OvertakeState from AvatarEngine and drives visual reactions,
/// keeping motion math cleanly separated.
/// </summary>
[RequireComponent(typeof(AvatarEngine))]
public class OvertakeBehaviourController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform userCamera;  // XR Origin Main Camera
    [SerializeField] private Animator  avatarAnimator;

    [Header("Head-Turn Settings (Being Overtaken)")]
    [Tooltip("How quickly the avatar turns its head toward the passing user (deg/s)")]
    [SerializeField] private float headTurnSpeed = 90.0f;

    [Header("Visual Feedback")]
    [Tooltip("Optional particle system to play on sprint surge")]
    [SerializeField] private ParticleSystem sprintParticles;
    [Tooltip("Optional particle system to play when yielding (being overtaken)")]
    [SerializeField] private ParticleSystem overtakenParticles;

    // ── Private state ────────────────────────────────────────────────────────
    private AvatarEngine              _engine;
    private AvatarEngine.OvertakeState _lastState = AvatarEngine.OvertakeState.None;

    // For smooth head-look during BeingOvertaken
    private bool      _isLookingAtUser = false;
    private Quaternion _defaultHeadRot  = Quaternion.identity;

    // ── Unity lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        _engine = GetComponent<AvatarEngine>();

        if (avatarAnimator == null)
            avatarAnimator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (_engine == null) return;

        AvatarEngine.OvertakeState currentState = _engine.CurrentOvertakeState;

        // ── React to state transitions ─────────────────────────────────────
        if (currentState != _lastState)
        {
            OnOvertakeStateChanged(_lastState, currentState);
            _lastState = currentState;
        }

        // ── Per-frame visual update ────────────────────────────────────────
        switch (currentState)
        {
            case AvatarEngine.OvertakeState.BeingOvertaken:
                UpdateHeadLookAtUser();
                break;

            case AvatarEngine.OvertakeState.None:
                if (_isLookingAtUser) ReturnHeadToForward();
                break;
        }
    }

    // ── State change reactions ───────────────────────────────────────────────
    private void OnOvertakeStateChanged(AvatarEngine.OvertakeState from,
                                        AvatarEngine.OvertakeState to)
    {
        switch (to)
        {
            case AvatarEngine.OvertakeState.BeingOvertaken:
                // Start head-turn toward the passing user
                _isLookingAtUser = true;

                // Visual: yield particles
                if (overtakenParticles != null && !overtakenParticles.isPlaying)
                    overtakenParticles.Play();

                // Stop sprint particles if they were running
                if (sprintParticles != null && sprintParticles.isPlaying)
                    sprintParticles.Stop();

                // Animator: trigger idle-react or sideways-look blend
                SetAnimatorTriggerSafe("Overtaken");

                Debug.Log("[OVERTAKE VISUAL] Entering BeingOvertaken — head-look ON, yield particles playing.");
                break;

            case AvatarEngine.OvertakeState.Overtaking:
                _isLookingAtUser = false;

                // Visual: sprint particles
                if (sprintParticles != null && !sprintParticles.isPlaying)
                    sprintParticles.Play();

                // Stop yield particles
                if (overtakenParticles != null && overtakenParticles.isPlaying)
                    overtakenParticles.Stop();

                SetAnimatorTriggerSafe("Sprint");

                Debug.Log("[OVERTAKE VISUAL] Entering Overtaking — sprint particles playing.");
                break;

            case AvatarEngine.OvertakeState.None:
                // Clean up all overtake visuals
                if (sprintParticles    != null && sprintParticles.isPlaying)    sprintParticles.Stop();
                if (overtakenParticles != null && overtakenParticles.isPlaying) overtakenParticles.Stop();

                SetAnimatorTriggerSafe("RunResume");
                Debug.Log("[OVERTAKE VISUAL] Returning to normal pacing.");
                break;
        }
    }

    // ── Head-look helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly turns the avatar's head bone toward the user camera while yielding.
    /// Falls back gracefully if no head bone is found.
    /// </summary>
    private void UpdateHeadLookAtUser()
    {
        if (avatarAnimator == null || userCamera == null) return;

        // Use Animator IK if the avatar rig supports it (humanoid)
        if (avatarAnimator.isHuman)
        {
            // IK weight is set in OnAnimatorIK — just flag the intent here
            // (IK callback below handles the actual bone rotation)
        }
        else
        {
            // Non-humanoid fallback: rotate the whole transform body slightly toward user
            Vector3 toUser = userCamera.position - transform.position;
            toUser.y       = 0;
            if (toUser.sqrMagnitude > 0.01f)
            {
                Quaternion lookTarget = Quaternion.LookRotation(toUser.normalized);
                // Only rotate 20° from forward to simulate a glance, not a full spin
                lookTarget = Quaternion.Slerp(transform.rotation, lookTarget, 0.2f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, lookTarget, headTurnSpeed * Time.deltaTime);
            }
        }
    }

    /// <summary>Unity Animator IK callback — drives humanoid head look-at toward user.</summary>
    private void OnAnimatorIK(int layerIndex)
    {
        if (avatarAnimator == null || userCamera == null) return;
        if (!_isLookingAtUser) return;

        avatarAnimator.SetLookAtWeight(
            weight:      0.8f,    // overall IK blend
            bodyWeight:  0.1f,    // slight body lean
            headWeight:  1.0f,    // full head rotation
            eyesWeight:  0.6f,    // eye tracking
            clampWeight: 0.5f);   // prevent extreme angles

        avatarAnimator.SetLookAtPosition(userCamera.position);
    }

    private void ReturnHeadToForward()
    {
        // Disable IK look — the animator will blend back naturally
        if (avatarAnimator != null && avatarAnimator.isHuman)
        {
            avatarAnimator.SetLookAtWeight(0f);
        }
        _isLookingAtUser = false;
    }

    // ── Animator helper ──────────────────────────────────────────────────────
    private void SetAnimatorTriggerSafe(string triggerName)
    {
        if (avatarAnimator == null) return;

        // Check that the trigger exists in the controller before calling it
        foreach (var param in avatarAnimator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger
             && param.name == triggerName)
            {
                avatarAnimator.SetTrigger(triggerName);
                return;
            }
        }
        Debug.LogWarning($"[OVERTAKE VISUAL] Animator trigger '{triggerName}' not found in controller. " +
                          "Add it to the Animator to enable the reaction animation.");
    }
}
