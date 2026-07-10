using UnityEngine;
using TMPro;

/// <summary>
/// Readyチェック機能 (企画書 §5):
/// スマホ・ARグラス（必須）、Watch・イヤホン（任意）の接続状況を
/// 4色インジケーター（緑=接続 / 琥珀=検索中 / 赤=未接続 / 灰=任意オフ）で表示し、
/// 必須デバイスが揃うまで走行開始をブロックする。
///
/// エディタ検証キー: F1=ARグラス切替, F2=Watch切替, F3=イヤホン切替
/// 実機では XREAL SDK / CoreBluetooth の接続コールバックから SetDeviceState を呼ぶ。
/// </summary>
public class ReadyCheckController : MonoBehaviour
{
    public enum DeviceState { Disconnected, Searching, Connected, OptionalOff }

    [Header("Simulated Handshake")]
    [Tooltip("Seconds the simulated AR-glass handshake takes in the editor.")]
    [SerializeField] private float simulatedGlassConnectSeconds = 1.5f;

    private DeviceState _phone = DeviceState.Connected; // self — always connected
    private DeviceState _glass = DeviceState.Searching;
    private DeviceState _watch = DeviceState.OptionalOff;
    private DeviceState _earphone = DeviceState.OptionalOff;

    private TextMeshProUGUI _indicatorLabel;
    private float _bootTime;

    /// <summary>必須デバイス（スマホ＋ARグラス）が接続済みなら true。</summary>
    public bool AllRequiredReady =>
        _phone == DeviceState.Connected && _glass == DeviceState.Connected;

    void Start()
    {
        _bootTime = Time.time;
#if !(UNITY_IOS && !UNITY_EDITOR)
        // 実機(UaaL)ではReadyチェックはSwift側のDeviceConnectViewが担当 —
        // グラス視界にUnityのインジケーターを重ねない
        BuildIndicatorUI();
#endif
        RefreshIndicator();
    }

    void Update()
    {
        // Simulated glass handshake completes after a short delay
        if (_glass == DeviceState.Searching &&
            Time.time - _bootTime >= simulatedGlassConnectSeconds)
        {
            _glass = DeviceState.Connected;
            Debug.Log("[READY CHECK] AR Glass connected (simulated handshake).");
            RefreshIndicator();
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1))
        {
            _glass = _glass == DeviceState.Connected ? DeviceState.Disconnected : DeviceState.Connected;
            RefreshIndicator();
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            _watch = _watch == DeviceState.Connected ? DeviceState.OptionalOff : DeviceState.Connected;
            RefreshIndicator();
        }
        if (Input.GetKeyDown(KeyCode.F3))
        {
            _earphone = _earphone == DeviceState.Connected ? DeviceState.OptionalOff : DeviceState.Connected;
            RefreshIndicator();
        }
#endif
    }

    /// <summary>Production entry point for native connectivity callbacks.</summary>
    public void SetDeviceState(string device, DeviceState state)
    {
        switch (device.ToLowerInvariant())
        {
            case "phone": _phone = state; break;
            case "glass": _glass = state; break;
            case "watch": _watch = state; break;
            case "earphone": _earphone = state; break;
        }
        RefreshIndicator();
    }

    private void BuildIndicatorUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) return;

        GameObject go = new GameObject("ReadyCheckIndicator", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(canvas.transform, false);

        _indicatorLabel = go.AddComponent<TextMeshProUGUI>();
        _indicatorLabel.fontSize = 16;
        _indicatorLabel.alignment = TextAlignmentOptions.TopLeft;
        _indicatorLabel.raycastTarget = false;
        _indicatorLabel.richText = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16f, -16f);
        rt.sizeDelta = new Vector2(360f, 110f);
    }

    private void RefreshIndicator()
    {
        if (_indicatorLabel == null) return;

        _indicatorLabel.text =
            "<b>READY CHECK</b>\n" +
            $"{Dot(_phone)} iPhone   {Dot(_glass)} AR Glass\n" +
            $"{Dot(_watch)} Watch <size=12>(opt)</size>   {Dot(_earphone)} Earphone <size=12>(opt)</size>";
    }

    private static string Dot(DeviceState state)
    {
        string hex = state switch
        {
            DeviceState.Connected => "#3DDC84",   // green
            DeviceState.Searching => "#FFB300",   // amber
            DeviceState.Disconnected => "#FF4444",// red
            _ => "#888888"                        // gray (optional, off)
        };
        return $"<color={hex}>●</color>";
    }
}
