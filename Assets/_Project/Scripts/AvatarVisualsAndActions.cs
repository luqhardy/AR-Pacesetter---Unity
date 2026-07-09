using UnityEngine;

public class AvatarVisualsAndActions : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private MeshRenderer avatarRenderer; // Capsule Mesh Renderer

    private SkinnedMeshRenderer _avatarSkinnedRenderer;   // Added for VRChat model compatibility

    [Header("Vital Sync (Bio-Luminescence)")]
    [SerializeField] private float baseIntensity = 1.0f;
    [SerializeField] private float pulseAmplitude = 1.5f;

    [Header("Vital Warning (企画書 4.1 — 心拍過負荷)")]
    [Tooltip("BPM at or above which the avatar turns deep blue and performs the calm-down hand sign.")]
    [SerializeField] private int vitalWarningBpmThreshold = 185;
    [Tooltip("Optional avatar Animator. Trigger 'CalmDownSign' fires once per overload episode.")]
    [SerializeField] private Animator avatarAnimator;

    private Material _glowMaterial;
    private int _currentHeartRate = 60; // Baseline default
    private bool _vitalWarningActive = false;

    // Color states from technical specification Section 4.1
    private Color _normalCyan = new Color(0.0f, 0.94f, 1.0f);   // Normal Bio-Luminescence
    private Color _amberWarning = new Color(1.0f, 0.62f, 0.0f); // 10m separation alert
    private Color _deepBlueVital = new Color(0.05f, 0.15f, 0.9f); // HR overload "calm down" state

    public bool IsVitalWarningActive => _vitalWarningActive;

    void Start()
    {
        RefreshMaterialReference();
    }

    void Update()
    {
        if (userCamera == null || _glowMaterial == null) return;

        // 1. Calculate Spatial Distance to User (Horizontal X-Z plane only to remove vertical height bias)
        Vector3 userPosHorizontal = new Vector3(userCamera.position.x, 0f, userCamera.position.z);
        Vector3 avatarPosHorizontal = new Vector3(transform.position.x, 0f, transform.position.z);
        float distanceToUser = Vector3.Distance(userPosHorizontal, avatarPosHorizontal);

        // 2. Handle Autonomous Action Logic based on 10m separation
        Color targetBaseColor = _normalCyan;

        if (distanceToUser >= 10.0f)
        {
            // Requirement 4.1: 10m separation switches color to Amber
            targetBaseColor = _amberWarning;
        }

        // 2b. Vital warning takes priority: HR overload turns avatar deep blue
        //     and plays the "calm down" hand sign once per episode (企画書 4.1)
        if (_currentHeartRate >= vitalWarningBpmThreshold)
        {
            targetBaseColor = _deepBlueVital;

            if (!_vitalWarningActive)
            {
                _vitalWarningActive = true;
                Debug.LogWarning($"[VITAL WARNING] HR {_currentHeartRate} BPM >= {vitalWarningBpmThreshold}. Deep-blue state + calm-down sign.");
                if (avatarAnimator != null)
                    avatarAnimator.SetTrigger("CalmDownSign");
            }
        }
        else if (_vitalWarningActive && _currentHeartRate < vitalWarningBpmThreshold - 5)
        {
            // 5 BPM hysteresis so the color does not flicker at the threshold
            _vitalWarningActive = false;
        }

        // 3. Compute Bio-Luminescence Pulse Frequency using Heart Rate
        float pulseFrequency = (_currentHeartRate / 60.0f) * Mathf.PI * 2.0f;

        // Use a sine wave to create a smooth, continuous glowing oscillation
        float sineWave = Mathf.Sin(Time.time * (pulseFrequency / 2.0f));
        float currentIntensity = baseIntensity + (sineWave * pulseAmplitude);

        // 4. Apply Final HDR Color and Light Intensity Matrix to the shader
        Color finalGlowColor = targetBaseColor * currentIntensity;
        _glowMaterial.SetColor("_EmissionColor", finalGlowColor);
    }

    // --- THE MISSING LINK FOR MODEL SWITCHING ---
    public void UpdateActiveRenderer(MeshRenderer staticMesh, SkinnedMeshRenderer skinnedMesh)
    {
        avatarRenderer = staticMesh;
        _avatarSkinnedRenderer = skinnedMesh;

        // Reset the cached material reference so it grabs from the new renderer
        _glowMaterial = null; 
        RefreshMaterialReference();
    }

    private void RefreshMaterialReference()
    {
        if (_glowMaterial != null) return;

        if (avatarRenderer != null)
        {
            _glowMaterial = avatarRenderer.material;
        }
        else if (_avatarSkinnedRenderer != null)
        {
            _glowMaterial = _avatarSkinnedRenderer.material;
        }

        if (_glowMaterial != null)
        {
            _glowMaterial.EnableKeyword("_EMISSION");
        }
    }

    // Public gateway method to feed data directly from your Apple Watch BLE script loop
    public void UpdateHeartRate(int newBpm)
    {
        _currentHeartRate = newBpm;
    }
}