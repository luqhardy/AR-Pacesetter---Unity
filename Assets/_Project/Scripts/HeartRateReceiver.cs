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

    // 第1期(iPhone + グラスのみ)は心拍BLEがスコープ外なので既定OFF。
    // ONのまま起動するとBluetooth許可ダイアログが出て、走行中ずっと無線を使う(§10 60分稼働)。
    [Tooltip("起動時に心拍/ケイデンスのBLEセンサーを探す(第1期スコープ外・既定OFF)")]
    [SerializeField] private bool scanOnStart = false;

    private bool _scanning;

    private void Start()
    {
        if (scanOnStart) StartScan();
    }

    /// <summary>心拍/ケイデンスのBLEセンサー探索を始める。繋ぐのは近くの1台だけ(HeartRatePlugin.mm)。</summary>
    public void StartScan()
    {
        if (_scanning) return;
        _scanning = true;
#if UNITY_IOS && !UNITY_EDITOR
        StartHeartRateBLEScan();
#else
        Debug.Log("[BLE SIMULATOR] Running in the editor. Simulating Bluetooth hardware connection...");
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
        if (!_scanning) return;
        _scanning = false;
#if UNITY_IOS && !UNITY_EDITOR
        StopHeartRateBLEScan();
#endif
    }
}
