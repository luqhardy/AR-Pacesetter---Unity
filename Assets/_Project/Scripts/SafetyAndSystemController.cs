using UnityEngine;
using UnityEngine.UI;

public class SafetyAndSystemController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameObject avatarContainer;       // The 3D avatar companion
    [SerializeField] private GameStateController stateController; // Our main state machine

    [Header("Safety UI Elements")]
    [SerializeField] private Image redFlashScreenOverlay;       // Screen-space UI overlay for danger
    [SerializeField] private GameObject minimalistHudPanel;     // Clean, high-contrast numeric panel

    [Header("Acoustic Alerting")]
    [SerializeField] private AudioSource alertAudioSource;
    [SerializeField] private AudioClip ttcWarningChime;

    [Header("System Tuning")]
    [SerializeField] private float ttcDangerThreshold = 1.5f;   // Trigger warning if collision is < 1.5s away

    private bool _hasEvacuatedDueToBattery = false;
    private bool _isTtcWarningActive = false;

    void Update()
    {
        // 1. Monitor Hardware Energy Levels (Requirement 6.2)
        // SystemInfo.batteryLevel returns a float value spanning 0.0 to 1.0
        float currentBatteryPercentage = SystemInfo.batteryLevel;

        // If battery drops below 10% (0.1f) and we haven't handled it yet, trigger safety evacuation
        if (currentBatteryPercentage > 0f && currentBatteryPercentage <= 0.1f && !_hasEvacuatedDueToBattery)
        {
            ExecuteLowBatteryEmergencyEvacuation();
        }

        // 2. Continuous Safety Proximity Auditing (Requirement 7.0)
        // For testing purposes, we simulate checking an obstacle ahead.
        // In your physical rollout, this parses your LiDAR spatial mesh delta arrays.
        EvaluateTimeToCollision();
    }

    private void ExecuteLowBatteryEmergencyEvacuation()
    {
        _hasEvacuatedDueToBattery = true;
        Debug.LogWarning("SYSTEM ALERT: Battery dropped below 10%. Activating safe HUD mode!");

        // Shut down the heavy 3D rendering container to minimize processor load
        if (avatarContainer != null)
        {
            avatarContainer.SetActive(false);
        }

        // Force the application state machine into static Standby
        if (stateController != null)
        {
            stateController.TransitionToState(GameStateController.ARVisionState.Standby);
        }

        // Enable your ultra-minimalist, high-contrast, text-only safe numerical UI overlay
        if (minimalistHudPanel != null)
        {
            minimalistHudPanel.SetActive(true);
        }
    }

    private void EvaluateTimeToCollision()
    {
        // Conceptual implementation of TTC logic:
        // TTC = Distance to obstacle / Closing relative velocity
        float simulatedDistanceToObstacle = 5.0f; // 5 meters away
        float runnerSprintingSpeed = 4.5f;        // 4.5 meters per second closing speed

        float calculatedTtc = simulatedDistanceToObstacle / runnerSprintingSpeed;

        // If the calculated window drops below our risk threshold, trigger the high-priority alert matrix
        if (calculatedTtc <= ttcDangerThreshold)
        {
            if (!_isTtcWarningActive)
            {
                TriggerCollisionEmergencyWarning(true);
            }

            // Pulsate the red hazard overlay on the screen to grab the runner's attention
            if (redFlashScreenOverlay != null)
            {
                float pingPongAlpha = Mathf.PingPong(Time.time * 4.0f, 0.6f); // Swift visual pulse modulation
                redFlashScreenOverlay.color = new Color(1f, 0f, 0f, pingPongAlpha);
            }
        }
        else if (_isTtcWarningActive)
        {
            TriggerCollisionEmergencyWarning(false);
        }
    }

    private void TriggerCollisionEmergencyWarning(bool activateAlert)
    {
        _isTtcWarningActive = activateAlert;

        if (activateAlert)
        {
            Debug.LogError("CRITICAL RISK: Imminent collision threat detected! Priority visual warning active.");

            // Execute physical audio chime looping sequences
            if (alertAudioSource != null && ttcWarningChime != null && !alertAudioSource.isPlaying)
            {
                alertAudioSource.clip = ttcWarningChime;
                alertAudioSource.loop = true;
                alertAudioSource.Play();
            }
        }
        else
        {
            // Clear warning parameters and restore normal visual state
            if (alertAudioSource != null)
            {
                alertAudioSource.Stop();
            }
            if (redFlashScreenOverlay != null)
            {
                redFlashScreenOverlay.color = new Color(1f, 0f, 0f, 0f); // Make fully transparent
            }
        }
    }
}