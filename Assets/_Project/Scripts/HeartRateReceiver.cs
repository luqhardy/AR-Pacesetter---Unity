using System.Runtime.InteropServices;
using UnityEngine;

public class HeartRateReceiver : MonoBehaviour
{
    [Header("Pipelines")]
    [SerializeField] private AvatarVisualsAndActions visualsEngine;
    [SerializeField] private PeripheralHUDManager hudManager;

    // Native iOS Objective-C++ Bridge Binding
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void StartHeartRateBLEScan();

    [DllImport("__Internal")]
    private static extern void StopHeartRateBLEScan();
#endif

    private void Start()
    {
#if UNITY_IOS && !UNITY_EDITOR
        // Boot up native Apple CoreBluetooth stack on app launch
        StartHeartRateBLEScan();
#else
        Debug.Log("[BLE SIMULATOR] Running on Windows Editor. Simulating Bluetooth hardware connection...");
#endif
    }

    // This specific method name is targeted by our native iOS Objective-C++ file
    public void OnHeartRateDataReceived(string rawBpmString)
    {
        if (int.TryParse(rawBpmString, out int cleanBpm))
        {
            // Discard invalid zero readings (common transient BLE drops)
            if (cleanBpm <= 0) return;

            // Clamp to standard human physiological bounds to protect metrics
            cleanBpm = Mathf.Clamp(cleanBpm, 40, 220);

            Debug.Log($"[BIOMETRIC INGESTION] Live BLE Heart Rate Update: {cleanBpm} BPM");

            // 1. Route to the Avatar Engine to accelerate/decelerate the color pulse frequency
            if (visualsEngine != null)
            {
                visualsEngine.UpdateHeartRate(cleanBpm);
            }

            // 2. Route to the Peripheral HUD to display the actual data on screen
            if (hudManager != null)
            {
                hudManager.UpdateLiveHeartRate(cleanBpm);
            }
        }
    }

    // This specific method name is targeted by watch running pitch updates (Requirement 2 & 4.3)
    public void OnRunningPitchReceived(string rawPitchString)
    {
        if (float.TryParse(rawPitchString, out float cleanPitch))
        {
            // Discard invalid zero readings
            if (cleanPitch <= 0.01f) return;

            // Clamp to normal human running cadences
            cleanPitch = Mathf.Clamp(cleanPitch, 50f, 250f);

            Debug.Log($"[BIOMETRIC INGESTION] Live Apple Watch Running Pitch: {cleanPitch:F1} SPM");

            // Route to the Peripheral HUD to display the actual running pitch cadence
            if (hudManager != null)
            {
                hudManager.UpdateLivePitch(cleanPitch);
            }
        }
    }

    private void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        StopHeartRateBLEScan();
#endif
    }
}
