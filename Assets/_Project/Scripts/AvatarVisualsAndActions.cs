using UnityEngine;

public class AvatarVisualsAndActions : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private MeshRenderer avatarRenderer; // Capsule Mesh Renderer

    [Header("Vital Sync (Bio-Luminescence)")]
    [SerializeField] private float baseIntensity = 1.0f;
    [SerializeField] private float pulseAmplitude = 1.5f;

    private Material _glowMaterial;
    private int _currentHeartRate = 60; // Baseline default

    // Color states from technical specification Section 4.1
    private Color _normalCyan = new Color(0.0f, 0.94f, 1.0f);   // Normal Bio-Luminescence
    private Color _amberWarning = new Color(1.0f, 0.62f, 0.0f); // 10m separation alert

    void Start()
    {
        if (avatarRenderer != null)
        {
            // Create a local instance of the material so we don't overwrite the project file
            _glowMaterial = avatarRenderer.material;
        }
    }

    void Update()
    {
        if (userCamera == null || _glowMaterial == null) return;

        // 1. Calculate Spatial Distance to User
        float distanceToUser = Vector3.Distance(transform.position, userCamera.position);

        // 2. Handle Autonomous Action Logic based on 10m separation
        Color targetBaseColor = _normalCyan;

        if (distanceToUser >= 10.0f)
        {
            // Requirement 4.1: 10m separation switches color to Amber
            targetBaseColor = _amberWarning;

            // Placeholder: Trigger "Hand Wave / Beckon" animation clip here
            // animator.SetBool("IsBeckoning", true);
        }
        else
        {
            // animator.SetBool("IsBeckoning", false);
        }

        // 3. Compute Bio-Luminescence Pulse Frequency using Heart Rate
        // Convert Beats Per Minute into a frequency multiplier (hz)
        float pulseFrequency = (_currentHeartRate / 60.0f) * Mathf.PI * 2.0f;

        // Use a sine wave to create a smooth, continuous glowing oscillation
        float sineWave = Mathf.Sin(Time.time * (pulseFrequency / 2.0f));
        float currentIntensity = baseIntensity + (sineWave * pulseAmplitude);

        // 4. Apply Final HDR Color and Light Intensity Matrix to the shader
        Color finalGlowColor = targetBaseColor * currentIntensity;
        _glowMaterial.SetColor("_EmissionColor", finalGlowColor);
    }

    // Public gateway method to feed data directly from your Apple Watch BLE script loop
    public void UpdateHeartRate(int newBpm)
    {
        _currentHeartRate = newBpm;
    }
}