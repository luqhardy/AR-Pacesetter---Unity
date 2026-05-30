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

    private void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        StopHeartRateBLEScan();
#endif
    }
}