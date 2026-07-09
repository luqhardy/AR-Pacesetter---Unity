using System;
using UnityEngine;

/// <summary>
/// Swift → Unity 受信ブリッジ (AR-runner の UnityBridge.swift 契約)。
/// GameObject名は必ず "ARSessionManager"(Swift側の sendMessageToGO ターゲット)。
///
/// 受信コマンド:
///   StartSession  { targetPaceKmH, distanceKm, avatarHeightCm, forwardOffsetM }
///   UpdateMetrics { paceKmH, heartRate, distanceKm }
///   EndSession    {}
///
/// また、走行中は 1Hz で SyncRate / AvatarState / GPS / Latency をSwiftへ送信する。
/// </summary>
public class ARSessionManagerBridge : MonoBehaviour
{
    public const string RequiredGameObjectName = "ARSessionManager";

    [Serializable]
    private class SwiftCommand
    {
        public string command;
        public double targetPaceKmH;
        public double distanceKm;
        public double paceKmH;
        public int heartRate;
        public int avatarHeightCm;
        public double forwardOffsetM;
    }

    [Header("Engine Links (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private AnalyticsManager analytics;
    [SerializeField] private RunSessionController sessionController;
    [SerializeField] private HeartRateReceiver heartRateReceiver;
    [SerializeField] private PaceCalibrationController paceCalibration;
    [SerializeField] private GameStateController gameStateController;

    private const float ReportIntervalSeconds = 1.0f;
    private const float BaselineAvatarHeightCm = 175f; // 企画書 §4.1

    private float _nextReportTime;
    private float _smoothedFrameMs = 16.6f;
    private string _lastSentAvatarState = "";
    private bool _gpsWasLost = false;
    private bool _sessionDriven = false; // true once Swift has issued StartSession

    void Awake()
    {
        if (gameObject.name != RequiredGameObjectName)
            Debug.LogWarning($"[SWIFT BRIDGE] GameObject must be named '{RequiredGameObjectName}' for UnitySendMessage to reach it (current: '{gameObject.name}').");

        if (avatarEngine == null) avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (analytics == null) analytics = FindFirstObjectByType<AnalyticsManager>(FindObjectsInactive.Include);
        if (sessionController == null) sessionController = FindFirstObjectByType<RunSessionController>(FindObjectsInactive.Include);
        if (heartRateReceiver == null) heartRateReceiver = FindFirstObjectByType<HeartRateReceiver>(FindObjectsInactive.Include);
        if (paceCalibration == null) paceCalibration = FindFirstObjectByType<PaceCalibrationController>(FindObjectsInactive.Include);
        if (gameStateController == null) gameStateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
    }

    // ── Swift → Unity エントリポイント ───────────────────────────────────────
    // Swift: sendMessageToGO(withName: "ARSessionManager", functionName: "OnSwiftCommand", message: json)
    public void OnSwiftCommand(string json)
    {
        SwiftCommand cmd;
        try
        {
            cmd = JsonUtility.FromJson<SwiftCommand>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SWIFT BRIDGE] Failed to parse command JSON: {e.Message}\n{json}");
            return;
        }
        if (cmd == null || string.IsNullOrEmpty(cmd.command)) return;

        switch (cmd.command)
        {
            case "StartSession": HandleStartSession(cmd); break;
            case "UpdateMetrics": HandleUpdateMetrics(cmd); break;
            case "EndSession": HandleEndSession(); break;
            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown command: {cmd.command}");
                break;
        }
    }

    private void HandleStartSession(SwiftCommand cmd)
    {
        if (avatarEngine == null)
        {
            Debug.LogError("[SWIFT BRIDGE] StartSession ignored — AvatarEngine not found.");
            return;
        }

        _sessionDriven = true;

        // km/h → 分/km 変換 (例: 12km/h → 5:00/km)
        if (cmd.targetPaceKmH > 0.1)
        {
            float minutesPerKm = Mathf.Clamp((float)(60.0 / cmd.targetPaceKmH), 3.0f, 12.0f);
            avatarEngine.UpdateTargetPace(minutesPerKm);
        }

        if (cmd.forwardOffsetM > 0.1)
            avatarEngine.SetLeadDistance((float)cmd.forwardOffsetM);

        // アバター身長: 175cm基準の相対スケール
        if (cmd.avatarHeightCm > 0)
            avatarEngine.transform.localScale = Vector3.one * (cmd.avatarHeightCm / BaselineAvatarHeightCm);

        // Swift側がオンボーディング/設定UIを持つため、Unityのセットアップ画面は閉じる
        if (paceCalibration != null)
            paceCalibration.HideSetupUiForExternalControl();

        if (sessionController != null)
            sessionController.OnRunStarted(showUnityUi: false);

        avatarEngine.StartPacing();

        SendAvatarStateIfChanged("Run");
        Debug.Log($"[SWIFT BRIDGE] StartSession — pace {cmd.targetPaceKmH}km/h, goal {cmd.distanceKm}km, lead {cmd.forwardOffsetM}m.");
    }

    private void HandleUpdateMetrics(SwiftCommand cmd)
    {
        // 心拍はBLE受信と同じ入口に流す(アバター発光/HUD/バイタル警告が連動)
        if (heartRateReceiver != null && cmd.heartRate > 0)
            heartRateReceiver.OnHeartRateDataReceived(cmd.heartRate.ToString());

        // 実機ではSwift(CoreLocation)の距離が正 — スプリット判定に供給
        if (analytics != null && cmd.distanceKm > 0)
            analytics.CheckDistanceIntervalSplits((float)(cmd.distanceKm * 1000.0));
    }

    private void HandleEndSession()
    {
        RunSessionRecord record = sessionController != null
            ? sessionController.FinishRunExternal()
            : null;

        SendAvatarStateIfChanged("Goal");
        SwiftMessageSender.SendSessionResult(record);
        Debug.Log("[SWIFT BRIDGE] EndSession — result sent to Swift.");
    }

    // ── Unity → Swift 定期レポート (1Hz) ─────────────────────────────────────
    void Update()
    {
        _smoothedFrameMs = Mathf.Lerp(_smoothedFrameMs, Time.deltaTime * 1000f, 0.1f);

        ReportGpsTransitions();

        if (Time.time < _nextReportTime) return;
        _nextReportTime = Time.time + ReportIntervalSeconds;

        if (avatarEngine == null || !avatarEngine.HasStarted) return;

        if (analytics != null)
            SwiftMessageSender.SendSyncRate(Mathf.RoundToInt(analytics.GetLiveSyncRate()));

        SwiftMessageSender.SendLatency(_smoothedFrameMs);
        SendAvatarStateIfChanged(DeriveAvatarState());
    }

    private void ReportGpsTransitions()
    {
        if (gameStateController == null) return;

        bool gpsLost = gameStateController.currentState == GameStateController.ARVisionState.InertialMovement
                    || gameStateController.currentState == GameStateController.ARVisionState.FadeOut
                    || gameStateController.currentState == GameStateController.ARVisionState.Standby;

        if (gpsLost && !_gpsWasLost)
        {
            _gpsWasLost = true;
            SwiftMessageSender.SendGpsLost();
        }
        else if (!gpsLost && _gpsWasLost)
        {
            _gpsWasLost = false;
            SwiftMessageSender.SendGpsRecovered();
        }
    }

    // Swift側 AvatarState enum (Idle/Run/Slow/Fast/Goal/Lost) への写像
    private string DeriveAvatarState()
    {
        if (avatarEngine == null || !avatarEngine.HasStarted) return "Idle";
        if (avatarEngine.IsSessionEnded) return "Goal";
        if (_gpsWasLost) return "Lost";

        float baseSpeed = avatarEngine.GetBaseTargetSpeed();
        if (baseSpeed > 0.01f)
        {
            float ratio = avatarEngine.GetTargetSpeed() / baseSpeed;
            if (ratio < 0.95f) return "Slow"; // ユーザー遅れ → アバター減速中
            if (ratio > 1.05f) return "Fast"; // 追い上げ/スプリント中
        }
        return "Run";
    }

    private void SendAvatarStateIfChanged(string state)
    {
        if (state == _lastSentAvatarState) return;
        _lastSentAvatarState = state;
        SwiftMessageSender.SendAvatarState(state);
    }

    /// <summary>Swift主導セッションかどうか(Unity内UIの抑制判定に使用可)。</summary>
    public bool IsExternallyDriven => _sessionDriven;

#if UNITY_EDITOR
    // エディタ検証: Inspectorの右クリックメニューからSwiftコマンドをシミュレート
    [ContextMenu("Simulate StartSession (12km/h = 5:00/km)")]
    private void SimulateStartSession()
        => OnSwiftCommand("{\"command\":\"StartSession\",\"targetPaceKmH\":12.0,\"distanceKm\":5.0,\"avatarHeightCm\":175,\"forwardOffsetM\":3.0}");

    [ContextMenu("Simulate UpdateMetrics (HR 150)")]
    private void SimulateUpdateMetrics()
        => OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":11.5,\"heartRate\":150,\"distanceKm\":1.2}");

    [ContextMenu("Simulate EndSession")]
    private void SimulateEndSession()
        => OnSwiftCommand("{\"command\":\"EndSession\"}");
#endif
}
