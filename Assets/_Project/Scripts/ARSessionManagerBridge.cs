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
        public string mode;         // "pace"(既定) | "ghost"
        public string ghostDateIso; // mode=ghost時: 競走相手のセッションdateIso
        // UpdateMetrics の測位サンプル(§8.1 ロスト判定 / §5.2 CSVログ用)。
        // gpsAccuracy > 0 が有効サンプルの目印(CoreLocation同様、負値/未送信は無効)
        public double gpsLatitude;
        public double gpsLongitude;
        public float gpsAccuracy;
        // true only when CoreLocation delivered a genuinely new fix. Cached timer
        // retransmissions must not refresh Unity's 5-second freshness windows.
        public bool locationSampleFresh;
        public bool speedSampleValid;
    }

    [Header("Engine Links (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private AnalyticsManager analytics;
    [SerializeField] private RunSessionController sessionController;
    [SerializeField] private HeartRateReceiver heartRateReceiver;
    [SerializeField] private PaceCalibrationController paceCalibration;
    [SerializeField] private GameStateController gameStateController;
    [SerializeField] private PeripheralHUDManager hudManager;
    [SerializeField] private LatencyBenchmarkRunner latencyRunner;
    [SerializeField] private GhostPaceDriver ghostDriver;
    [SerializeField] private GpsSignalMonitor gpsMonitor;
    [SerializeField] private GoalLineController goalLineController;
    [SerializeField] private RunnerTrackingState runnerTracking;

    private const float ReportIntervalSeconds = 1.0f;
    private const float BaselineAvatarHeightCm = 175f; // 企画書 §4.1

    private float _nextReportTime;
    private float _smoothedFrameMs = 16.6f;
    private string _lastSentAvatarState = "";
    private bool _gpsWasLost = false;
    private bool _sessionDriven = false; // true once Swift has issued StartSession

    // 目標距離ゴール判定 (StartSessionのdistanceKm)
    private double _goalDistanceMeters = 0;

    // 実測ペース(Swift/CoreLocation)。F-07の「現在ペース」表示に使う。
    // 距離と同じく、途絶えたら古い値を使い続けないよう鮮度で失効させる
    private float _swiftReportedPaceKmH;
    private float _swiftPaceReceivedTime = -999f;
    private const float MeasuredPaceFreshnessSeconds = 5f;

    /// <summary>
    /// Swiftから供給された実測ペース(km/h)。未受信または鮮度切れなら0。
    /// 0のときUnity側は自前のカメラ移動量から算出したペースへフォールバックする。
    /// </summary>
    public float MeasuredPaceKmH =>
        (Time.time - _swiftPaceReceivedTime) <= MeasuredPaceFreshnessSeconds
            ? _swiftReportedPaceKmH
            : 0f;
    private double _swiftReportedDistanceMeters = 0;
    private bool _goalReached = false;
    // CoreLocation starts before the visual countdown so GPS can settle. Keep
    // the raw total for calibration, then subtract the value captured at START.
    private double _latestRawDistanceMeters = 0;
    private double _runStartDistanceBaselineMeters = 0;
    private bool _runDistanceBaselineCaptured = false;
    private bool _previousRunMotionActive = false;

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
        if (hudManager == null) hudManager = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (latencyRunner == null) latencyRunner = FindFirstObjectByType<LatencyBenchmarkRunner>(FindObjectsInactive.Include);
        if (ghostDriver == null) ghostDriver = FindFirstObjectByType<GhostPaceDriver>(FindObjectsInactive.Include);
        if (gpsMonitor == null) gpsMonitor = FindFirstObjectByType<GpsSignalMonitor>(FindObjectsInactive.Include);
        if (goalLineController == null) goalLineController = FindFirstObjectByType<GoalLineController>(FindObjectsInactive.Include);
        if (runnerTracking == null) runnerTracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
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
            case "RequestHistory": HandleRequestHistory(); break;
            case "ResumeSession": HandleResumeSession(); break;
            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown command: {cmd.command}");
                break;
        }
    }

    private void HandleStartSession(SwiftCommand cmd)
    {
        ResolveRunnerTracking();
        if (avatarEngine == null)
        {
            Debug.LogError("[SWIFT BRIDGE] StartSession ignored — AvatarEngine not found.");
            return;
        }

        _sessionDriven = true;

        // 再走行: 前セッションが終了済みなら全コンポーネントをリセットしてから開始
        if (sessionController != null && sessionController.IsFinished)
        {
            sessionController.ResetForNewSession();
            _lastSentAvatarState = ""; // Idle→Run遷移を再送させる
        }

        // リセットで false に戻るため、必ずリセット後に立てる
        ExternalMetricsActive = true;

        // 目標距離: 到達したらUnity側から自動終了する (SessionEnded送信)
        _goalDistanceMeters = cmd.distanceKm > 0 ? cmd.distanceKm * 1000.0 : 0;
        _swiftReportedDistanceMeters = 0;
        _latestRawDistanceMeters = 0;
        _runStartDistanceBaselineMeters = 0;
        _runDistanceBaselineCaptured = false;
        _previousRunMotionActive = false;
        _goalReached = false;
        if (goalLineController != null)
            goalLineController.ConfigureGoal(_goalDistanceMeters);
        if (runnerTracking != null)
            runnerTracking.BeginSession((float)cmd.targetPaceKmH, cmd.distanceKm);

        // km/h → 分/km 変換 (例: 12km/h → 5:00/km)
        if (cmd.targetPaceKmH > 0.1)
        {
            float minutesPerKm = Mathf.Clamp((float)(60.0 / cmd.targetPaceKmH), 3.0f, 12.0f);
            avatarEngine.UpdateTargetPace(minutesPerKm);
        }

        // ゴーストモード (企画書§3): 過去セッションの速度プロファイルでアバターを駆動
        if (ghostDriver != null)
        {
            if (cmd.mode == "ghost" && !string.IsNullOrEmpty(cmd.ghostDateIso))
            {
                RunSessionRecord ghost = SessionDataStore.LoadSessionByDateIso(cmd.ghostDateIso);
                if (ghost != null)
                    ghostDriver.Activate(ghost);
                else
                    Debug.LogWarning($"[SWIFT BRIDGE] Ghost session not found: {cmd.ghostDateIso} — falling back to pace mode.");
            }
            else
            {
                ghostDriver.Deactivate();
            }
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

        // 実測Motion-to-Photonのバックグラウンド計測を開始 (LatencyReportに使用)
        if (latencyRunner != null)
            latencyRunner.SetContinuousMeasurement(true);

        SendAvatarStateIfChanged("Run");
        Debug.Log($"[SWIFT BRIDGE] StartSession — pace {cmd.targetPaceKmH}km/h, goal {cmd.distanceKm}km, lead {cmd.forwardOffsetM}m.");
    }

    private void HandleUpdateMetrics(SwiftCommand cmd)
    {
        ResolveRunnerTracking();
        // Only Swift's explicit acceptance flag refreshes distance/pace. Raw GPS
        // accuracy is still forwarded to the signal-loss FSM, including bad fixes.
        bool freshLocationSample = cmd.locationSampleFresh;
        bool validSpeedSample = cmd.speedSampleValid;
#if UNITY_EDITOR
        // Existing ContextMenu/E2E JSON predates locationSampleFresh. Keep those
        // deterministic editor-only commands useful without weakening device semantics.
        if (!freshLocationSample && (cmd.paceKmH > 0 || cmd.distanceKm > 0))
            freshLocationSample = true;
        if (!validSpeedSample && cmd.paceKmH > 0)
            validSpeedSample = true;
#endif

        bool runMotionActive = avatarEngine != null && avatarEngine.IsRunMotionActive;
        double distanceFromStartKm = 0.0;
        if (freshLocationSample && cmd.distanceKm >= 0.0)
        {
            double previousRawDistanceMeters = _latestRawDistanceMeters;
            _latestRawDistanceMeters = Math.Max(0.0, cmd.distanceKm * 1000.0);

            // If a location callback lands before this MonoBehaviour's Update on
            // the START frame, use the last pre-callback total as the baseline.
            if (runMotionActive && !_runDistanceBaselineCaptured)
                CaptureRunDistanceBaseline(previousRawDistanceMeters);

            if (runMotionActive)
            {
                distanceFromStartKm = Math.Max(
                    0.0, _latestRawDistanceMeters - _runStartDistanceBaselineMeters) / 1000.0;
            }
        }

        if (runnerTracking != null)
        {
            runnerTracking.ReportMetrics(
                (float)cmd.paceKmH, cmd.heartRate, distanceFromStartKm,
                cmd.gpsLatitude, cmd.gpsLongitude, cmd.gpsAccuracy,
                freshLocationSample, validSpeedSample, cmd.gpsAccuracy > 0f);
        }

        // 測位サンプル(§8.1 ロスト自動判定 / §5.2 CSVログのGPS列)。
        // gpsAccuracy > 0 のときのみ有効サンプルとして扱う
        if (gpsMonitor != null && cmd.gpsAccuracy > 0f)
            gpsMonitor.ReportGpsUpdate(cmd.gpsLatitude, cmd.gpsLongitude, cmd.gpsAccuracy);

        // 実測ペース(F-07 現在ペース表示用)。0以下は無効サンプルとして無視する
        if (freshLocationSample && validSpeedSample)
        {
            _swiftReportedPaceKmH = Mathf.Max(0f, (float)cmd.paceKmH);
            _swiftPaceReceivedTime = Time.time;
        }

        // 心拍はBLE受信と同じ入口に流す(アバター発光/HUD/バイタル警告が連動)
        if (heartRateReceiver != null && cmd.heartRate > 0)
            heartRateReceiver.OnHeartRateDataReceived(cmd.heartRate.ToString());

        // 実機ではSwift(CoreLocation)の距離が正 — スプリット/ゴール判定・記録に供給
        if (freshLocationSample && runMotionActive)
        {
            _swiftReportedDistanceMeters = distanceFromStartKm * 1000.0;
            if (analytics != null)
                analytics.CheckDistanceIntervalSplits((float)_swiftReportedDistanceMeters);
            if (sessionController != null)
                sessionController.ExternalDistanceMeters = _swiftReportedDistanceMeters;
            CheckGoalReached();
        }
    }

    private void HandleEndSession()
    {
        if (!_goalReached && goalLineController != null)
            goalLineController.HideImmediately();

        if (latencyRunner != null)
            latencyRunner.SetContinuousMeasurement(false);

        if (ghostDriver != null)
            ghostDriver.Deactivate();

        RunSessionRecord record = sessionController != null
            ? sessionController.FinishRunExternal()
            : null;

        if (runnerTracking != null)
            runnerTracking.EndSession();

        SendAvatarStateIfChanged("Goal");
        SwiftMessageSender.SendSessionResult(record);
        Debug.Log("[SWIFT BRIDGE] EndSession — result sent to Swift.");
    }

    /// <summary>
    /// §8.3: グラス切断でスタンバイ中の走行を、準備画面での再スタート操作後に再開する。
    /// 新規セッションは開始せず(記録・CSVログは継続)、表示状態のみNormalへ戻す。
    /// </summary>
    private void HandleResumeSession()
    {
        if (avatarEngine == null || !avatarEngine.HasStarted || avatarEngine.IsSessionEnded)
        {
            Debug.LogWarning("[SWIFT BRIDGE] ResumeSession — 再開できる走行がありません。");
            return;
        }

        if (gameStateController != null)
            gameStateController.TransitionToState(GameStateController.ARVisionState.Normal);

        SendAvatarStateIfChanged("Run");
        Debug.Log("[SWIFT BRIDGE] ResumeSession — スタンバイから通常追従へ復帰。");
    }

    private void HandleRequestHistory()
    {
        var records = SessionDataStore.LoadAllSessions();
        // 新しい順・最大20件 (ファイル名はタイムスタンプ順)
        records.Reverse();
        if (records.Count > 20)
            records.RemoveRange(20, records.Count - 20);

        SwiftMessageSender.SendHistory(records);
        Debug.Log($"[SWIFT BRIDGE] RequestHistory — {records.Count}件を送信。");
    }

    // ── Unity → Swift 定期レポート (1Hz) ─────────────────────────────────────
    void Update()
    {
        _smoothedFrameMs = Mathf.Lerp(_smoothedFrameMs, Time.deltaTime * 1000f, 0.1f);

        ReportGpsTransitions();
        UpdateRunMotionBoundary();

        if (Time.time < _nextReportTime) return;
        _nextReportTime = Time.time + ReportIntervalSeconds;

        // 走行中のみレポート(終了後に古いSyncRate/Latencyを送り続けない)
        if (avatarEngine == null || !avatarEngine.IsRunMotionActive) return;

        if (analytics != null)
            SwiftMessageSender.SendSyncRate(Mathf.RoundToInt(analytics.GetLiveSyncRate()));

        // 実測M2P(LatencyBenchmarkRunnerのローリング平均)を優先、
        // 未計測時は平滑化フレーム時間にフォールバック
        double measuredM2p = latencyRunner != null ? latencyRunner.AverageTotalMs : -1.0;
        SwiftMessageSender.SendLatency(measuredM2p > 0 ? measuredM2p : _smoothedFrameMs);
        SendAvatarStateIfChanged(DeriveAvatarState());

        // エディタ/スタンドアロン走行ではUnity自身の距離計測でもゴール判定する
        CheckGoalReached();
    }

    private void CheckGoalReached()
    {
        if (_goalReached || _goalDistanceMeters <= 0) return;
        if (avatarEngine == null || !avatarEngine.IsRunMotionActive) return;

        // 記録と同じ距離ポリシー(新鮮なGPS優先→Unity計測)を使い、
        // ゴール判定と記録距離の不一致(記録が目標未満になる等)を防ぐ
        double bestDistance = sessionController != null
            ? sessionController.AuthoritativeDistanceMeters
            : Math.Max(hudManager != null ? hudManager.DistanceMeters : 0, _swiftReportedDistanceMeters);
        if (goalLineController != null)
            goalLineController.UpdateProgress(bestDistance);
        if (bestDistance < _goalDistanceMeters) return;

        _goalReached = true;
        if (goalLineController != null)
            goalLineController.MarkReached();
        Debug.Log($"[SWIFT BRIDGE] 目標距離 {_goalDistanceMeters / 1000.0:F2}km 到達 — セッションを自動終了します。");
        HandleEndSession();
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
        if (avatarEngine.IsWaitingForUser) return "Slow"; // 離隔待機(手招き)中

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

    private void ResolveRunnerTracking()
    {
        if (runnerTracking == null)
            runnerTracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
    }

    private void UpdateRunMotionBoundary()
    {
        bool active = avatarEngine != null && avatarEngine.IsRunMotionActive;
        if (active && !_previousRunMotionActive)
            CaptureRunDistanceBaseline(_latestRawDistanceMeters);
        _previousRunMotionActive = active;
    }

    private void CaptureRunDistanceBaseline(double rawDistanceMeters)
    {
        if (_runDistanceBaselineCaptured)
            return;

        _runStartDistanceBaselineMeters = Math.Max(0.0, rawDistanceMeters);
        _runDistanceBaselineCaptured = true;
        _swiftReportedDistanceMeters = 0.0;
        if (sessionController != null)
            sessionController.ExternalDistanceMeters = -1.0;
        Debug.Log($"[SWIFT BRIDGE] START distance baseline captured at {_runStartDistanceBaselineMeters:F2}m.");
    }

    /// <summary>Swift主導セッションかどうか(Unity内UIの抑制判定に使用可)。</summary>
    public bool IsExternallyDriven => _sessionDriven;

    /// <summary>
    /// Swift(CoreLocation)がメトリクスを供給中かどうか。
    /// trueの間、Unity内部計測からのスプリット判定供給は停止する(二重供給防止)。
    /// リセット/Unity単体開始時に false へ戻る(setはRunSessionController等から)。
    /// </summary>
    public static bool ExternalMetricsActive { get; set; }

#if UNITY_EDITOR
    // エディタ検証: Inspectorの右クリックメニューからSwiftコマンドをシミュレート
    [ContextMenu("Simulate StartSession (12km/h = 5:00/km)")]
    private void SimulateStartSession()
        => OnSwiftCommand("{\"command\":\"StartSession\",\"targetPaceKmH\":12.0,\"distanceKm\":5.0,\"avatarHeightCm\":175,\"forwardOffsetM\":3.0}");

    [ContextMenu("Simulate UpdateMetrics (HR 150)")]
    private void SimulateUpdateMetrics()
        => OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":11.5,\"heartRate\":150,\"distanceKm\":1.2}");

    [ContextMenu("Simulate Near Goal (5m remaining)")]
    private void SimulateNearGoal()
    {
        if (_goalDistanceMeters <= 0)
            SimulateStartSession();

        double nearGoalKm = Math.Max(0.0, _goalDistanceMeters - 5.0) / 1000.0;
        OnSwiftCommand($"{{\"command\":\"UpdateMetrics\",\"paceKmH\":12.0,\"heartRate\":150,\"distanceKm\":{nearGoalKm}}}");
    }

    [ContextMenu("Simulate EndSession")]
    private void SimulateEndSession()
        => OnSwiftCommand("{\"command\":\"EndSession\"}");

    [ContextMenu("Simulate Goal Reached (distance = goal)")]
    private void SimulateGoalReached()
        => OnSwiftCommand($"{{\"command\":\"UpdateMetrics\",\"paceKmH\":12.0,\"heartRate\":151,\"distanceKm\":{Math.Max(_goalDistanceMeters, 1000) / 1000.0}}}");

    [ContextMenu("Simulate RequestHistory")]
    private void SimulateRequestHistory()
        => OnSwiftCommand("{\"command\":\"RequestHistory\"}");

    [ContextMenu("Simulate Voice Alert (赤信号 TTC2.5s → 交差点 TTC1.2s 割込)")]
    private void SimulateVoiceAlert()
    {
        // 重複時の優先度制御テスト: 後発でもTTCが短い方が割り込むこと
        SwiftMessageSender.SendVoiceAlert("Signal", 2.5);
        SwiftMessageSender.SendVoiceAlert("Intersection", 1.2);
    }

    [ContextMenu("Simulate Ghost Run (latest saved session)")]
    private void SimulateGhostRun()
    {
        RunSessionRecord latest = SessionDataStore.LoadLatestSession();
        if (latest == null)
        {
            Debug.LogWarning("[SWIFT BRIDGE] No saved session — run and finish once first.");
            return;
        }
        OnSwiftCommand($"{{\"command\":\"StartSession\",\"targetPaceKmH\":12.0,\"distanceKm\":5.0," +
                       $"\"avatarHeightCm\":175,\"forwardOffsetM\":3.0," +
                       $"\"mode\":\"ghost\",\"ghostDateIso\":\"{latest.dateIso}\"}}");
    }
#endif
}
