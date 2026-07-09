using UnityEngine;
using UnityEngine.UI;

public class SafetyAndSystemController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameObject avatarContainer;
    [SerializeField] private GameStateController stateController;
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Safety UI Elements")]
    [SerializeField] private Image redFlashScreenOverlay;
    [SerializeField] private GameObject minimalistHudPanel;

    [Header("Acoustic Alerting")]
    [SerializeField] private AudioSource alertAudioSource;
    [SerializeField] private AudioClip ttcWarningChime;

    [Header("TTC LiDAR Settings")]
    [SerializeField] private float ttcDangerThreshold = 1.5f;
    [SerializeField] private float ttcScanRadius = 0.4f;
    [SerializeField] private float ttcScanRange = 8.0f;
    [SerializeField] private LayerMask obstacleLayerMask = ~0;

    private bool _hasEvacuatedDueToBattery = false;
    private bool _isTtcWarningActive = false;
    private bool _simulateTtcThreat = false;

    private Vector3 _lastUserPos;
    private float _userSpeed = 0.0f;
    private bool _isUserPosInitialized = false;

    void Update()
    {
        // Track user speed
        if (userCamera != null)
        {
            if (!_isUserPosInitialized)
            {
                _lastUserPos = userCamera.position;
                _isUserPosInitialized = true;
            }
            else
            {
                Vector3 userMove = userCamera.position - _lastUserPos;
                userMove.y = 0;
                _userSpeed = userMove.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                _lastUserPos = userCamera.position;
            }
        }

        // Editor: toggle simulated TTC threat with T
        if (Input.GetKeyDown(KeyCode.T))
        {
            _simulateTtcThreat = !_simulateTtcThreat;
            Debug.Log($"[SIMULATOR] TTC threat simulation: {_simulateTtcThreat}");
        }

        // Battery evacuation (fires once)
        float battery = SystemInfo.batteryLevel;
        if (battery > 0f && battery <= 0.1f && !_hasEvacuatedDueToBattery)
            ExecuteLowBatteryEmergencyEvacuation();

        // Live TTC evaluation
        EvaluateTimeToCollision();
    }

    private void ExecuteLowBatteryEmergencyEvacuation()
    {
        _hasEvacuatedDueToBattery = true;
        Debug.LogWarning("[SYSTEM] Battery <10% - activating safe HUD mode.");

        if (avatarContainer != null) avatarContainer.SetActive(false);
        if (stateController != null)
            stateController.TransitionToState(GameStateController.ARVisionState.Standby);
        if (minimalistHudPanel != null) minimalistHudPanel.SetActive(true);
    }

    private void EvaluateTimeToCollision()
    {
        float distanceToObstacle;

        if (_simulateTtcThreat)
        {
            distanceToObstacle = 1.0f;
        }
        else if (userCamera != null)
        {
            Vector3 forward = userCamera.forward;
            forward.y = 0;
            forward.Normalize();

            RaycastHit hit;
            bool found = Physics.SphereCast(
                userCamera.position, ttcScanRadius,
                forward, out hit, ttcScanRange, obstacleLayerMask);

            distanceToObstacle = found ? hit.distance : ttcScanRange;
        }
        else
        {
            return;
        }

        float closingSpeed = (userCamera != null)
            ? _userSpeed
            : 3.5f;

        float ttc = distanceToObstacle / Mathf.Max(closingSpeed, 0.1f);

        if (ttc <= ttcDangerThreshold)
        {
            if (!_isTtcWarningActive) TriggerCollisionWarning(true);
            if (redFlashScreenOverlay != null)
                redFlashScreenOverlay.color = new Color(1f, 0f, 0f,
                    Mathf.PingPong(Time.time * 4.0f, 0.6f));
        }
        else if (_isTtcWarningActive)
        {
            TriggerCollisionWarning(false);
        }
    }

    private void TriggerCollisionWarning(bool activate)
    {
        _isTtcWarningActive = activate;

        if (activate)
        {
            Debug.LogError("[TTC ALERT] Imminent collision threat! Warning active.");
            if (alertAudioSource != null && ttcWarningChime != null && !alertAudioSource.isPlaying)
            {
                alertAudioSource.clip = ttcWarningChime;
                alertAudioSource.loop = true;
                alertAudioSource.Play();
            }

            // 企画書 §3 マルチモーダル通知: 危険時はスマホ振動も併用
            // (振動長の使い分けは iOS Haptics ネイティブ実装時に対応)
#if UNITY_IOS || UNITY_ANDROID
            Handheld.Vibrate();
#endif
        }
        else
        {
            if (alertAudioSource != null) alertAudioSource.Stop();
            if (redFlashScreenOverlay != null)
                redFlashScreenOverlay.color = new Color(1f, 0f, 0f, 0f);
        }
    }
}
